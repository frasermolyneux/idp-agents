using Microsoft.Extensions.Logging;

using MX.IDP.Agents.Models;

namespace MX.IDP.Agents.Services.CampaignSources;

public class DevStandardsCampaignSource : ICampaignDataSource
{
    public string SourceType => "dev_standards";

    private readonly IGitHubQueryService _ghService;
    private readonly ILogger<DevStandardsCampaignSource> _logger;

    public DevStandardsCampaignSource(IGitHubQueryService ghService, ILogger<DevStandardsCampaignSource> logger)
    {
        _ghService = ghService;
        _logger = logger;
    }

    public async Task<List<CampaignFinding>> ScanAsync(CampaignFilter? filter)
    {
        var findings = new List<CampaignFinding>();

        var repos = await _ghService.GetRepositoriesAsync(repos: filter?.Repos);
        var protections = await _ghService.GetBranchProtectionStatusAsync(filter?.Repos);

        foreach (var bp in protections)
        {
            // Exclude repos filtering
            if (filter?.ExcludeRepos is not null && filter.ExcludeRepos.Contains(bp.Repo, StringComparer.OrdinalIgnoreCase))
                continue;

            var repo = repos.FirstOrDefault(r => r.Name == bp.Repo);

            if (!bp.IsProtected)
            {
                findings.Add(new CampaignFinding
                {
                    SourceType = "dev_standards",
                    Title = $"[DevStandards] No branch protection on {bp.Repo}/{bp.DefaultBranch}",
                    Description = $"Repository `{bp.Repo}` default branch `{bp.DefaultBranch}` has no branch protection rules. Consider adding rulesets for required status checks, PR reviews, and linear history.",
                    Severity = "High",
                    Repo = bp.Repo,
                    DeduplicationKey = $"devstandards:{bp.Repo}:no_branch_protection"
                });
            }
            else if (!bp.HasStatusChecks)
            {
                findings.Add(new CampaignFinding
                {
                    SourceType = "dev_standards",
                    Title = $"[DevStandards] No required status checks on {bp.Repo}",
                    Description = $"Repository `{bp.Repo}` has branch protection but no required status checks. Add checks like build-and-test, terraform-plan, or code scanning.",
                    Severity = "Medium",
                    Repo = bp.Repo,
                    DeduplicationKey = $"devstandards:{bp.Repo}:no_status_checks"
                });
            }

            if (repo is not null && !repo.HasIssues)
            {
                findings.Add(new CampaignFinding
                {
                    SourceType = "dev_standards",
                    Title = $"[DevStandards] Issues disabled on {bp.Repo}",
                    Description = $"Repository `{bp.Repo}` has issues disabled. Issues should be enabled for tracking work and campaign findings.",
                    Severity = "Medium",
                    Repo = bp.Repo,
                    DeduplicationKey = $"devstandards:{bp.Repo}:issues_disabled"
                });
            }
        }

        _logger.LogInformation("Dev standards scan found {Count} findings", findings.Count);
        return findings;
    }
}
