using Microsoft.Extensions.Logging;

using MX.IDP.Agents.Models;
using MX.IDP.Agents.Services.CampaignSources;

namespace MX.IDP.Agents.Services;

public interface ICampaignOrchestrationService
{
    Task<Campaign> RunCampaignAsync(Campaign campaign, bool dryRun = false);
    Task<Octokit.Issue> CreateIssueForFindingAsync(Campaign campaign, CampaignFinding finding);
}

public class CampaignOrchestrationService : ICampaignOrchestrationService
{
    private readonly ICampaignService _campaignService;
    private readonly IGitHubClientFactory _gitHubClientFactory;
    private readonly IGitHubQueryService _gitHubQueryService;
    private readonly IIssueTemplateRenderer _templateRenderer;
    private readonly IEnumerable<ICampaignDataSource> _dataSources;
    private readonly ILogger<CampaignOrchestrationService> _logger;

    public CampaignOrchestrationService(
        ICampaignService campaignService,
        IGitHubClientFactory gitHubClientFactory,
        IGitHubQueryService gitHubQueryService,
        IIssueTemplateRenderer templateRenderer,
        IEnumerable<ICampaignDataSource> dataSources,
        ILogger<CampaignOrchestrationService> logger)
    {
        _campaignService = campaignService;
        _gitHubClientFactory = gitHubClientFactory;
        _gitHubQueryService = gitHubQueryService;
        _templateRenderer = templateRenderer;
        _dataSources = dataSources;
        _logger = logger;
    }

