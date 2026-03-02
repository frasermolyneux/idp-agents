using System.Text.Json;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

using MX.IDP.Agents.Models;
using MX.IDP.Agents.Services;

namespace MX.IDP.Agents.Functions;

public class ChatFunction
{
    private readonly IIdpChatService _chatService;
    private readonly ILogger<ChatFunction> _logger;

    public ChatFunction(IIdpChatService chatService, ILogger<ChatFunction> logger)
    {
        _chatService = chatService;
        _logger = logger;
    }

    [Function("Chat")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "chat")] HttpRequest req)
    {
        try
        {
            var request = await JsonSerializer.DeserializeAsync<ChatRequest>(req.Body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (request is null)
            {
                return new BadRequestObjectResult(new { error = "Invalid request body." });
            }

            var response = await _chatService.GetCompletionAsync(request);

            return new OkObjectResult(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing chat request");
            return new ObjectResult(new { error = "An internal error occurred." }) { StatusCode = 500 };
        }
    }
}
