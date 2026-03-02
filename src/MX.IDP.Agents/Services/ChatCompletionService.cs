using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

using MX.IDP.Agents.Models;
using MX.IDP.Agents.Tools;

namespace MX.IDP.Agents.Services;

public class ChatCompletionService : IIdpChatService
{
    private readonly Kernel _kernel;
    private readonly IAgentRouter _agentRouter;
    private readonly TelemetryClient? _telemetryClient;
    private readonly ILogger<ChatCompletionService> _logger;
    private bool _pluginsRegistered;

    public ChatCompletionService(
        Kernel kernel,
        IAgentRouter agentRouter,
        ILogger<ChatCompletionService> logger,
        SubscriptionTool subscriptionTool,
        ResourceGraphTool resourceGraphTool,
        AdvisorTool advisorTool,
        PolicyTool policyTool,
        TelemetryClient? telemetryClient = null)
    {
        _kernel = kernel;
        _agentRouter = agentRouter;
        _logger = logger;
        _telemetryClient = telemetryClient;

        // Register tools as SK plugins (idempotent — only once per kernel)
        if (!_pluginsRegistered)
        {
            _kernel.Plugins.AddFromObject(subscriptionTool, "AzureSubscriptions");
            _kernel.Plugins.AddFromObject(resourceGraphTool, "AzureResourceGraph");
            _kernel.Plugins.AddFromObject(advisorTool, "AzureAdvisor");
            _kernel.Plugins.AddFromObject(policyTool, "AzurePolicy");
            _pluginsRegistered = true;
        }
    }

    public async Task<ChatResponse> GetCompletionAsync(ChatRequest request)
    {
        // Route to the appropriate specialist agent
        var routing = await _agentRouter.RouteAsync(request.Message);

        _telemetryClient?.TrackEvent("AgentRouted", new Dictionary<string, string>
        {
            ["Agent"] = routing.AgentName,
            ["ConversationId"] = request.ConversationId ?? "unknown",
            ["MessagePreview"] = request.Message.Length > 100 ? request.Message[..100] : request.Message
        });

        var chatCompletion = _kernel.GetRequiredService<IChatCompletionService>();

        var chatHistory = new ChatHistory(routing.SystemPrompt);

        if (request.History is not null)
        {
            foreach (var message in request.History)
            {
                switch (message.Role.ToLowerInvariant())
                {
                    case "user":
                        chatHistory.AddUserMessage(message.Content);
                        break;
                    case "assistant":
                        chatHistory.AddAssistantMessage(message.Content);
                        break;
                }
            }
        }

        chatHistory.AddUserMessage(request.Message);

        // Enable auto function calling so the LLM can invoke Azure tools
        var executionSettings = new OpenAIPromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
        };

        ChatMessageContent result;
        try
        {
            result = await chatCompletion.GetChatMessageContentAsync(chatHistory, executionSettings, _kernel);
        }
        catch (HttpOperationException ex) when (ex.Message.Contains("content_filter") || ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            _logger.LogWarning(ex, "Content filter triggered for conversation {ConversationId}", request.ConversationId);
            _telemetryClient?.TrackEvent("ContentFilterTriggered", new Dictionary<string, string>
            {
                ["ConversationId"] = request.ConversationId ?? "unknown",
                ["FilterType"] = "input"
            });
            return new ChatResponse
            {
                Message = "I'm unable to process that request as it was flagged by the content safety filter. Please rephrase your question.",
                ConversationId = request.ConversationId ?? Guid.NewGuid().ToString(),
                Agent = routing.AgentName
            };
        }

        // Check if response was filtered (finish_reason = content_filter)
        var finishReason = result.Metadata?.TryGetValue("FinishReason", out var fr) == true ? fr?.ToString() : null;
        if (string.Equals(finishReason, "content_filter", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Output content filter triggered for conversation {ConversationId}", request.ConversationId);
            _telemetryClient?.TrackEvent("ContentFilterTriggered", new Dictionary<string, string>
            {
                ["ConversationId"] = request.ConversationId ?? "unknown",
                ["FilterType"] = "output"
            });
            return new ChatResponse
            {
                Message = "The response was flagged by the content safety filter. Please try a different question.",
                ConversationId = request.ConversationId ?? Guid.NewGuid().ToString(),
                Agent = routing.AgentName
            };
        }

        var tokenUsage = ExtractTokenUsage(result);
        TrackTokenUsage(tokenUsage, request.ConversationId);

        return new ChatResponse
        {
            Message = result.Content ?? string.Empty,
            ConversationId = request.ConversationId ?? Guid.NewGuid().ToString(),
            Agent = routing.AgentName,
            Usage = tokenUsage
        };
    }

    private static TokenUsage? ExtractTokenUsage(ChatMessageContent result)
    {
        if (result.Metadata is null) return null;

        // SK surfaces Azure OpenAI usage in Metadata["Usage"]
        if (result.Metadata.TryGetValue("Usage", out var usageObj) && usageObj is not null)
        {
            var type = usageObj.GetType();
            var prompt = type.GetProperty("InputTokenCount")?.GetValue(usageObj)
                      ?? type.GetProperty("PromptTokens")?.GetValue(usageObj);
            var completion = type.GetProperty("OutputTokenCount")?.GetValue(usageObj)
                          ?? type.GetProperty("CompletionTokens")?.GetValue(usageObj);
            var total = type.GetProperty("TotalTokenCount")?.GetValue(usageObj)
                     ?? type.GetProperty("TotalTokens")?.GetValue(usageObj);

            if (prompt is not null || completion is not null)
            {
                var promptTokens = Convert.ToInt32(prompt ?? 0);
                var completionTokens = Convert.ToInt32(completion ?? 0);
                return new TokenUsage
                {
                    PromptTokens = promptTokens,
                    CompletionTokens = completionTokens,
                    TotalTokens = total is not null ? Convert.ToInt32(total) : promptTokens + completionTokens
                };
            }
        }

        return null;
    }

    private void TrackTokenUsage(TokenUsage? usage, string? conversationId)
    {
        if (usage is null || _telemetryClient is null) return;

        _telemetryClient.TrackMetric(new MetricTelemetry("ChatPromptTokens", usage.PromptTokens)
        {
            Properties = { ["ConversationId"] = conversationId ?? "unknown" }
        });
        _telemetryClient.TrackMetric(new MetricTelemetry("ChatCompletionTokens", usage.CompletionTokens)
        {
            Properties = { ["ConversationId"] = conversationId ?? "unknown" }
        });
        _telemetryClient.TrackMetric(new MetricTelemetry("ChatTotalTokens", usage.TotalTokens)
        {
            Properties = { ["ConversationId"] = conversationId ?? "unknown" }
        });
    }
}
