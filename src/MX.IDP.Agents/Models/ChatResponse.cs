namespace MX.IDP.Agents.Models;

public class ChatResponse
{
    public string Message { get; set; } = string.Empty;
    public string? ConversationId { get; set; }
    public string Agent { get; set; } = "TriageAgent";
    public TokenUsage? Usage { get; set; }
}

public class TokenUsage
{
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
}
