using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Logging;

using Octokit;

namespace MX.IDP.Agents.Services;

/// <summary>
/// Shared GitHub query service. Single source of truth for all GitHub data access —
/// used by both chat tools and campaign data sources.
/// </summary>
public interface IGitHubQueryService
{
    Task<List<GitHubRepoInfo>> GetRepositoriesAsync(string? visibility = null, List<string>? repos = null);
    Task<List<DependabotAlertInfo>> GetDependabotAlertsAsync(List<string>? repos = null, string? severity = null);
    Task<List<CodeScanningAlertInfo>> GetCodeScanningAlertsAsync(List<string>? repos = null, string? severity = null);
    Task<List<BranchProtectionInfo>> GetBranchProtectionStatusAsync(List<string>? repos = null);
    Task<List<RepoConfigInfo>> GetRepoConfigStatusAsync(List<string>? repos = null);
    Task<List<PullRequestInfo>> GetPullRequestsAsync(string repo, string? state = "open", int maxResults = 25);
    Task<List<WorkflowFailureInfo>> GetWorkflowFailuresAsync(string repo, int maxResults = 10);
    Task<List<CodeSearchResult>> SearchCodeAsync(string query, string? repo = null);
    Task<List<RepoStatsInfo>> GetRepoStatsAsync(List<string>? repos = null);
    Task<IssueResult> CloseOrReopenIssueAsync(string repo, int issueNumber, string action, string? comment = null);
    Task<IssueResult> AddLabelAsync(string repo, int issueNumber, List<string> labels);
    Task<List<EnvironmentInfo>> GetEnvironmentsAsync(string repo);
    Task<RepoVersionInfo> GetVersionInfoAsync(string repo);
}

// Result types — shared between tools and campaign sources

public class GitHubRepoInfo
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Language { get; set; } = "";
    public string Visibility { get; set; } = "";
    public string Url { get; set; } = "";
    public string DefaultBranch { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
    public bool Archived { get; set; }
    public bool HasIssues { get; set; }
    public bool DeleteBranchOnMerge { get; set; }
}

public class DependabotAlertInfo
{
    public int Number { get; set; }
    public string Repo { get; set; } = "";
    public string PackageName { get; set; } = "";
    public string Ecosystem { get; set; } = "";
    public string Severity { get; set; } = "";
    public string Summary { get; set; } = "";
    public string? CveId { get; set; }
    public string? FixVersion { get; set; }
}

public class CodeScanningAlertInfo
{
    public int Number { get; set; }
    public string Repo { get; set; } = "";
    public string RuleId { get; set; } = "";
    public string Description { get; set; } = "";
    public string Severity { get; set; } = "";
    public string Location { get; set; } = "";
    public string ToolName { get; set; } = "";
    public string? Category { get; set; }
}

public class BranchProtectionInfo
{
    public string Repo { get; set; } = "";
    public string DefaultBranch { get; set; } = "";
    public bool IsProtected { get; set; }
    public bool HasStatusChecks { get; set; }
    public int StatusCheckCount { get; set; }
    public List<string> Issues { get; set; } = [];
}

public class RepoConfigInfo
{
    public string Repo { get; set; } = "";
    public bool HasDescription { get; set; }
    public bool HasTopics { get; set; }
    public int TopicCount { get; set; }
    public bool DeleteBranchOnMerge { get; set; }
    public string DefaultBranch { get; set; } = "";
    public List<string> Issues { get; set; } = [];
}

