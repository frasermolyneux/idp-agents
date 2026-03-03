using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

using Moq;

using MX.IDP.Agents.Services;

using Xunit;

namespace MX.IDP.Agents.Tests;

public class AgentRouterTests
{
    private readonly Mock<IChatCompletionService> _mockChatCompletion;
    private readonly AgentRouter _sut;

    public AgentRouterTests()
    {
        _mockChatCompletion = new Mock<IChatCompletionService>();

        var builder = Kernel.CreateBuilder();
        builder.Services.AddSingleton(_mockChatCompletion.Object);
        var kernel = builder.Build();

        _sut = new AgentRouter(kernel, Mock.Of<ILogger<AgentRouter>>());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RouteAsync_OpsBot_ReturnsOpsBotRouting()
    {
        SetupTriageResponse("OpsBot");

        var result = await _sut.RouteAsync("Show me advisor recommendations");

        Assert.Equal("OpsBot", result.AgentName);
        Assert.Contains("OpsBot", result.SystemPrompt);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RouteAsync_ComplianceBot_ReturnsComplianceBotRouting()
    {
        SetupTriageResponse("ComplianceBot");

        var result = await _sut.RouteAsync("What are the non-compliant resources?");

        Assert.Equal("ComplianceBot", result.AgentName);
        Assert.Contains("ComplianceBot", result.SystemPrompt);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RouteAsync_GeneralBot_ReturnsGeneralBotRouting()
    {
        SetupTriageResponse("GeneralBot");

        var result = await _sut.RouteAsync("Hello, how are you?");

        Assert.Equal("GeneralBot", result.AgentName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RouteAsync_UnknownAgent_FallsBackToGeneralBot()
    {
        SetupTriageResponse("UnknownAgent");

        var result = await _sut.RouteAsync("Something weird");

        Assert.Equal("GeneralBot", result.AgentName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RouteAsync_TriageThrows_FallsBackToGeneralBot()
    {
        _mockChatCompletion
            .Setup(x => x.GetChatMessageContentsAsync(
                It.IsAny<ChatHistory>(),
                It.IsAny<PromptExecutionSettings>(),
                It.IsAny<Kernel>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Service unavailable"));

        var result = await _sut.RouteAsync("Some question");

        Assert.Equal("GeneralBot", result.AgentName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RouteAsync_NullContent_FallsBackToGeneralBot()
    {
        _mockChatCompletion
            .Setup(x => x.GetChatMessageContentsAsync(
                It.IsAny<ChatHistory>(),
                It.IsAny<PromptExecutionSettings>(),
                It.IsAny<Kernel>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ChatMessageContent(AuthorRole.Assistant, (string?)null)]);

        var result = await _sut.RouteAsync("Some question");

        Assert.Equal("GeneralBot", result.AgentName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RouteAsync_OpsBotRouting_IncludesExpectedToolPlugins()
    {
        SetupTriageResponse("OpsBot");

        var result = await _sut.RouteAsync("List subscriptions");

        Assert.Contains("AzureSubscriptions", result.ToolPlugins);
        Assert.Contains("AzureResourceGraph", result.ToolPlugins);
        Assert.Contains("AzureAdvisor", result.ToolPlugins);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RouteAsync_ComplianceBotRouting_IncludesExpectedToolPlugins()
    {
        SetupTriageResponse("ComplianceBot");

        var result = await _sut.RouteAsync("Check compliance");

        Assert.Contains("AzurePolicy", result.ToolPlugins);
        Assert.Contains("AzureResourceGraph", result.ToolPlugins);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RouteAsync_GitHubBot_ReturnsGitHubBotRouting()
    {
        SetupTriageResponse("GitHubBot");

        var result = await _sut.RouteAsync("Create an issue in idp-core");

        Assert.Equal("GitHubBot", result.AgentName);
        Assert.Contains("GitHub", result.ToolPlugins);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RouteAsync_KnowledgeBot_ReturnsKnowledgeBotRouting()
    {
        SetupTriageResponse("KnowledgeBot");

        var result = await _sut.RouteAsync("How do we handle DNS delegation?");

        Assert.Equal("KnowledgeBot", result.AgentName);
        Assert.Contains("Knowledge", result.ToolPlugins);
        Assert.Contains("KnowledgeBot", result.SystemPrompt);
    }

    private void SetupTriageResponse(string agentName)
    {
        _mockChatCompletion
            .Setup(x => x.GetChatMessageContentsAsync(
                It.IsAny<ChatHistory>(),
                It.IsAny<PromptExecutionSettings>(),
                It.IsAny<Kernel>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ChatMessageContent(AuthorRole.Assistant, agentName)]);
    }
}
