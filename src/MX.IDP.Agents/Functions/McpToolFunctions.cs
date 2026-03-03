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
    private readonly KnowledgeTool _knowledgeTool;
    private readonly CampaignTool _campaignTool;
    private readonly ILogger<McpToolFunctions> _logger;

    public McpToolFunctions(
        SubscriptionTool subscriptionTool,
        ResourceGraphTool resourceGraphTool,
        AdvisorTool advisorTool,
        PolicyTool policyTool,
        GitHubTool gitHubTool,
        KnowledgeTool knowledgeTool,
        CampaignTool campaignTool,
        ILogger<McpToolFunctions> logger)
    {
        _subscriptionTool = subscriptionTool;
        _resourceGraphTool = resourceGraphTool;
        _advisorTool = advisorTool;
        _policyTool = policyTool;
        _gitHubTool = gitHubTool;
        _knowledgeTool = knowledgeTool;
        _campaignTool = campaignTool;
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

    [Function("mcp_search_knowledge")]
    public async Task<string> SearchKnowledge(
        [McpToolTrigger("search_knowledge_base", "Search the knowledge base for documentation, runbooks, ADRs, and Terraform docs using hybrid search")] ToolInvocationContext context,
        [McpToolProperty("query", "Search query", isRequired: true)] string query,
        [McpToolProperty("sourceType", "Filter by source type: github_repo or blob_storage")] string? sourceType,
        [McpToolProperty("sourceName", "Filter by source name (repo name or blob path)")] string? sourceName,
        [McpToolProperty("maxResults", "Maximum results (default 5)")] string? maxResults)
    {
        _logger.LogInformation("MCP tool invoked: search_knowledge_base");
        var max = int.TryParse(maxResults, out var m) ? m : 5;
        return await _knowledgeTool.SearchKnowledgeBaseAsync(query, sourceType, sourceName, max);
    }

    [Function("mcp_list_knowledge_sources")]
    public async Task<string> ListKnowledgeSources(
        [McpToolTrigger("list_knowledge_sources", "List all indexed knowledge sources")] ToolInvocationContext context)
    {
        _logger.LogInformation("MCP tool invoked: list_knowledge_sources");
        return await _knowledgeTool.ListKnowledgeSourcesAsync();
    }

    [Function("mcp_trigger_reindex")]
    public async Task<string> TriggerKnowledgeReindex(
        [McpToolTrigger("trigger_reindex", "Trigger a reindex of knowledge sources")] ToolInvocationContext context,
        [McpToolProperty("sourceType", "Source type to reindex: github_repo, blob_storage, or all", isRequired: true)] string sourceType,
        [McpToolProperty("sourceName", "Specific source name or 'all'")] string? sourceName)
    {
        _logger.LogInformation("MCP tool invoked: trigger_reindex");
        return await _knowledgeTool.TriggerReindexAsync(sourceType, sourceName);
    }

    // Campaign tools

    [Function("mcp_create_campaign")]
    public async Task<string> CreateCampaign(
        [McpToolTrigger("create_campaign", "Create a new campaign to systematically scan and remediate issues across repositories and infrastructure")] ToolInvocationContext context,
        [McpToolProperty("name", "Campaign name", isRequired: true)] string name,
        [McpToolProperty("sourceType", "Source type: advisor, policy, dev_standards, repo_config, dependabot, codeql, or kql", isRequired: true)] string sourceType,
        [McpToolProperty("description", "Campaign description")] string? description,
        [McpToolProperty("category", "For advisor: filter by category (Cost, Security, Reliability, Performance)")] string? category,
        [McpToolProperty("impact", "For advisor/dependabot/codeql: filter by impact (High, Medium, Low)")] string? impact,
        [McpToolProperty("repos", "Comma-separated repo names to scope the campaign to")] string? repos,
        [McpToolProperty("assignTo", "Assignee for created issues (use 'copilot' for Copilot coding agent)")] string? assignTo,
        [McpToolProperty("kqlQuery", "For kql source type: the ARG KQL query to execute")] string? kqlQuery)
    {
        _logger.LogInformation("MCP tool invoked: create_campaign");
        return await _campaignTool.CreateCampaignAsync(name, sourceType, description, category, impact, repos, assignTo, kqlQuery);
    }

    [Function("mcp_list_campaigns")]
    public async Task<string> ListCampaigns(
        [McpToolTrigger("list_campaigns", "List all campaigns, optionally filtered by status")] ToolInvocationContext context,
        [McpToolProperty("status", "Filter by status: created, running, paused, completed, failed")] string? status)
    {
        _logger.LogInformation("MCP tool invoked: list_campaigns");
        return await _campaignTool.ListCampaignsAsync(status);
    }

    [Function("mcp_run_campaign")]
    public async Task<string> RunCampaign(
        [McpToolTrigger("run_campaign", "Run a campaign by ID — scans data source, deduplicates findings, creates GitHub issues, and tracks progress")] ToolInvocationContext context,
        [McpToolProperty("campaignId", "Campaign ID to run", isRequired: true)] string campaignId)
    {
        _logger.LogInformation("MCP tool invoked: run_campaign");
        return await _campaignTool.RunCampaignAsync(campaignId);
    }

    [Function("mcp_get_campaign_findings")]
    public async Task<string> GetCampaignFindings(
        [McpToolTrigger("get_campaign_findings", "Get findings for a campaign, optionally filtered by status")] ToolInvocationContext context,
        [McpToolProperty("campaignId", "Campaign ID", isRequired: true)] string campaignId,
        [McpToolProperty("status", "Filter by status: new, pending_approval, issue_created, resolved, skipped, duplicate, dismissed")] string? status)
    {
        _logger.LogInformation("MCP tool invoked: get_campaign_findings");
        return await _campaignTool.GetCampaignFindingsAsync(campaignId, status);
    }

    [Function("mcp_list_campaign_templates")]
    public Task<string> ListCampaignTemplates(
        [McpToolTrigger("list_campaign_templates", "List available pre-built campaign templates for common scenarios like security hardening and cost optimisation")] ToolInvocationContext context)
    {
        _logger.LogInformation("MCP tool invoked: list_campaign_templates");
        return _campaignTool.ListCampaignTemplatesAsync();
    }

    [Function("mcp_create_campaign_from_template")]
    public async Task<string> CreateCampaignFromTemplate(
        [McpToolTrigger("create_campaign_from_template", "Create a campaign from a pre-built template by template ID")] ToolInvocationContext context,
        [McpToolProperty("templateId", "Template ID (e.g., security-hardening, dependency-updates, cost-optimisation)", isRequired: true)] string templateId,
        [McpToolProperty("name", "Optional custom name for the campaign")] string? name,
        [McpToolProperty("assignTo", "Assignee for created issues (use 'copilot' for Copilot coding agent)")] string? assignTo)
    {
        _logger.LogInformation("MCP tool invoked: create_campaign_from_template");
        return await _campaignTool.CreateCampaignFromTemplateAsync(templateId, name, assignTo);
    }
}
