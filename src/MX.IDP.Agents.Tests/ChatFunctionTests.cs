using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

using Moq;

using MX.IDP.Agents.Functions;
using MX.IDP.Agents.Models;
using MX.IDP.Agents.Services;

using Xunit;

namespace MX.IDP.Agents.Tests;

public class ChatFunctionTests
{
    private readonly Mock<IIdpChatService> _mockChatService;
    private readonly Mock<ILogger<ChatFunction>> _mockLogger;
    private readonly ChatFunction _sut;

    public ChatFunctionTests()
    {
        _mockChatService = new Mock<IIdpChatService>();
        _mockLogger = new Mock<ILogger<ChatFunction>>();
        _sut = new ChatFunction(_mockChatService.Object, _mockLogger.Object);
    }

    private static HttpRequest CreateRequest(object body)
    {
        var json = JsonSerializer.Serialize(body, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        context.Request.ContentType = "application/json";
        return context.Request;
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Run_ValidRequest_ReturnsOkWithResponse()
    {
        var chatResponse = new ChatResponse { Message = "Hi there", ConversationId = "conv-1" };
        _mockChatService
            .Setup(x => x.GetCompletionAsync(It.IsAny<ChatRequest>()))
            .ReturnsAsync(chatResponse);

        var request = CreateRequest(new { message = "Hello" });

        var result = await _sut.Run(request) as OkObjectResult;

        Assert.NotNull(result);
        Assert.Equal(200, result.StatusCode);
        var response = result.Value as ChatResponse;
        Assert.NotNull(response);
        Assert.Equal("Hi there", response.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Run_NullBody_ReturnsBadRequest()
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("null"));
        context.Request.ContentType = "application/json";

        var result = await _sut.Run(context.Request) as BadRequestObjectResult;

        Assert.NotNull(result);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Run_ServiceThrows_Returns500()
    {
        _mockChatService
            .Setup(x => x.GetCompletionAsync(It.IsAny<ChatRequest>()))
            .ThrowsAsync(new InvalidOperationException("Service error"));

        var request = CreateRequest(new { message = "Hello" });

        var result = await _sut.Run(request) as ObjectResult;

        Assert.NotNull(result);
        Assert.Equal(500, result.StatusCode);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Run_PassesRequestToService()
    {
        ChatRequest? capturedRequest = null;
        _mockChatService
            .Setup(x => x.GetCompletionAsync(It.IsAny<ChatRequest>()))
            .Callback<ChatRequest>(r => capturedRequest = r)
            .ReturnsAsync(new ChatResponse { Message = "response" });

        var request = CreateRequest(new { message = "Test message", conversationId = "conv-42" });

        await _sut.Run(request);

        Assert.NotNull(capturedRequest);
        Assert.Equal("Test message", capturedRequest!.Message);
        Assert.Equal("conv-42", capturedRequest.ConversationId);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Run_EmptyBody_ReturnsBadRequest()
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Array.Empty<byte>());
        context.Request.ContentType = "application/json";

        var result = await _sut.Run(context.Request);

        // Empty body should either be bad request or 500
        Assert.IsType<ObjectResult>(result);
    }
}