    public async Task<Campaign> RunCampaignAsync(Campaign campaign, bool dryRun = false)
    {
        campaign.Status = dryRun ? "previewing" : "running";
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

            // 2. Resolve target repos from topics (union with explicit repos, subtract excludeRepos)
            var effectiveFilter = campaign.Filter;
            if (effectiveFilter?.RepoTopics is not null && effectiveFilter.RepoTopics.Count > 0)
            {
                effectiveFilter = ResolveRepoTopicsIntoFilter(effectiveFilter, await _gitHubQueryService.GetRepoNamesByTopicsAsync(effectiveFilter.RepoTopics));
            }

            // 3. Scan for findings
            _logger.LogInformation("Running campaign '{Name}' with source '{SourceType}', actionMode '{ActionMode}'",
                campaign.Name, campaign.SourceType, campaign.ActionMode);
            List<CampaignFinding> findings;

            if (source is KqlCampaignSource kqlSource && campaign.SourceType == "kql")
            {
                findings = await kqlSource.ScanWithQueryAsync(campaign.KqlQuery, effectiveFilter);
            }
            else
            {
                findings = await source.ScanAsync(effectiveFilter);
            }

            // Apply cross-source severity filter
            if (effectiveFilter?.Severity is not null)
            {
                findings = findings.Where(f =>
                    string.Equals(f.Severity, effectiveFilter.Severity, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            // 3. Deduplicate against existing findings
            var existingFindings = await _campaignService.GetFindingsAsync(campaign.Id);
            var existingKeys = existingFindings.Select(f => f.DeduplicationKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var newFindings = findings.Where(f => !existingKeys.Contains(f.DeduplicationKey)).ToList();

            // 4. Build per-repo issue title cache for dedup (one API call per repo, not per finding)
            var client = await _gitHubClientFactory.CreateClientAsync();
            var repoIssueTitles = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var finding in newFindings)
            {
                finding.CampaignId = campaign.Id;

                if (finding.Repo is null)
                {
                    finding.Status = "skipped";
                    continue;
                }

                // Cache issue titles per repo
                if (!repoIssueTitles.TryGetValue(finding.Repo, out var titleSet))
                {
                    titleSet = await LoadRepoIssueTitlesAsync(client, finding.Repo);
                    repoIssueTitles[finding.Repo] = titleSet;
                }

                if (titleSet.Contains(finding.Title))
                {
                    finding.Status = "duplicate";
                    continue;
                }

                if (dryRun)
                {
                    finding.Status = "preview";
                    continue;
                }

                // Determine status based on action mode and approval gate
                switch (campaign.ActionMode)
                {
                    case "audit":
                        finding.Status = "audited";
                        break;

                    case "issue" or "copilot_agent" when campaign.RequireApproval:
                        finding.Status = "pending_approval";
                        break;

                    case "issue" or "copilot_agent":
                        await CreateAndLinkIssueAsync(client, finding, campaign);
                        break;

                    default:
                        finding.Status = "new";
                        break;
                }
            }

            // 5. Persist findings
            await _campaignService.UpsertFindingsBatchAsync(newFindings);

            // 6. Refresh progress
            if (!dryRun)
            {
                await UpdateCampaignProgressAsync(campaign, client);
            }
            else
            {
                campaign.Stats = new CampaignStats
                {
                    TotalFindings = newFindings.Count,
                    IssuesCreated = 0,
                    IssuesOpen = 0,
                    IssuesClosed = 0,
                    IssuesSkipped = newFindings.Count(f => f.Status is "skipped" or "duplicate"),
                    PendingApproval = 0,
                    ProgressPercent = 0
                };
            }

            campaign.Status = dryRun ? "created" : "completed";
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

    /// <summary>
    /// Creates a GitHub issue for a finding. Used by approval flow and direct creation.
    /// </summary>
    public async Task<Octokit.Issue> CreateIssueForFindingAsync(Campaign campaign, CampaignFinding finding)
    {
        var client = await _gitHubClientFactory.CreateClientAsync();
        return await CreateAndLinkIssueAsync(client, finding, campaign);
    }

    private async Task<Octokit.Issue> CreateAndLinkIssueAsync(
        Octokit.IGitHubClient client, CampaignFinding finding, Campaign campaign)
    {
        var issueTemplate = campaign.IssueTemplate ?? DefaultIssueTemplates.GetForSourceType(campaign.SourceType);

        var title = _templateRenderer.RenderTitle(issueTemplate, finding, campaign);
        var body = _templateRenderer.RenderBody(issueTemplate, finding, campaign);
        var labels = _templateRenderer.GetLabels(issueTemplate, campaign);
        var assignees = _templateRenderer.GetAssignees(issueTemplate, campaign);

        var newIssue = new Octokit.NewIssue(title) { Body = body };
        foreach (var label in labels)
            newIssue.Labels.Add(label);

        // Add severity label
        newIssue.Labels.Add($"severity:{finding.Severity.ToLowerInvariant()}");

        // Add copilot label for copilot_agent mode
        if (campaign.ActionMode == "copilot_agent")
            newIssue.Labels.Add("copilot");

        var issue = await client.Issue.Create("frasermolyneux", finding.Repo!, newIssue);

        // Assign if any assignees configured
        if (assignees.Count > 0)
        {
            try
            {
                await client.Issue.Assignee.AddAssignees(
                    "frasermolyneux", finding.Repo!, issue.Number,
                    new Octokit.AssigneesUpdate(assignees));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to assign {Assignees} to issue #{Number} in {Repo}",
                    string.Join(", ", assignees), issue.Number, finding.Repo);
            }
        }

        finding.IssueNumber = issue.Number;
        finding.IssueUrl = issue.HtmlUrl;
        finding.Status = "issue_created";

        return issue;
    }

    private static async Task<HashSet<string>> LoadRepoIssueTitlesAsync(Octokit.IGitHubClient client, string repo)
    {
        try
        {
            var issues = await client.Issue.GetAllForRepository(
                "frasermolyneux", repo,
                new Octokit.RepositoryIssueRequest { State = Octokit.ItemStateFilter.All });

            return issues.Select(i => i.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Merges topic-resolved repo names with the filter's explicit repos, subtracts excludeRepos.
    /// </summary>
    private static CampaignFilter ResolveRepoTopicsIntoFilter(CampaignFilter filter, List<string> topicRepos)
    {
        var merged = new HashSet<string>(topicRepos, StringComparer.OrdinalIgnoreCase);
        if (filter.Repos is not null)
        {
            foreach (var repo in filter.Repos)
                merged.Add(repo);
        }
        if (filter.ExcludeRepos is not null)
        {
            foreach (var repo in filter.ExcludeRepos)
                merged.Remove(repo);
        }

        // Return a new filter with the resolved repos list
        return new CampaignFilter
        {
            Repos = merged.ToList(),
            Category = filter.Category,
            Impact = filter.Impact,
            Subcategory = filter.Subcategory,
            SubscriptionIds = filter.SubscriptionIds,
            AssignTo = filter.AssignTo,
            ResourceGroups = filter.ResourceGroups,
            Tags = filter.Tags,
            RepoTopics = filter.RepoTopics,
            ExcludeRepos = null, // Already applied
            Severity = filter.Severity,
            CreatedAfter = filter.CreatedAfter
        };
    }

    private async Task UpdateCampaignProgressAsync(Campaign campaign, Octokit.IGitHubClient client)
    {
        var allFindings = await _campaignService.GetFindingsAsync(campaign.Id);
        var staleDays = 14;

        var issuesCreated = allFindings.Count(f => f.Status is "issue_created" or "resolved" or "stale");
        var issuesOpen = 0;
        var issuesClosed = 0;

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
                    if (issue.CreatedAt < DateTimeOffset.UtcNow.AddDays(-staleDays))
                        finding.Status = "stale";
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
            PendingApproval = allFindings.Count(f => f.Status == "pending_approval"),
            ProgressPercent = allFindings.Count > 0
                ? Math.Round((double)(issuesClosed + allFindings.Count(f => f.Status is "skipped" or "duplicate" or "dismissed" or "audited")) / allFindings.Count * 100, 1)
                : 0
        };
    }
}
