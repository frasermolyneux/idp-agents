using MX.IDP.Agents.Models;

namespace MX.IDP.Agents.Services;

public interface IIdpChatService
{
    Task<ChatResponse> GetCompletionAsync(ChatRequest request);
}
