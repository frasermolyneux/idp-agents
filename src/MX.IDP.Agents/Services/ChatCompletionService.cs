using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

using MX.IDP.Agents.Models;

namespace MX.IDP.Agents.Services;

public class ChatCompletionService : IIdpChatService
{
    private const string SystemPrompt = """
        You are an Internal Developer Platform assistant for a cloud engineering team. You help with:
        - Azure infrastructure questions and resource visibility
        - Compliance and policy status
        - GitHub repository and issue management
        - Cost management insights
        - General platform knowledge

        Be concise, helpful, and use markdown formatting when appropriate.
        When you don't know something, say so clearly.
        """;

    private readonly Kernel _kernel;

    public ChatCompletionService(Kernel kernel)
    {
        _kernel = kernel;
    }

    public async Task<ChatResponse> GetCompletionAsync(ChatRequest request)
    {
        var chatCompletion = _kernel.GetRequiredService<IChatCompletionService>();

        var chatHistory = new ChatHistory(SystemPrompt);

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

        var result = await chatCompletion.GetChatMessageContentAsync(chatHistory);

        return new ChatResponse
        {
            Message = result.Content ?? string.Empty,
            ConversationId = request.ConversationId ?? Guid.NewGuid().ToString()
        };
    }
}