public class PullRequestInfo
{
    public int Number { get; set; }
    public string Title { get; set; } = "";
    public string State { get; set; } = "";
    public string Author { get; set; } = "";
    public string Url { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public int ReviewComments { get; set; }
    public bool IsDraft { get; set; }
    public List<string> Labels { get; set; } = [];
}

public class WorkflowFailureInfo
{
    public long RunId { get; set; }
    public string WorkflowName { get; set; } = "";
    public string Branch { get; set; } = "";
    public string Conclusion { get; set; } = "";
    public string Url { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string? HeadSha { get; set; }
}

public class CodeSearchResult
{
    public string Repo { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string Url { get; set; } = "";
}

public class RepoStatsInfo
{
    public string Repo { get; set; } = "";
    public int OpenIssues { get; set; }
    public int OpenPullRequests { get; set; }
    public int Stars { get; set; }
    public int Forks { get; set; }
    public string Language { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
    public long SizeKb { get; set; }
}

public class IssueResult
{
    public int Number { get; set; }
    public string Title { get; set; } = "";
    public string State { get; set; } = "";
    public string Url { get; set; } = "";
    public List<string> Labels { get; set; } = [];
    public List<string> Assignees { get; set; } = [];
}

public class EnvironmentInfo
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string? DeploymentBranchPolicy { get; set; }
    public string? CreatedAt { get; set; }
    public string? UpdatedAt { get; set; }
}

public class RepoVersionInfo
{
    public string Repo { get; set; } = "";
    public string? NerdbankVersion { get; set; }
    public string? LatestReleaseTag { get; set; }
    public string? LatestReleaseName { get; set; }
    public string? LatestReleaseDate { get; set; }
    public bool? LatestReleaseIsPrerelease { get; set; }
    public int TotalReleases { get; set; }
    public List<string> RecentTags { get; set; } = [];
}

public class GitHubQueryService : IGitHubQueryService
{
    private const string DefaultOwner = "frasermolyneux";
    private readonly IGitHubClientFactory _clientFactory;
    private readonly ILogger<GitHubQueryService> _logger;

    public GitHubQueryService(IGitHubClientFactory clientFactory, ILogger<GitHubQueryService> logger)
    {
        _clientFactory = clientFactory;
        _logger = logger;
    }

    public async Task<List<GitHubRepoInfo>> GetRepositoriesAsync(string? visibility = null, List<string>? repos = null)
    {
        var client = await _clientFactory.CreateClientAsync();
        var installationRepos = await client.GitHubApps.Installation.GetAllRepositoriesForCurrent();

        var filtered = installationRepos.Repositories.Where(r => !r.Archived).AsEnumerable();

        if (repos is not null)
            filtered = filtered.Where(r => repos.Contains(r.Name, StringComparer.OrdinalIgnoreCase));

        if (visibility is not null)
            filtered = visibility.ToLowerInvariant() switch
            {
                "public" => filtered.Where(r => !r.Private),
                "private" => filtered.Where(r => r.Private),
                _ => filtered
            };

        return filtered.Select(r => new GitHubRepoInfo
        {
            Name = r.Name,
            Description = r.Description ?? "",
            Language = r.Language ?? "—",
            Visibility = r.Private ? "private" : "public",
            Url = r.HtmlUrl,
            DefaultBranch = r.DefaultBranch,
            UpdatedAt = r.UpdatedAt.ToString("yyyy-MM-dd"),
            Archived = r.Archived,
            HasIssues = r.HasIssues,
            DeleteBranchOnMerge = r.DeleteBranchOnMerge.GetValueOrDefault()
        }).OrderBy(r => r.Name).ToList();
    }

    public async Task<List<DependabotAlertInfo>> GetDependabotAlertsAsync(List<string>? repos = null, string? severity = null)
    {
        var client = await _clientFactory.CreateClientAsync();
        var targetRepos = await GetTargetReposAsync(client, repos);
        var results = new List<DependabotAlertInfo>();

        foreach (var repo in targetRepos)
        {
            try
            {
                var alerts = await client.Connection.Get<List<DependabotAlertDto>>(
                    new Uri($"repos/{repo.Owner.Login}/{repo.Name}/dependabot/alerts?state=open&per_page=100", UriKind.Relative),
                    null, "application/vnd.github+json");

                if (alerts?.Body is null) continue;

                foreach (var alert in alerts.Body)
                {
                    var sev = MapDependabotSeverity(alert.SecurityAdvisory?.Severity ?? alert.SecurityVulnerability?.Severity ?? "medium");
                    if (severity is not null && !string.Equals(sev, severity, StringComparison.OrdinalIgnoreCase))
                        continue;

                    results.Add(new DependabotAlertInfo
                    {
                        Number = alert.Number,
                        Repo = repo.Name,
                        PackageName = alert.SecurityVulnerability?.Package?.Name ?? "unknown",
                        Ecosystem = alert.SecurityVulnerability?.Package?.Ecosystem ?? "unknown",
                        Severity = sev,
                        Summary = alert.SecurityAdvisory?.Summary ?? $"Vulnerable {alert.SecurityVulnerability?.Package?.Name}",
                        CveId = alert.SecurityAdvisory?.CveId,
                        FixVersion = alert.SecurityVulnerability?.FirstPatchedVersion?.Identifier
                    });
                }
            }
            catch (NotFoundException) { }
            catch (ForbiddenException) { _logger.LogWarning("No Dependabot permission for {Repo}", repo.Name); }
            catch (Exception ex) { _logger.LogWarning(ex, "Dependabot scan failed for {Repo}", repo.Name); }
        }

        return results;
    }

