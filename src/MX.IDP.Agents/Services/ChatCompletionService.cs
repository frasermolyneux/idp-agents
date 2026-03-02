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
        GitHubTool gitHubTool,
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
            _kernel.Plugins.AddFromObject(gitHubTool, "GitHub");
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
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
            Logprobs = true,
            TopLogprobs = 5
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

        var logprobs = ExtractLogprobs(result);
        var functionCalls = ExtractFunctionCallTrace(chatHistory);

        return new ChatResponse
        {
            Message = result.Content ?? string.Empty,
            ConversationId = request.ConversationId ?? Guid.NewGuid().ToString(),
            Agent = routing.AgentName,
            Usage = tokenUsage,
            Logprobs = logprobs,
            FunctionCalls = functionCalls
        };
    }

    private static List<TokenLogprobInfo>? ExtractLogprobs(ChatMessageContent result)
    {
        if (result.Metadata is null) return null;

        // SK surfaces logprobs in Metadata["ContentTokenLogProbabilityResults"] or similar
        if (result.Metadata.TryGetValue("ContentTokenLogProbabilityResults", out var logprobsObj) && logprobsObj is not null)
        {
            return ExtractLogprobsFromObject(logprobsObj);
        }

        // Also try "Logprobs" key
        if (result.Metadata.TryGetValue("Logprobs", out var logprobs2) && logprobs2 is not null)
        {
            return ExtractLogprobsFromObject(logprobs2);
        }

        return null;
    }

    private static List<TokenLogprobInfo>? ExtractLogprobsFromObject(object logprobsObj)
    {
        var result = new List<TokenLogprobInfo>();

        // Use reflection to handle different SDK versions
        if (logprobsObj is System.Collections.IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                var type = item.GetType();
                var token = type.GetProperty("Token")?.GetValue(item)?.ToString();
                var logprob = type.GetProperty("LogProbability")?.GetValue(item);

                if (token is null) continue;

                var info = new TokenLogprobInfo
                {
                    Token = token,
                    Logprob = logprob is not null ? Convert.ToDouble(logprob) : 0
                };

                // Extract top logprobs (alternatives)
                var topLogprobs = type.GetProperty("TopLogProbabilities")?.GetValue(item);
                if (topLogprobs is System.Collections.IEnumerable topEnum)
                {
                    foreach (var alt in topEnum)
                    {
                        var altType = alt.GetType();
                        var altToken = altType.GetProperty("Token")?.GetValue(alt)?.ToString();
                        var altLogprob = altType.GetProperty("LogProbability")?.GetValue(alt);

                        if (altToken is not null)
                        {
                            info.TopAlternatives.Add(new TokenAlternative
                            {
                                Token = altToken,
                                Logprob = altLogprob is not null ? Convert.ToDouble(altLogprob) : 0
                            });
                        }
                    }
                }

                result.Add(info);
            }
        }

        return result.Count > 0 ? result : null;
    }

    private static List<FunctionCallInfo>? ExtractFunctionCallTrace(ChatHistory chatHistory)
    {
        var calls = new List<FunctionCallInfo>();
        var order = 0;

        foreach (var msg in chatHistory)
        {
            if (msg.Role == AuthorRole.Assistant && msg.Items is not null)
            {
                foreach (var item in msg.Items)
                {
                    if (item is Microsoft.SemanticKernel.FunctionCallContent fcc)
                    {
                        calls.Add(new FunctionCallInfo
                        {
                            ToolName = $"{fcc.PluginName}.{fcc.FunctionName}",
                            Arguments = fcc.Arguments?.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? ""),
                            Order = order++
                        });
                    }
                }
            }

            if (msg.Role == AuthorRole.Tool && msg.Items is not null)
            {
                foreach (var item in msg.Items)
                {
                    if (item is Microsoft.SemanticKernel.FunctionResultContent frc && calls.Count > 0)
                    {
                        var lastCall = calls.LastOrDefault(c => c.ResultPreview is null);
                        if (lastCall is not null)
                        {
                            var resultStr = frc.Result?.ToString() ?? "";
                            lastCall.ResultPreview = resultStr.Length > 200 ? resultStr[..200] + "..." : resultStr;
                        }
                    }
                }
            }
        }

        return calls.Count > 0 ? calls : null;
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
