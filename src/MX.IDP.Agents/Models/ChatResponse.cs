namespace MX.IDP.Agents.Models;

public class ChatResponse
{
    public string Message { get; set; } = string.Empty;
    public string? ConversationId { get; set; }
    public string Agent { get; set; } = "TriageAgent";
}
