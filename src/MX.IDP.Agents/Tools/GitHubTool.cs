using System.ComponentModel;
using System.Text.Json;

using Microsoft.ApplicationInsights;
using Microsoft.SemanticKernel;

using MX.IDP.Agents.Services;

using Octokit;

namespace MX.IDP.Agents.Tools;

public class GitHubTool
{
    private readonly IGitHubClientFactory _clientFactory;
    private readonly IGitHubQueryService _ghService;
    private readonly TelemetryClient? _telemetryClient;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private const string DefaultOwner = "frasermolyneux";

    public GitHubTool(IGitHubClientFactory clientFactory, IGitHubQueryService ghService, TelemetryClient? telemetryClient = null)
    {
        _clientFactory = clientFactory;
        _ghService = ghService;
        _telemetryClient = telemetryClient;
    }

    private void Track(string tool, Dictionary<string, string>? extra = null)
    {
        var props = new Dictionary<string, string> { ["Tool"] = tool };
        if (extra is not null) foreach (var kv in extra) props[kv.Key] = kv.Value;
        _telemetryClient?.TrackEvent("ToolInvocation", props);
    }

    [KernelFunction("list_repositories")]
    [Description("Lists repositories accessible to the IDP GitHub App installation under frasermolyneux. Returns name, description, language, and visibility.")]
    public async Task<string> ListRepositoriesAsync(
        [Description("Optional: filter by visibility — public, private, all. Default: all")] string? visibility = "all",
        [Description("Optional: maximum number of results. Default 50")] int maxResults = 50)
    {
        Track("list_repositories", new() { ["Visibility"] = visibility ?? "all" });
        var repos = await _ghService.GetRepositoriesAsync(visibility);
        var results = repos.Take(maxResults);
        return JsonSerializer.Serialize(new { count = results.Count(), repositories = results }, JsonOpts);
    }

