using Microsoft.Extensions.Logging;

using MX.IDP.Agents.Models;

namespace MX.IDP.Agents.Services.CampaignSources;

public class RepoConfigCampaignSource : ICampaignDataSource
{
    public string SourceType => "repo_config";

    private readonly IGitHubClientFactory _gitHubClientFactory;
    private readonly ILogger<RepoConfigCampaignSource> _logger;

    public RepoConfigCampaignSource(IGitHubClientFactory gitHubClientFactory, ILogger<RepoConfigCampaignSource> logger)
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

            // Check description
            if (string.IsNullOrWhiteSpace(repo.Description))
            {
                findings.Add(new CampaignFinding
                {
                    SourceType = "repo_config",
                    Title = $"[RepoConfig] Missing description on {repo.Name}",
                    Description = $"Repository `{repo.Name}` has no description set. Add a meaningful description for discoverability.",
                    Severity = "Low",
                    Repo = repo.Name,
                    DeduplicationKey = $"repoconfig:{repo.Name}:no_description"
                });
            }

            // Check topics
            try
            {
                var topics = await client.Repository.GetAllTopics(repo.Owner.Login, repo.Name);
                if (topics.Names.Count == 0)
                {
                    findings.Add(new CampaignFinding
                    {
                        SourceType = "repo_config",
                        Title = $"[RepoConfig] No topics on {repo.Name}",
                        Description = $"Repository `{repo.Name}` has no topics set. Add topics for categorisation and discoverability.",
                        Severity = "Low",
                        Repo = repo.Name,
                        DeduplicationKey = $"repoconfig:{repo.Name}:no_topics"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check topics for repo {Repo}", repo.Name);
            }

            // Check delete branch on merge
            if (!repo.DeleteBranchOnMerge.GetValueOrDefault())
            {
                findings.Add(new CampaignFinding
                {
                    SourceType = "repo_config",
                    Title = $"[RepoConfig] Delete branch on merge disabled on {repo.Name}",
                    Description = $"Repository `{repo.Name}` does not auto-delete branches after merge. Enable this to keep the repository clean.",
                    Severity = "Low",
                    Repo = repo.Name,
                    DeduplicationKey = $"repoconfig:{repo.Name}:no_delete_branch_on_merge"
                });
            }

            // Check default branch is 'main'
            if (repo.DefaultBranch != "main")
            {
                findings.Add(new CampaignFinding
                {
                    SourceType = "repo_config",
                    Title = $"[RepoConfig] Non-standard default branch on {repo.Name}",
                    Description = $"Repository `{repo.Name}` uses `{repo.DefaultBranch}` as default branch instead of `main`.",
                    Severity = "Medium",
                    Repo = repo.Name,
                    DeduplicationKey = $"repoconfig:{repo.Name}:non_standard_default_branch"
                });
            }
        }

        _logger.LogInformation("Repo config scan found {Count} findings", findings.Count);
        return findings;
    }
}
