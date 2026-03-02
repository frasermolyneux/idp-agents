namespace MX.IDP.Agents.Models;

public class ChatResponse
{
    public string Message { get; set; } = string.Empty;
    public string? ConversationId { get; set; }
    public string Agent { get; set; } = "GeneralBot";
    public TokenUsage? Usage { get; set; }
    public List<TokenLogprobInfo>? Logprobs { get; set; }
    public List<FunctionCallInfo>? FunctionCalls { get; set; }
}

public class TokenUsage
{
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
}

public class TokenLogprobInfo
{
    public string Token { get; set; } = string.Empty;
    public double Logprob { get; set; }
    public List<TokenAlternative> TopAlternatives { get; set; } = new();
}

public class TokenAlternative
{
    public string Token { get; set; } = string.Empty;
    public double Logprob { get; set; }
}

public class FunctionCallInfo
{
    public string ToolName { get; set; } = string.Empty;
    public Dictionary<string, string>? Arguments { get; set; }
    public string? ResultPreview { get; set; }
    public int Order { get; set; }
}
