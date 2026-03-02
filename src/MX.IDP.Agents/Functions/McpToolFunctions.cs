using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Microsoft.Extensions.Logging;

using MX.IDP.Agents.Tools;

namespace MX.IDP.Agents.Functions;

public class McpToolFunctions
{
    private readonly SubscriptionTool _subscriptionTool;
    private readonly ResourceGraphTool _resourceGraphTool;
    private readonly AdvisorTool _advisorTool;
    private readonly PolicyTool _policyTool;
    private readonly GitHubTool _gitHubTool;
    private readonly ILogger<McpToolFunctions> _logger;

    public McpToolFunctions(
        SubscriptionTool subscriptionTool,
        ResourceGraphTool resourceGraphTool,
        AdvisorTool advisorTool,
        PolicyTool policyTool,
        GitHubTool gitHubTool,
        ILogger<McpToolFunctions> logger)
    {
        _subscriptionTool = subscriptionTool;
        _resourceGraphTool = resourceGraphTool;
        _advisorTool = advisorTool;
        _policyTool = policyTool;
        _gitHubTool = gitHubTool;
        _logger = logger;
    }

    [Function("mcp_list_subscriptions")]
    public async Task<string> ListSubscriptions(
        [McpToolTrigger("list_subscriptions", "List all Azure subscriptions accessible to the platform")] ToolInvocationContext context)
    {
        _logger.LogInformation("MCP tool invoked: list_subscriptions");
        return await _subscriptionTool.ListSubscriptionsAsync();
    }

    [Function("mcp_query_resources")]
    public async Task<string> QueryResources(
        [McpToolTrigger("query_resources", "Run an Azure Resource Graph query to find and analyze resources across all subscriptions")] ToolInvocationContext context,
        [McpToolProperty("query", "KQL query to execute against Azure Resource Graph", isRequired: true)] string query)
    {
        _logger.LogInformation("MCP tool invoked: query_resources");
        return await _resourceGraphTool.QueryResourcesAsync(query);
    }

    [Function("mcp_get_advisor_recommendations")]
    public async Task<string> GetAdvisorRecommendations(
        [McpToolTrigger("get_advisor_recommendations", "Get Azure Advisor recommendations across all subscriptions for cost, security, reliability, and performance")] ToolInvocationContext context,
        [McpToolProperty("category", "Filter by category: Cost, Security, Reliability, OperationalExcellence, Performance")] string? category,
        [McpToolProperty("impact", "Filter by impact: High, Medium, Low")] string? impact,
        [McpToolProperty("maxResults", "Maximum number of results to return (default 25)")] string? maxResults)
    {
        _logger.LogInformation("MCP tool invoked: get_advisor_recommendations");
        var max = int.TryParse(maxResults, out var m) ? m : 25;
        return await _advisorTool.GetRecommendationsAsync(category, impact, max);
    }

    [Function("mcp_get_policy_compliance")]
    public async Task<string> GetPolicyCompliance(
        [McpToolTrigger("get_policy_compliance", "Get Azure Policy compliance summary showing compliant vs non-compliant resource counts per subscription")] ToolInvocationContext context,
        [McpToolProperty("subscriptionId", "Optional subscription ID to scope the query")] string? subscriptionId)
    {
        _logger.LogInformation("MCP tool invoked: get_policy_compliance");
        return await _policyTool.GetPolicyComplianceAsync(subscriptionId);
    }

    [Function("mcp_get_non_compliant_resources")]
    public async Task<string> GetNonCompliantResources(
        [McpToolTrigger("get_non_compliant_resources", "List non-compliant Azure resources with policy details")] ToolInvocationContext context,
        [McpToolProperty("subscriptionId", "Optional subscription ID to scope the query")] string? subscriptionId,
        [McpToolProperty("maxResults", "Maximum number of results to return (default 25)")] string? maxResults)
    {
        _logger.LogInformation("MCP tool invoked: get_non_compliant_resources");
        var max = int.TryParse(maxResults, out var m) ? m : 25;
        return await _policyTool.GetNonCompliantResourcesAsync(subscriptionId, max);
    }

    [Function("mcp_create_issue")]
    public async Task<string> CreateIssue(
        [McpToolTrigger("create_issue", "Create a GitHub issue in a frasermolyneux repository")] ToolInvocationContext context,
        [McpToolProperty("repo", "Repository name", isRequired: true)] string repo,
        [McpToolProperty("title", "Issue title", isRequired: true)] string title,
        [McpToolProperty("body", "Issue body in markdown", isRequired: true)] string body,
        [McpToolProperty("assignees", "Comma-separated assignees (use 'copilot' for Copilot coding agent)")] string? assignees,
        [McpToolProperty("labels", "Comma-separated labels")] string? labels)
    {
        _logger.LogInformation("MCP tool invoked: create_issue");
        return await _gitHubTool.CreateIssueAsync(repo, title, body, assignees, labels);
    }

    [Function("mcp_list_issues")]
    public async Task<string> ListIssues(
        [McpToolTrigger("list_issues", "List issues in a frasermolyneux GitHub repository")] ToolInvocationContext context,
        [McpToolProperty("repo", "Repository name", isRequired: true)] string repo,
        [McpToolProperty("state", "Filter by state: open, closed, all")] string? state,
        [McpToolProperty("labels", "Comma-separated labels to filter by")] string? labels,
        [McpToolProperty("maxResults", "Maximum results (default 25)")] string? maxResults)
    {
        _logger.LogInformation("MCP tool invoked: list_issues");
        var max = int.TryParse(maxResults, out var m) ? m : 25;
        return await _gitHubTool.ListIssuesAsync(repo, state, labels, max);
    }

    [Function("mcp_get_actions_status")]
    public async Task<string> GetActionsStatus(
        [McpToolTrigger("get_actions_status", "Get recent GitHub Actions workflow run status")] ToolInvocationContext context,
        [McpToolProperty("repo", "Repository name", isRequired: true)] string repo,
        [McpToolProperty("maxResults", "Maximum results (default 10)")] string? maxResults)
    {
        _logger.LogInformation("MCP tool invoked: get_actions_status");
        var max = int.TryParse(maxResults, out var m) ? m : 10;
        return await _gitHubTool.GetActionsStatusAsync(repo, max);
    }

    [Function("mcp_assign_issue")]
    public async Task<string> AssignIssue(
        [McpToolTrigger("assign_issue", "Assign a GitHub issue to users or the Copilot coding agent")] ToolInvocationContext context,
        [McpToolProperty("repo", "Repository name", isRequired: true)] string repo,
        [McpToolProperty("issueNumber", "Issue number", isRequired: true)] string issueNumber,
        [McpToolProperty("assignees", "Comma-separated assignees (use 'copilot' for Copilot coding agent)", isRequired: true)] string assignees)
    {
        _logger.LogInformation("MCP tool invoked: assign_issue");
        return await _gitHubTool.AssignIssueAsync(repo, int.Parse(issueNumber), assignees);
    }
}
