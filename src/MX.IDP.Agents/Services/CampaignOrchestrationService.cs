using Microsoft.Extensions.Logging;

using MX.IDP.Agents.Models;
using MX.IDP.Agents.Services.CampaignSources;

namespace MX.IDP.Agents.Services;

public interface ICampaignOrchestrationService
{
    Task<Campaign> RunCampaignAsync(Campaign campaign);
}

public class CampaignOrchestrationService : ICampaignOrchestrationService
{
    private readonly ICampaignService _campaignService;
    private readonly IGitHubClientFactory _gitHubClientFactory;
    private readonly IEnumerable<ICampaignDataSource> _dataSources;
    private readonly ILogger<CampaignOrchestrationService> _logger;

    public CampaignOrchestrationService(
        ICampaignService campaignService,
        IGitHubClientFactory gitHubClientFactory,
        IEnumerable<ICampaignDataSource> dataSources,
        ILogger<CampaignOrchestrationService> logger)
    {
        _campaignService = campaignService;
        _gitHubClientFactory = gitHubClientFactory;
        _dataSources = dataSources;
        _logger = logger;
    }

    public async Task<Campaign> RunCampaignAsync(Campaign campaign)
    {
        campaign.Status = "running";
        campaign.LastRunAt = DateTimeOffset.UtcNow;
        await _campaignService.UpdateAsync(campaign);

        try
        {
            // 1. Find the right data source
            var source = _dataSources.FirstOrDefault(s => s.SourceType == campaign.SourceType);
            if (source is null)
            {
                campaign.Status = "failed";
                await _campaignService.UpdateAsync(campaign);
                _logger.LogError("No data source found for campaign source type '{SourceType}'", campaign.SourceType);
                return campaign;
            }

            // 2. Scan for findings
            _logger.LogInformation("Running campaign '{Name}' with source '{SourceType}'", campaign.Name, campaign.SourceType);
            var findings = await source.ScanAsync(campaign.Filter);

            // 3. Deduplicate against existing findings
            var existingFindings = await _campaignService.GetFindingsAsync(campaign.Id);
            var existingKeys = existingFindings.Select(f => f.DeduplicationKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var newFindings = findings.Where(f => !existingKeys.Contains(f.DeduplicationKey)).ToList();

            // 4. Also check against existing GitHub issues
            var client = await _gitHubClientFactory.CreateClientAsync();
            foreach (var finding in newFindings)
            {
                finding.CampaignId = campaign.Id;

                if (finding.Repo is not null)
                {
                    var isDuplicate = await CheckExistingIssueAsync(client, finding);
                    if (isDuplicate)
                    {
                        finding.Status = "duplicate";
                        continue;
                    }

                    // 5. Create GitHub issue
                    try
                    {
                        var issue = await CreateIssueForFinding(client, finding, campaign);
                        finding.IssueNumber = issue.Number;
                        finding.IssueUrl = issue.HtmlUrl;
                        finding.Status = "issue_created";

                        // 6. Assign if configured
                        if (campaign.Filter?.AssignTo is not null)
                        {
                            await client.Issue.Assignee.AddAssignees(
                                "frasermolyneux", finding.Repo, issue.Number,
                                new Octokit.AssigneesUpdate(new[] { campaign.Filter.AssignTo }));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to create issue for finding in {Repo}", finding.Repo);
                        finding.Status = "new";
                    }
                }
                else
                {
                    finding.Status = "skipped"; // No repo mapping
                }
            }

            // 7. Persist findings
            await _campaignService.UpsertFindingsBatchAsync(newFindings);

            // 8. Refresh progress from all existing + new issues
            await UpdateCampaignProgressAsync(campaign, client);

            campaign.Status = "completed";
            await _campaignService.UpdateAsync(campaign);

            _logger.LogInformation("Campaign '{Name}' completed. {New} new findings, {Total} total",
                campaign.Name, newFindings.Count, campaign.Stats.TotalFindings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Campaign '{Name}' failed", campaign.Name);
            campaign.Status = "failed";
            await _campaignService.UpdateAsync(campaign);
        }

        return campaign;
    }

    private static async Task<bool> CheckExistingIssueAsync(Octokit.IGitHubClient client, CampaignFinding finding)
    {
        if (finding.Repo is null) return false;

        try
        {
            var issues = await client.Issue.GetAllForRepository(
                "frasermolyneux", finding.Repo,
                new Octokit.RepositoryIssueRequest { State = Octokit.ItemStateFilter.All });

            return issues.Any(i => i.Title == finding.Title);
        }
        catch
        {
            return false;
        }
    }

    private static async Task<Octokit.Issue> CreateIssueForFinding(
        Octokit.IGitHubClient client, CampaignFinding finding, Campaign campaign)
    {
        var newIssue = new Octokit.NewIssue(finding.Title)
        {
            Body = $"{finding.Description}\n\n---\n_Created by IDP Campaign: **{campaign.Name}**_\n_Source: {finding.SourceType} | Severity: {finding.Severity}_"
        };

        newIssue.Labels.Add($"campaign:{campaign.SourceType}");
        newIssue.Labels.Add($"severity:{finding.Severity.ToLowerInvariant()}");

        return await client.Issue.Create("frasermolyneux", finding.Repo!, newIssue);
    }

    private async Task UpdateCampaignProgressAsync(Campaign campaign, Octokit.IGitHubClient client)
    {
        var allFindings = await _campaignService.GetFindingsAsync(campaign.Id);

        var issuesCreated = allFindings.Count(f => f.Status == "issue_created" || f.Status == "resolved");
        var issuesOpen = 0;
        var issuesClosed = 0;

        // Check current state of created issues
        foreach (var finding in allFindings.Where(f => f.IssueNumber.HasValue && f.Repo is not null))
        {
            try
            {
                var issue = await client.Issue.Get("frasermolyneux", finding.Repo!, finding.IssueNumber!.Value);
                if (issue.State.Value == Octokit.ItemState.Closed)
                {
                    finding.Status = "resolved";
                    issuesClosed++;
                }
                else
                {
                    issuesOpen++;
                }
                await _campaignService.UpsertFindingAsync(finding);
            }
            catch { /* skip if we can't check */ }
        }

        campaign.Stats = new CampaignStats
        {
            TotalFindings = allFindings.Count,
            IssuesCreated = issuesCreated,
            IssuesOpen = issuesOpen,
            IssuesClosed = issuesClosed,
            IssuesSkipped = allFindings.Count(f => f.Status is "skipped" or "duplicate"),
            ProgressPercent = allFindings.Count > 0
                ? Math.Round((double)(issuesClosed + allFindings.Count(f => f.Status is "skipped" or "duplicate")) / allFindings.Count * 100, 1)
                : 0
        };
    }
}
