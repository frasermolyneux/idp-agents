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
    private readonly TelemetryClient? _telemetryClient;
    private const string DefaultOwner = "frasermolyneux";

    public GitHubTool(IGitHubClientFactory clientFactory, TelemetryClient? telemetryClient = null)
    {
        _clientFactory = clientFactory;
        _telemetryClient = telemetryClient;
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
        _telemetryClient?.TrackEvent("ToolInvocation", new Dictionary<string, string>
        {
            ["Tool"] = "create_issue",
            ["Repo"] = repo,
            ["Assignees"] = assignees ?? "none"
        });

        var client = await _clientFactory.CreateClientAsync();

        var newIssue = new NewIssue(title)
        {
            Body = body
        };

        if (!string.IsNullOrEmpty(assignees))
        {
            foreach (var assignee in assignees.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                newIssue.Assignees.Add(assignee);
            }
        }

        if (!string.IsNullOrEmpty(labels))
        {
            foreach (var label in labels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                newIssue.Labels.Add(label);
            }
        }

        var issue = await client.Issue.Create(DefaultOwner, repo, newIssue);

        return JsonSerializer.Serialize(new
        {
            number = issue.Number,
            url = issue.HtmlUrl,
            title = issue.Title,
            state = issue.State.StringValue,
            assignees = issue.Assignees.Select(a => a.Login).ToList()
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    [KernelFunction("list_issues")]
    [Description("Lists issues in a GitHub repository owned by frasermolyneux. Supports filtering by state and labels.")]
    public async Task<string> ListIssuesAsync(
        [Description("Repository name (e.g. 'idp-core')")] string repo,
        [Description("Optional: filter by state — open, closed, all. Default: open")] string? state = "open",
        [Description("Optional: comma-separated labels to filter by")] string? labels = null,
        [Description("Optional: maximum number of results. Default 25")] int maxResults = 25)
    {
        _telemetryClient?.TrackEvent("ToolInvocation", new Dictionary<string, string>
        {
            ["Tool"] = "list_issues",
            ["Repo"] = repo,
            ["State"] = state ?? "open"
        });

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
        {
            foreach (var label in labels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                request.Labels.Add(label);
            }
        }

        var issues = await client.Issue.GetAllForRepository(DefaultOwner, repo, request,
            new ApiOptions { PageSize = maxResults, PageCount = 1 });

        var results = issues.Take(maxResults).Select(i => new
        {
            number = i.Number,
            title = i.Title,
            state = i.State.StringValue,
            assignees = i.Assignees.Select(a => a.Login).ToList(),
            labels = i.Labels.Select(l => l.Name).ToList(),
            url = i.HtmlUrl,
            createdAt = i.CreatedAt.ToString("yyyy-MM-dd")
        });

        return JsonSerializer.Serialize(new
        {
            count = results.Count(),
            issues = results
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    [KernelFunction("get_actions_status")]
    [Description("Gets the status of recent GitHub Actions workflow runs for a repository owned by frasermolyneux.")]
    public async Task<string> GetActionsStatusAsync(
        [Description("Repository name (e.g. 'idp-core')")] string repo,
        [Description("Optional: maximum number of recent runs to return. Default 10")] int maxResults = 10)
    {
        _telemetryClient?.TrackEvent("ToolInvocation", new Dictionary<string, string>
        {
            ["Tool"] = "get_actions_status",
            ["Repo"] = repo
        });

        var client = await _clientFactory.CreateClientAsync();

        var runs = await client.Actions.Workflows.Runs.List(DefaultOwner, repo,
            new WorkflowRunsRequest(),
            new ApiOptions { PageSize = maxResults, PageCount = 1 });

        var results = runs.WorkflowRuns.Take(maxResults).Select(r => new
        {
            id = r.Id,
            name = r.Name,
            status = r.Status.StringValue,
            conclusion = r.Conclusion?.StringValue ?? "in_progress",
            branch = r.HeadBranch,
            createdAt = r.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
            url = r.HtmlUrl
        });

        return JsonSerializer.Serialize(new
        {
            count = results.Count(),
            runs = results
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    [KernelFunction("assign_issue")]
    [Description("Assigns a GitHub issue to one or more users. Use 'copilot' to assign to the Copilot coding agent.")]
    public async Task<string> AssignIssueAsync(
        [Description("Repository name (e.g. 'idp-core')")] string repo,
        [Description("Issue number")] int issueNumber,
        [Description("Comma-separated assignees (use 'copilot' for the Copilot coding agent)")] string assignees)
    {
        _telemetryClient?.TrackEvent("ToolInvocation", new Dictionary<string, string>
        {
            ["Tool"] = "assign_issue",
            ["Repo"] = repo,
            ["IssueNumber"] = issueNumber.ToString(),
            ["Assignees"] = assignees
        });

        var client = await _clientFactory.CreateClientAsync();

        var assigneeList = assignees.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToArray();

        var updated = await client.Issue.Assignee.AddAssignees(DefaultOwner, repo, issueNumber,
            new AssigneesUpdate(assigneeList));

        return JsonSerializer.Serialize(new
        {
            number = updated.Number,
            title = updated.Title,
            assignees = updated.Assignees.Select(a => a.Login).ToList(),
            url = updated.HtmlUrl
        }, new JsonSerializerOptions { WriteIndented = true });
    }
}
