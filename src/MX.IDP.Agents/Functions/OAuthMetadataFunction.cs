using System.Net;
using System.Text.Json;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MX.IDP.Agents.Functions;

public class OAuthMetadataFunction
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<OAuthMetadataFunction> _logger;

    public OAuthMetadataFunction(IConfiguration configuration, ILogger<OAuthMetadataFunction> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    [Function("OAuthProtectedResource")]
    public async Task<HttpResponseData> GetOAuthProtectedResource(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = ".well-known/oauth-protected-resource")] HttpRequestData req)
    {
        _logger.LogInformation("OAuth protected resource metadata requested");

        var tenantId = _configuration["AzureAd:TenantId"];
        var audience = _configuration["AzureAd:Audience"];

        var metadata = new
        {
            resource = audience,
            authorization_servers = new[]
            {
                $"https://login.microsoftonline.com/{tenantId}/v2.0"
            },
            scopes_supported = new[]
            {
                $"{audience}/Mcp.Read",
                $"{audience}/Mcp.ReadWrite"
            }
        };

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");
        await response.WriteStringAsync(JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
        return response;
    }
}
