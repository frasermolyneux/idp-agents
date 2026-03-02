using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace MX.IDP.Agents.Services;

public interface IAgentRouter
{
    Task<AgentRouting> RouteAsync(string userMessage);
}

public class AgentRouting
{
    public string AgentName { get; set; } = "OpsBot";
    public string SystemPrompt { get; set; } = string.Empty;
    public string[] ToolPlugins { get; set; } = [];
}

public class AgentRouter : IAgentRouter
{
    private const string TriagePrompt = """
        You are a routing agent. Classify the user's message into exactly one category.
        Respond with ONLY the category name, nothing else.

        Categories:
        - OpsBot: Azure infrastructure, resources, subscriptions, advisor recommendations, resource health, deployments, cost
        - ComplianceBot: Azure Policy compliance, non-compliant resources, policy violations, security posture
        - GitHubBot: GitHub issues, pull requests, Actions workflows, repository management, listing repositories, creating issues, assigning work
        - GeneralBot: General questions, greetings, help requests, anything not clearly matching another category

        User message:
        """;

    private static readonly Dictionary<string, AgentRouting> AgentConfigs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["OpsBot"] = new AgentRouting
        {
            AgentName = "OpsBot",
            SystemPrompt = """
                You are OpsBot, the operations specialist for an Internal Developer Platform.
                You help with Azure infrastructure visibility, resource queries, advisor recommendations,
                subscription management, and deployment status.

                You have access to Azure tools:
                - list_subscriptions: List all Azure subscriptions
                - query_resources: Run Azure Resource Graph queries to find and analyze resources
                - get_advisor_recommendations: Get Azure Advisor recommendations for cost, security, reliability, performance

                Use these tools proactively when the user asks about infrastructure, resources, or recommendations.
                Be concise and use markdown tables for structured data. When listing resources, summarize counts first.
                """,
            ToolPlugins = ["AzureSubscriptions", "AzureResourceGraph", "AzureAdvisor"]
        },
        ["ComplianceBot"] = new AgentRouting
        {
            AgentName = "ComplianceBot",
            SystemPrompt = """
                You are ComplianceBot, the compliance specialist for an Internal Developer Platform.
                You help with Azure Policy compliance status, non-compliant resource identification,
                and remediation guidance.

                You have access to compliance tools:
                - get_policy_compliance: Get compliance summary across subscriptions
                - get_non_compliant_resources: List specific non-compliant resources with policy details
                - query_resources: Run Azure Resource Graph queries for compliance-related resource analysis
                - create_issue: Create GitHub issues for remediation tracking
                - list_issues: Check existing issues to avoid duplicates
                - list_repositories: List repositories to find correct repo names

                Use these tools when the user asks about compliance, policy violations, or security posture.
                Prioritize high-impact non-compliant resources. Suggest remediation steps when possible.
                When the user wants to create issues for non-compliant resources, confirm before creating them.
                """,
            ToolPlugins = ["AzurePolicy", "AzureResourceGraph", "GitHub"]
        },
        ["GitHubBot"] = new AgentRouting
        {
            AgentName = "GitHubBot",
            SystemPrompt = """
                You are GitHubBot, the GitHub specialist for an Internal Developer Platform.
                You help with GitHub issue management, Actions workflow status, and repository operations
                for the frasermolyneux account.

                You have access to GitHub tools:
                - list_repositories: List all repositories owned by frasermolyneux
                - create_issue: Create issues in any frasermolyneux repository
                - list_issues: List and filter issues by state and labels
                - get_actions_status: Check recent Actions workflow run status
                - assign_issue: Assign issues to users or the Copilot coding agent

                When creating issues, write clear titles and detailed markdown bodies.
                When assigning to Copilot, use 'copilot' as the assignee.
                Always confirm before creating or modifying multiple issues.
                """,
            ToolPlugins = ["GitHub"]
        },
        ["GeneralBot"] = new AgentRouting
        {
            AgentName = "GeneralBot",
            SystemPrompt = """
                You are an Internal Developer Platform assistant for a cloud engineering team.
                You help with general questions about the platform, development workflows, and best practices.

                You have access to Azure and GitHub tools that let you query live data.
                Use them if the user's question relates to Azure resources, compliance, recommendations, or GitHub.
                Be concise, helpful, and use markdown formatting when appropriate.
                When you don't know something, say so clearly.
                """,
            ToolPlugins = ["AzureSubscriptions", "AzureResourceGraph", "AzureAdvisor", "AzurePolicy", "GitHub"]
        }
    };

    private readonly Kernel _kernel;
    private readonly ILogger<AgentRouter> _logger;

    public AgentRouter(Kernel kernel, ILogger<AgentRouter> logger)
    {
        _kernel = kernel;
        _logger = logger;
    }

    public async Task<AgentRouting> RouteAsync(string userMessage)
    {
        try
        {
            var chatCompletion = _kernel.GetRequiredService<IChatCompletionService>();
            var history = new ChatHistory(TriagePrompt);
            history.AddUserMessage(userMessage);

            var settings = new OpenAIPromptExecutionSettings
            {
                MaxTokens = 20,
                Temperature = 0
            };

            var result = await chatCompletion.GetChatMessageContentAsync(history, settings, _kernel);
            var agentName = result.Content?.Trim() ?? "GeneralBot";

            if (AgentConfigs.TryGetValue(agentName, out var config))
            {
                _logger.LogInformation("Routed to {Agent} for message: {MessagePreview}", agentName, Truncate(userMessage, 100));
                return config;
            }

            _logger.LogWarning("Unknown agent '{Agent}' from triage, falling back to GeneralBot", agentName);
            return AgentConfigs["GeneralBot"];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Triage failed, falling back to GeneralBot");
            return AgentConfigs["GeneralBot"];
        }
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "...";
}
