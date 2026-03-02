using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace MX.IDP.Agents.Functions;

public class WhoAmIFunction
{
    [Function("WhoAmI")]
    public IActionResult Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "whoami")] HttpRequest req)
    {
        var user = req.HttpContext.User;

        var result = new
        {
            objectId = user.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
                       ?? user.FindFirst("oid")?.Value,
            preferredUsername = user.FindFirst("preferred_username")?.Value,
            name = user.FindFirst("name")?.Value,
            roles = user.Claims
                .Where(c => c.Type == "roles" || c.Type == System.Security.Claims.ClaimTypes.Role)
                .Select(c => c.Value)
                .ToList(),
            isAuthenticated = user.Identity?.IsAuthenticated ?? false
        };

        return new OkObjectResult(result);
    }
}