    public async Task<List<CodeScanningAlertInfo>> GetCodeScanningAlertsAsync(List<string>? repos = null, string? severity = null)
    {
        var client = await _clientFactory.CreateClientAsync();
        var targetRepos = await GetTargetReposAsync(client, repos);
        var results = new List<CodeScanningAlertInfo>();

        foreach (var repo in targetRepos)
        {
            try
            {
                var alerts = await client.Connection.Get<List<CodeScanningAlertDto>>(
                    new Uri($"repos/{repo.Owner.Login}/{repo.Name}/code-scanning/alerts?state=open&per_page=100", UriKind.Relative),
                    null, "application/vnd.github+json");

                if (alerts?.Body is null) continue;

                foreach (var alert in alerts.Body)
                {
                    var sev = MapCodeScanSeverity(alert.Rule?.SecuritySeverityLevel ?? alert.Rule?.Severity ?? "warning");
                    if (severity is not null && !string.Equals(sev, severity, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var loc = alert.MostRecentInstance?.Location;
                    results.Add(new CodeScanningAlertInfo
                    {
                        Number = alert.Number,
                        Repo = repo.Name,
                        RuleId = alert.Rule?.Id ?? "",
                        Description = alert.Rule?.Description ?? "",
                        Severity = sev,
                        Location = loc is not null ? $"{loc.Path}:{loc.StartLine}" : "unknown",
                        ToolName = alert.Tool?.Name ?? "CodeQL",
                        Category = alert.Rule?.Tags?.FirstOrDefault()
                    });
                }
            }
            catch (NotFoundException) { }
            catch (Exception ex) { _logger.LogWarning(ex, "Code scanning scan failed for {Repo}", repo.Name); }
        }

        return results;
    }

    public async Task<List<BranchProtectionInfo>> GetBranchProtectionStatusAsync(List<string>? repos = null)
    {
        var client = await _clientFactory.CreateClientAsync();
        var targetRepos = await GetTargetReposAsync(client, repos);
        var results = new List<BranchProtectionInfo>();

        foreach (var repo in targetRepos)
        {
            var info = new BranchProtectionInfo
            {
                Repo = repo.Name,
                DefaultBranch = repo.DefaultBranch
            };

            try
            {
                var branch = await client.Repository.Branch.Get(repo.Owner.Login, repo.Name, repo.DefaultBranch);
                info.IsProtected = branch.Protected;

                if (branch.Protected)
                {
                    var protection = await client.Repository.Branch.GetBranchProtection(repo.Owner.Login, repo.Name, repo.DefaultBranch);
                    info.HasStatusChecks = protection.RequiredStatusChecks?.Contexts?.Count > 0;
                    info.StatusCheckCount = protection.RequiredStatusChecks?.Contexts?.Count ?? 0;
                }

                if (!info.IsProtected) info.Issues.Add("No branch protection");
                if (info.IsProtected && !info.HasStatusChecks) info.Issues.Add("No required status checks");
                if (!repo.HasIssues) info.Issues.Add("Issues disabled");
            }
            catch (NotFoundException) { info.Issues.Add("Branch not found"); }
            catch (Exception ex) { _logger.LogWarning(ex, "Branch protection check failed for {Repo}", repo.Name); }

            results.Add(info);
        }

        return results;
    }

    public async Task<List<RepoConfigInfo>> GetRepoConfigStatusAsync(List<string>? repos = null)
    {
        var client = await _clientFactory.CreateClientAsync();
        var targetRepos = await GetTargetReposAsync(client, repos);
        var results = new List<RepoConfigInfo>();

        foreach (var repo in targetRepos)
        {
            var info = new RepoConfigInfo
            {
                Repo = repo.Name,
                HasDescription = !string.IsNullOrWhiteSpace(repo.Description),
                DeleteBranchOnMerge = repo.DeleteBranchOnMerge.GetValueOrDefault(),
                DefaultBranch = repo.DefaultBranch
            };

            try
            {
                var topics = await client.Repository.GetAllTopics(repo.Owner.Login, repo.Name);
                info.HasTopics = topics.Names.Count > 0;
                info.TopicCount = topics.Names.Count;
            }
            catch { info.HasTopics = false; }

            if (!info.HasDescription) info.Issues.Add("Missing description");
            if (!info.HasTopics) info.Issues.Add("No topics");
            if (!info.DeleteBranchOnMerge) info.Issues.Add("Delete branch on merge disabled");
            if (info.DefaultBranch != "main") info.Issues.Add($"Non-standard default branch: {info.DefaultBranch}");

            results.Add(info);
        }

        return results;
    }

    public async Task<List<PullRequestInfo>> GetPullRequestsAsync(string repo, string? state = "open", int maxResults = 25)
    {
        var client = await _clientFactory.CreateClientAsync();
        var stateFilter = state?.ToLowerInvariant() switch
        {
            "closed" => ItemStateFilter.Closed,
            "all" => ItemStateFilter.All,
            _ => ItemStateFilter.Open
        };

        var prs = await client.PullRequest.GetAllForRepository(DefaultOwner, repo,
            new PullRequestRequest { State = stateFilter },
            new ApiOptions { PageSize = maxResults, PageCount = 1 });

        return prs.Take(maxResults).Select(pr => new PullRequestInfo
        {
            Number = pr.Number,
            Title = pr.Title,
            State = pr.State.StringValue,
            Author = pr.User?.Login ?? "",
            Url = pr.HtmlUrl,
            CreatedAt = pr.CreatedAt.ToString("yyyy-MM-dd"),
            ReviewComments = 0,
            IsDraft = pr.Draft,
            Labels = pr.Labels?.Select(l => l.Name).ToList() ?? []
        }).ToList();
    }

    public async Task<List<WorkflowFailureInfo>> GetWorkflowFailuresAsync(string repo, int maxResults = 10)
    {
        var client = await _clientFactory.CreateClientAsync();
        var runs = await client.Actions.Workflows.Runs.List(DefaultOwner, repo,
            new WorkflowRunsRequest { Status = CheckRunStatusFilter.Completed },
            new ApiOptions { PageSize = maxResults * 3, PageCount = 1 });

        return runs.WorkflowRuns
            .Where(r => r.Conclusion?.StringValue is "failure" or "timed_out" or "cancelled")
            .Take(maxResults)
            .Select(r => new WorkflowFailureInfo
            {
                RunId = r.Id,
                WorkflowName = r.Name,
                Branch = r.HeadBranch,
                Conclusion = r.Conclusion?.StringValue ?? "unknown",
                Url = r.HtmlUrl,
                CreatedAt = r.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                HeadSha = r.HeadSha
            }).ToList();
    }

    public async Task<List<CodeSearchResult>> SearchCodeAsync(string query, string? repo = null)
    {
        var client = await _clientFactory.CreateClientAsync();

        var searchQuery = repo is not null
            ? $"{query} repo:{DefaultOwner}/{repo}"
            : $"{query} org:{DefaultOwner}";

        var searchResults = await client.Search.SearchCode(new SearchCodeRequest(searchQuery)
        {
            PerPage = 25
        });

        return searchResults.Items.Select(item => new CodeSearchResult
        {
            Repo = item.Repository?.Name ?? "",
            FilePath = item.Path,
            Url = item.HtmlUrl
        }).ToList();
    }

    public async Task<List<RepoStatsInfo>> GetRepoStatsAsync(List<string>? repos = null)
    {
        var client = await _clientFactory.CreateClientAsync();
        var targetRepos = await GetTargetReposAsync(client, repos);

        return targetRepos.Select(r => new RepoStatsInfo
        {
            Repo = r.Name,
            OpenIssues = r.OpenIssuesCount,
            Stars = r.StargazersCount,
            Forks = r.ForksCount,
            Language = r.Language ?? "—",
            UpdatedAt = r.UpdatedAt.ToString("yyyy-MM-dd"),
            SizeKb = r.Size
        }).OrderByDescending(r => r.UpdatedAt).ToList();
    }

    public async Task<IssueResult> CloseOrReopenIssueAsync(string repo, int issueNumber, string action, string? comment = null)
    {
        var client = await _clientFactory.CreateClientAsync();

        if (!string.IsNullOrEmpty(comment))
        {
            await client.Issue.Comment.Create(DefaultOwner, repo, issueNumber, comment);
        }

        var newState = action.ToLowerInvariant() == "reopen" ? ItemState.Open : ItemState.Closed;
        var updated = await client.Issue.Update(DefaultOwner, repo, issueNumber, new IssueUpdate { State = newState });

        return MapIssueResult(updated);
    }

    public async Task<IssueResult> AddLabelAsync(string repo, int issueNumber, List<string> labels)
    {
        var client = await _clientFactory.CreateClientAsync();
        var updated = await client.Issue.Labels.AddToIssue(DefaultOwner, repo, issueNumber, labels.ToArray());

        var issue = await client.Issue.Get(DefaultOwner, repo, issueNumber);
        return MapIssueResult(issue);
    }

    public async Task<List<EnvironmentInfo>> GetEnvironmentsAsync(string repo)
    {
        var client = await _clientFactory.CreateClientAsync();
        var results = new List<EnvironmentInfo>();

        try
        {
            var response = await client.Connection.Get<EnvironmentsResponseDto>(
                new Uri($"repos/{DefaultOwner}/{repo}/environments", UriKind.Relative),
                null, "application/vnd.github+json");

            if (response?.Body?.Environments is not null)
            {
                foreach (var env in response.Body.Environments)
                {
                    results.Add(new EnvironmentInfo
                    {
                        Name = env.Name ?? "",
                        Url = $"https://github.com/{DefaultOwner}/{repo}/deployments/{env.Name}",
                        DeploymentBranchPolicy = env.DeploymentBranchPolicy?.Type,
                        CreatedAt = env.CreatedAt,
                        UpdatedAt = env.UpdatedAt
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get environments for {Repo}", repo);
        }

        _logger.LogInformation("Found {Count} environments for {Repo}", results.Count, repo);
        return results;
    }

    public async Task<RepoVersionInfo> GetVersionInfoAsync(string repo)
    {
        var client = await _clientFactory.CreateClientAsync();
        var info = new RepoVersionInfo { Repo = repo };

        // Try to read version.json (Nerdbank GitVersioning)
        try
        {
            var rawContent = await client.Repository.Content.GetRawContentByRef(DefaultOwner, repo, "version.json", "main");
            var versionJson = System.Text.Encoding.UTF8.GetString(rawContent);
            using var doc = JsonDocument.Parse(versionJson);
            if (doc.RootElement.TryGetProperty("version", out var ver))
                info.NerdbankVersion = ver.GetString();
        }
        catch (NotFoundException) { /* No version.json */ }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read version.json for {Repo}", repo);
        }

        // Get releases
        try
        {
            var releases = await client.Repository.Release.GetAll(DefaultOwner, repo, new ApiOptions { PageSize = 10, PageCount = 1 });
            info.TotalReleases = releases.Count;

            if (releases.Count > 0)
            {
                var latest = releases[0];
                info.LatestReleaseTag = latest.TagName;
                info.LatestReleaseName = latest.Name;
                info.LatestReleaseDate = latest.PublishedAt?.ToString("yyyy-MM-dd HH:mm") ?? latest.CreatedAt.ToString("yyyy-MM-dd HH:mm");
                info.LatestReleaseIsPrerelease = latest.Prerelease;
                info.RecentTags = releases.Take(5).Select(r => r.TagName).ToList();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get releases for {Repo}", repo);
        }

        return info;
    }

    // Helpers

    private async Task<List<Repository>> GetTargetReposAsync(IGitHubClient client, List<string>? repos)
    {
        var all = await client.GitHubApps.Installation.GetAllRepositoriesForCurrent();
        var filtered = all.Repositories.Where(r => !r.Archived);
        if (repos is not null)
            filtered = filtered.Where(r => repos.Contains(r.Name, StringComparer.OrdinalIgnoreCase));
        return filtered.ToList();
    }

    private static IssueResult MapIssueResult(Issue issue) => new()
    {
        Number = issue.Number,
        Title = issue.Title,
        State = issue.State.StringValue,
        Url = issue.HtmlUrl,
        Labels = issue.Labels.Select(l => l.Name).ToList(),
        Assignees = issue.Assignees.Select(a => a.Login).ToList()
    };

    private static string MapDependabotSeverity(string s) => s.ToLowerInvariant() switch
    {
        "critical" or "high" => "High",
        "medium" => "Medium",
        "low" => "Low",
        _ => "Medium"
    };

    private static string MapCodeScanSeverity(string s) => s.ToLowerInvariant() switch
    {
        "critical" or "high" or "error" => "High",
        "medium" or "warning" => "Medium",
        "low" or "note" => "Low",
        _ => "Medium"
    };

    // DTOs for REST API responses (no native Octokit support)

    private class DependabotAlertDto
    {
        [JsonPropertyName("number")] public int Number { get; set; }
        [JsonPropertyName("security_advisory")] public SecurityAdvisoryDto? SecurityAdvisory { get; set; }
        [JsonPropertyName("security_vulnerability")] public SecurityVulnerabilityDto? SecurityVulnerability { get; set; }
    }

    private class SecurityAdvisoryDto
    {
        [JsonPropertyName("summary")] public string? Summary { get; set; }
        [JsonPropertyName("severity")] public string? Severity { get; set; }
        [JsonPropertyName("cve_id")] public string? CveId { get; set; }
    }

    private class SecurityVulnerabilityDto
    {
        [JsonPropertyName("severity")] public string? Severity { get; set; }
        [JsonPropertyName("package")] public PackageDto? Package { get; set; }
        [JsonPropertyName("first_patched_version")] public PatchedVersionDto? FirstPatchedVersion { get; set; }
    }

    private class PackageDto
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("ecosystem")] public string? Ecosystem { get; set; }
    }

    private class PatchedVersionDto
    {
        [JsonPropertyName("identifier")] public string? Identifier { get; set; }
    }

    private class CodeScanningAlertDto
    {
        [JsonPropertyName("number")] public int Number { get; set; }
        [JsonPropertyName("rule")] public RuleDto? Rule { get; set; }
        [JsonPropertyName("tool")] public ToolDto? Tool { get; set; }
        [JsonPropertyName("most_recent_instance")] public AlertInstanceDto? MostRecentInstance { get; set; }
    }

    private class RuleDto
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("severity")] public string? Severity { get; set; }
        [JsonPropertyName("security_severity_level")] public string? SecuritySeverityLevel { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("tags")] public List<string>? Tags { get; set; }
    }

    private class ToolDto
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
    }

    private class AlertInstanceDto
    {
        [JsonPropertyName("location")] public LocationDto? Location { get; set; }
    }

    private class LocationDto
    {
        [JsonPropertyName("path")] public string? Path { get; set; }
        [JsonPropertyName("start_line")] public int? StartLine { get; set; }
    }

    private class EnvironmentsResponseDto
    {
        [JsonPropertyName("total_count")] public int TotalCount { get; set; }
        [JsonPropertyName("environments")] public List<EnvironmentDto>? Environments { get; set; }
    }

    private class EnvironmentDto
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("created_at")] public string? CreatedAt { get; set; }
        [JsonPropertyName("updated_at")] public string? UpdatedAt { get; set; }
        [JsonPropertyName("deployment_branch_policy")] public DeploymentBranchPolicyDto? DeploymentBranchPolicy { get; set; }
    }

    private class DeploymentBranchPolicyDto
    {
        [JsonPropertyName("type")] public string? Type { get; set; }
    }
}
