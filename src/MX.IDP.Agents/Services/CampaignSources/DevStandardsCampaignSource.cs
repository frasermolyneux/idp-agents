using Microsoft.Extensions.Logging;

using MX.IDP.Agents.Models;

namespace MX.IDP.Agents.Services.CampaignSources;

public class DevStandardsCampaignSource : ICampaignDataSource
{
    public string SourceType => "dev_standards";

    private readonly IGitHubClientFactory _gitHubClientFactory;
    private readonly ILogger<DevStandardsCampaignSource> _logger;

    public DevStandardsCampaignSource(IGitHubClientFactory gitHubClientFactory, ILogger<DevStandardsCampaignSource> logger)
    {
        _gitHubClientFactory = gitHubClientFactory;
        _logger = logger;
    }

    public async Task<List<CampaignFinding>> ScanAsync(CampaignFilter? filter)
    {
        var findings = new List<CampaignFinding>();
        var client = await _gitHubClientFactory.CreateClientAsync();
        var repos = await client.GitHubApps.Installation.GetAllRepositoriesForCurrent();

        foreach (var repo in repos.Repositories)
        {
            if (repo.Archived) continue;
            if (filter?.Repos is not null && !filter.Repos.Contains(repo.Name, StringComparer.OrdinalIgnoreCase))
                continue;

            try
            {
                // Check branch protection
                await CheckBranchProtection(client, repo, findings);

                // Check issues enabled
                if (!repo.HasIssues)
                {
                    findings.Add(new CampaignFinding
                    {
                        SourceType = "dev_standards",
                        Title = $"[DevStandards] Issues disabled on {repo.Name}",
                        Description = $"Repository `{repo.Name}` has issues disabled. Issues should be enabled for tracking work and campaign findings.",
                        Severity = "Medium",
                        Repo = repo.Name,
                        DeduplicationKey = $"devstandards:{repo.Name}:issues_disabled"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check dev standards for repo {Repo}", repo.Name);
            }
        }

        _logger.LogInformation("Dev standards scan found {Count} findings", findings.Count);
        return findings;
    }

    private static async Task CheckBranchProtection(Octokit.IGitHubClient client, Octokit.Repository repo, List<CampaignFinding> findings)
    {
        try
        {
            var branch = await client.Repository.Branch.Get(repo.Owner.Login, repo.Name, repo.DefaultBranch);
            if (branch.Protected != true)
            {
                findings.Add(new CampaignFinding
                {
                    SourceType = "dev_standards",
                    Title = $"[DevStandards] No branch protection on {repo.Name}/{repo.DefaultBranch}",
                    Description = $"Repository `{repo.Name}` default branch `{repo.DefaultBranch}` has no branch protection rules. Consider adding rulesets for required status checks, PR reviews, and linear history.",
                    Severity = "High",
                    Repo = repo.Name,
                    DeduplicationKey = $"devstandards:{repo.Name}:no_branch_protection"
                });
                return;
            }

            // Check required status checks
            var protection = await client.Repository.Branch.GetBranchProtection(repo.Owner.Login, repo.Name, repo.DefaultBranch);
            if (protection.RequiredStatusChecks?.Contexts is null || protection.RequiredStatusChecks.Contexts.Count == 0)
            {
                findings.Add(new CampaignFinding
                {
                    SourceType = "dev_standards",
                    Title = $"[DevStandards] No required status checks on {repo.Name}",
                    Description = $"Repository `{repo.Name}` has branch protection but no required status checks. Add checks like build-and-test, terraform-plan, or code scanning.",
                    Severity = "Medium",
                    Repo = repo.Name,
                    DeduplicationKey = $"devstandards:{repo.Name}:no_status_checks"
                });
            }
        }
        catch (Octokit.NotFoundException)
        {
            // No branch or no protection — skip
        }
    }
}
