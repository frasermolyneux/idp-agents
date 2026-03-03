using Microsoft.Extensions.Logging;

using MX.IDP.Agents.Models;

namespace MX.IDP.Agents.Services.CampaignSources;

public class RepoConfigCampaignSource : ICampaignDataSource
{
    public string SourceType => "repo_config";

    private readonly IGitHubQueryService _ghService;
    private readonly ILogger<RepoConfigCampaignSource> _logger;

    public RepoConfigCampaignSource(IGitHubQueryService ghService, ILogger<RepoConfigCampaignSource> logger)
    {
        _ghService = ghService;
        _logger = logger;
    }

    public async Task<List<CampaignFinding>> ScanAsync(CampaignFilter? filter)
    {
        var configs = await _ghService.GetRepoConfigStatusAsync(filter?.Repos);
        var findings = new List<CampaignFinding>();

        foreach (var cfg in configs)
        {
            if (!cfg.HasDescription)
            {
                findings.Add(new CampaignFinding
                {
                    SourceType = "repo_config",
                    Title = $"[RepoConfig] Missing description on {cfg.Repo}",
                    Description = $"Repository `{cfg.Repo}` has no description set. Add a meaningful description for discoverability.",
                    Severity = "Low",
                    Repo = cfg.Repo,
                    DeduplicationKey = $"repoconfig:{cfg.Repo}:no_description"
                });
            }

            if (!cfg.HasTopics)
            {
                findings.Add(new CampaignFinding
                {
                    SourceType = "repo_config",
                    Title = $"[RepoConfig] No topics on {cfg.Repo}",
                    Description = $"Repository `{cfg.Repo}` has no topics set. Add topics for categorisation and discoverability.",
                    Severity = "Low",
                    Repo = cfg.Repo,
                    DeduplicationKey = $"repoconfig:{cfg.Repo}:no_topics"
                });
            }

            if (!cfg.DeleteBranchOnMerge)
            {
                findings.Add(new CampaignFinding
                {
                    SourceType = "repo_config",
                    Title = $"[RepoConfig] Delete branch on merge disabled on {cfg.Repo}",
                    Description = $"Repository `{cfg.Repo}` does not auto-delete branches after merge. Enable this to keep the repository clean.",
                    Severity = "Low",
                    Repo = cfg.Repo,
                    DeduplicationKey = $"repoconfig:{cfg.Repo}:no_delete_branch_on_merge"
                });
            }

            if (cfg.DefaultBranch != "main")
            {
                findings.Add(new CampaignFinding
                {
                    SourceType = "repo_config",
                    Title = $"[RepoConfig] Non-standard default branch on {cfg.Repo}",
                    Description = $"Repository `{cfg.Repo}` uses `{cfg.DefaultBranch}` as default branch instead of `main`.",
                    Severity = "Medium",
                    Repo = cfg.Repo,
                    DeduplicationKey = $"repoconfig:{cfg.Repo}:non_standard_default_branch"
                });
            }
        }

        _logger.LogInformation("Repo config scan found {Count} findings", findings.Count);
        return findings;
    }
}
