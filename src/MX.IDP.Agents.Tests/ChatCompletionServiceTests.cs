using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

using Moq;

using MX.IDP.Agents.Models;
using MX.IDP.Agents.Services;
using MX.IDP.Agents.Tools;

using Xunit;

namespace MX.IDP.Agents.Tests;

public class ChatCompletionServiceTests
{
    private readonly Mock<IChatCompletionService> _mockChatCompletion;
    private readonly Mock<IAgentRouter> _mockRouter;
    private readonly ChatCompletionService _sut;

    public ChatCompletionServiceTests()
    {
        _mockChatCompletion = new Mock<IChatCompletionService>();

        var builder = Kernel.CreateBuilder();
        builder.Services.AddSingleton(_mockChatCompletion.Object);
        var kernel = builder.Build();

        _mockRouter = new Mock<IAgentRouter>();
        _mockRouter.Setup(r => r.RouteAsync(It.IsAny<string>()))
            .ReturnsAsync(new AgentRouting
            {
                AgentName = "OpsBot",
                SystemPrompt = "You are OpsBot.",
                ToolPlugins = []
            });

        var mockArmClient = new Mock<Azure.ResourceManager.ArmClient>(MockBehavior.Loose);

        _sut = new ChatCompletionService(
            kernel,
            _mockRouter.Object,
            Mock.Of<ILogger<ChatCompletionService>>(),
            new SubscriptionTool(mockArmClient.Object),
            new ResourceGraphTool(mockArmClient.Object),
            new AdvisorTool(mockArmClient.Object),
            new PolicyTool(mockArmClient.Object),
            new GitHubTool(Mock.Of<IGitHubClientFactory>()));
    }

    private void SetupChatCompletion(string responseContent)
    {
        _mockChatCompletion
            .Setup(x => x.GetChatMessageContentsAsync(
                It.IsAny<ChatHistory>(),
                It.IsAny<PromptExecutionSettings>(),
                It.IsAny<Kernel>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ChatMessageContent(AuthorRole.Assistant, responseContent)]);
    }

    private void SetupChatCompletionWithCapture(string responseContent, Action<ChatHistory> onCapture)
    {
        _mockChatCompletion
            .Setup(x => x.GetChatMessageContentsAsync(
                It.IsAny<ChatHistory>(),
                It.IsAny<PromptExecutionSettings>(),
                It.IsAny<Kernel>(),
                It.IsAny<CancellationToken>()))
            .Callback<ChatHistory, PromptExecutionSettings?, Kernel?, CancellationToken>((h, _, _, _) => onCapture(h))
            .ReturnsAsync([new ChatMessageContent(AuthorRole.Assistant, responseContent)]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetCompletionAsync_ReturnsResponse_WithMessage()
    {
        var expected = "Hello from the assistant";
        SetupChatCompletion(expected);

        var request = new ChatRequest { Message = "Hello" };

        var result = await _sut.GetCompletionAsync(request);

        Assert.Equal(expected, result.Message);
        Assert.NotNull(result.ConversationId);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetCompletionAsync_PreservesConversationId_WhenProvided()
    {
        var conversationId = "conv-123";
        SetupChatCompletion("response");

        var request = new ChatRequest { Message = "Hello", ConversationId = conversationId };

        var result = await _sut.GetCompletionAsync(request);

        Assert.Equal(conversationId, result.ConversationId);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetCompletionAsync_GeneratesConversationId_WhenNotProvided()
    {
        SetupChatCompletion("response");

        var request = new ChatRequest { Message = "Hello" };

        var result = await _sut.GetCompletionAsync(request);

        Assert.NotNull(result.ConversationId);
        Assert.True(Guid.TryParse(result.ConversationId, out _));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetCompletionAsync_IncludesHistory_InChatMessages()
    {
        ChatHistory? capturedHistory = null;
        SetupChatCompletionWithCapture("response", h => capturedHistory = h);

        var request = new ChatRequest
        {
            Message = "Follow up",
            History =
            [
                new Models.ChatMessage { Role = "user", Content = "First message" },
                new Models.ChatMessage { Role = "assistant", Content = "First response" }
            ]
        };

        await _sut.GetCompletionAsync(request);

        Assert.NotNull(capturedHistory);
        // System prompt + 2 history + 1 current = 4 messages
        Assert.Equal(4, capturedHistory!.Count);
        Assert.Equal(AuthorRole.System, capturedHistory[0].Role);
        Assert.Equal(AuthorRole.User, capturedHistory[1].Role);
        Assert.Equal(AuthorRole.Assistant, capturedHistory[2].Role);
        Assert.Equal(AuthorRole.User, capturedHistory[3].Role);
        Assert.Equal("Follow up", capturedHistory[3].Content);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetCompletionAsync_HandlesNullContent_ReturnsEmptyString()
    {
        _mockChatCompletion
            .Setup(x => x.GetChatMessageContentsAsync(
                It.IsAny<ChatHistory>(),
                It.IsAny<PromptExecutionSettings>(),
                It.IsAny<Kernel>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ChatMessageContent(AuthorRole.Assistant, (string?)null)]);

        var request = new ChatRequest { Message = "Hello" };

        var result = await _sut.GetCompletionAsync(request);

        Assert.Equal(string.Empty, result.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetCompletionAsync_SetsAgentFromRouter()
    {
        SetupChatCompletion("response");

        var request = new ChatRequest { Message = "Hello" };

        var result = await _sut.GetCompletionAsync(request);

        Assert.Equal("OpsBot", result.Agent);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetCompletionAsync_WithNullHistory_DoesNotThrow()
    {
        SetupChatCompletion("response");

        var request = new ChatRequest { Message = "Hello", History = null };

        var result = await _sut.GetCompletionAsync(request);

        Assert.NotNull(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetCompletionAsync_CallsAgentRouter_WithUserMessage()
    {
        SetupChatCompletion("response");

        var request = new ChatRequest { Message = "Show me advisor recommendations" };

        await _sut.GetCompletionAsync(request);

        _mockRouter.Verify(r => r.RouteAsync("Show me advisor recommendations"), Times.Once);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetCompletionAsync_UsesRouterSystemPrompt()
    {
        ChatHistory? capturedHistory = null;
        SetupChatCompletionWithCapture("response", h => capturedHistory = h);

        var request = new ChatRequest { Message = "Hello" };

        await _sut.GetCompletionAsync(request);

        Assert.NotNull(capturedHistory);
        Assert.Equal("You are OpsBot.", capturedHistory![0].Content);
    }
}