    [KernelFunction("create_issue")]
    [Description("Creates a GitHub issue in a repository owned by frasermolyneux. Can assign to users or the Copilot coding agent.")]
    public async Task<string> CreateIssueAsync(
        [Description("Repository name (e.g. 'idp-core')")] string repo,
        [Description("Issue title")] string title,
        [Description("Issue body in markdown")] string body,
        [Description("Optional: comma-separated assignees (use 'copilot' for the Copilot coding agent)")] string? assignees = null,
        [Description("Optional: comma-separated labels to apply")] string? labels = null)
    {
        Track("create_issue", new() { ["Repo"] = repo });

        var client = await _clientFactory.CreateClientAsync();
        var newIssue = new NewIssue(title) { Body = body };

        if (!string.IsNullOrEmpty(assignees))
            foreach (var a in assignees.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                newIssue.Assignees.Add(a);

        if (!string.IsNullOrEmpty(labels))
            foreach (var l in labels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                newIssue.Labels.Add(l);

        var issue = await client.Issue.Create(DefaultOwner, repo, newIssue);
        return JsonSerializer.Serialize(new
        {
            number = issue.Number, url = issue.HtmlUrl, title = issue.Title,
            state = issue.State.StringValue, assignees = issue.Assignees.Select(a => a.Login).ToList()
        }, JsonOpts);
    }

    [KernelFunction("list_issues")]
    [Description("Lists issues in a GitHub repository owned by frasermolyneux. Supports filtering by state and labels.")]
    public async Task<string> ListIssuesAsync(
        [Description("Repository name (e.g. 'idp-core')")] string repo,
        [Description("Optional: filter by state — open, closed, all. Default: open")] string? state = "open",
        [Description("Optional: comma-separated labels to filter by")] string? labels = null,
        [Description("Optional: maximum number of results. Default 25")] int maxResults = 25)
    {
        Track("list_issues", new() { ["Repo"] = repo, ["State"] = state ?? "open" });

        var client = await _clientFactory.CreateClientAsync();
        var request = new RepositoryIssueRequest
        {
            State = state?.ToLowerInvariant() switch
            {
                "closed" => ItemStateFilter.Closed,
                "all" => ItemStateFilter.All,
                _ => ItemStateFilter.Open
            }
        };

        if (!string.IsNullOrEmpty(labels))
            foreach (var l in labels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                request.Labels.Add(l);

        var issues = await client.Issue.GetAllForRepository(DefaultOwner, repo, request,
            new ApiOptions { PageSize = maxResults, PageCount = 1 });

        var results = issues.Take(maxResults).Select(i => new
        {
            number = i.Number, title = i.Title, state = i.State.StringValue,
            assignees = i.Assignees.Select(a => a.Login).ToList(),
            labels = i.Labels.Select(l => l.Name).ToList(),
            url = i.HtmlUrl, createdAt = i.CreatedAt.ToString("yyyy-MM-dd")
        });

        return JsonSerializer.Serialize(new { count = results.Count(), issues = results }, JsonOpts);
    }

    [KernelFunction("get_actions_status")]
    [Description("Gets the status of recent GitHub Actions workflow runs for a repository owned by frasermolyneux.")]
    public async Task<string> GetActionsStatusAsync(
        [Description("Repository name (e.g. 'idp-core')")] string repo,
        [Description("Optional: maximum number of recent runs to return. Default 10")] int maxResults = 10)
    {
        Track("get_actions_status", new() { ["Repo"] = repo });

        var client = await _clientFactory.CreateClientAsync();
        var runs = await client.Actions.Workflows.Runs.List(DefaultOwner, repo,
            new WorkflowRunsRequest(), new ApiOptions { PageSize = maxResults, PageCount = 1 });

        var results = runs.WorkflowRuns.Take(maxResults).Select(r => new
        {
            id = r.Id, name = r.Name, status = r.Status.StringValue,
            conclusion = r.Conclusion?.StringValue ?? "in_progress",
            branch = r.HeadBranch, createdAt = r.CreatedAt.ToString("yyyy-MM-dd HH:mm"), url = r.HtmlUrl
        });

        return JsonSerializer.Serialize(new { count = results.Count(), runs = results }, JsonOpts);
    }

    [KernelFunction("assign_issue")]
    [Description("Assigns a GitHub issue to one or more users. Use 'copilot' to assign to the Copilot coding agent.")]
    public async Task<string> AssignIssueAsync(
        [Description("Repository name (e.g. 'idp-core')")] string repo,
        [Description("Issue number")] int issueNumber,
        [Description("Comma-separated assignees (use 'copilot' for the Copilot coding agent)")] string assignees)
    {
        Track("assign_issue", new() { ["Repo"] = repo, ["IssueNumber"] = issueNumber.ToString() });

        var client = await _clientFactory.CreateClientAsync();
        var assigneeList = assignees.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToArray();
        var updated = await client.Issue.Assignee.AddAssignees(DefaultOwner, repo, issueNumber,
            new AssigneesUpdate(assigneeList));

        return JsonSerializer.Serialize(new
        {
            number = updated.Number, title = updated.Title,
            assignees = updated.Assignees.Select(a => a.Login).ToList(), url = updated.HtmlUrl
        }, JsonOpts);
    }

    // --- New tools delegating to shared IGitHubQueryService ---

    [KernelFunction("get_pull_requests")]
    [Description("Lists pull requests for a repository owned by frasermolyneux. Filter by state (open/closed/all).")]
    public async Task<string> GetPullRequestsAsync(
        [Description("Repository name (e.g. 'idp-core')")] string repo,
        [Description("Optional: filter by state — open, closed, all. Default: open")] string? state = "open",
        [Description("Optional: maximum results. Default 25")] int maxResults = 25)
    {
        Track("get_pull_requests", new() { ["Repo"] = repo });
        var prs = await _ghService.GetPullRequestsAsync(repo, state, maxResults);
        return JsonSerializer.Serialize(new { count = prs.Count, pullRequests = prs }, JsonOpts);
    }

    [KernelFunction("get_workflow_failures")]
    [Description("Gets recent failed GitHub Actions workflow runs for a repository owned by frasermolyneux.")]
    public async Task<string> GetWorkflowFailuresAsync(
        [Description("Repository name (e.g. 'idp-core')")] string repo,
        [Description("Optional: maximum results. Default 10")] int maxResults = 10)
    {
        Track("get_workflow_failures", new() { ["Repo"] = repo });
        var failures = await _ghService.GetWorkflowFailuresAsync(repo, maxResults);
        return JsonSerializer.Serialize(new { count = failures.Count, failures }, JsonOpts);
    }

    [KernelFunction("search_code")]
    [Description("Searches code across frasermolyneux repositories. Optionally scope to a single repo.")]
    public async Task<string> SearchCodeAsync(
        [Description("Search query (e.g. 'DefaultAzureCredential', 'TODO', 'connectionString')")] string query,
        [Description("Optional: repository name to scope search to")] string? repo = null)
    {
        Track("search_code", new() { ["Query"] = query });
        var results = await _ghService.SearchCodeAsync(query, repo);
        return JsonSerializer.Serialize(new { count = results.Count, results }, JsonOpts);
    }

    [KernelFunction("get_repo_stats")]
    [Description("Gets statistics for repositories — open issues, PRs, stars, forks, size. Optionally filter by repo name.")]
    public async Task<string> GetRepoStatsAsync(
        [Description("Optional: comma-separated repository names. Default: all")] string? repos = null)
    {
        Track("get_repo_stats");
        var repoList = repos?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var stats = await _ghService.GetRepoStatsAsync(repoList);
        return JsonSerializer.Serialize(new { count = stats.Count, stats }, JsonOpts);
    }

    [KernelFunction("close_or_reopen_issue")]
    [Description("Closes or reopens a GitHub issue in a repository owned by frasermolyneux. Optionally adds a comment.")]
    public async Task<string> CloseOrReopenIssueAsync(
        [Description("Repository name (e.g. 'idp-core')")] string repo,
        [Description("Issue number")] int issueNumber,
        [Description("Action: 'close' or 'reopen'")] string action,
        [Description("Optional: comment to add before changing state")] string? comment = null)
    {
        Track("close_or_reopen_issue", new() { ["Repo"] = repo, ["Action"] = action });
        var result = await _ghService.CloseOrReopenIssueAsync(repo, issueNumber, action, comment);
        return JsonSerializer.Serialize(result, JsonOpts);
    }

    [KernelFunction("add_label")]
    [Description("Adds labels to a GitHub issue or pull request in a repository owned by frasermolyneux.")]
    public async Task<string> AddLabelAsync(
        [Description("Repository name (e.g. 'idp-core')")] string repo,
        [Description("Issue or PR number")] int issueNumber,
        [Description("Comma-separated labels to add")] string labels)
    {
        Track("add_label", new() { ["Repo"] = repo });
        var labelList = labels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var result = await _ghService.AddLabelAsync(repo, issueNumber, labelList);
        return JsonSerializer.Serialize(result, JsonOpts);
    }
}
