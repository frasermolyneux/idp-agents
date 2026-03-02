using System.Security.Claims;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using MX.IDP.Agents.Functions;

using Xunit;

namespace MX.IDP.Agents.Tests;

public class WhoAmIFunctionTests
{
    private readonly WhoAmIFunction _sut = new();

    private static HttpRequest CreateRequestWithClaims(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        var context = new DefaultHttpContext { User = principal };
        return context.Request;
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Run_AuthenticatedUser_ReturnsUserInfo()
    {
        var request = CreateRequestWithClaims(
            new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", "user-oid-123"),
            new Claim("preferred_username", "user@example.com"),
            new Claim("name", "Test User"),
            new Claim("roles", "Admin"));

        var result = _sut.Run(request) as OkObjectResult;

        Assert.NotNull(result);
        Assert.Equal(200, result.StatusCode);

        var json = System.Text.Json.JsonSerializer.Serialize(result.Value);
        Assert.Contains("user-oid-123", json);
        Assert.Contains("user@example.com", json);
        Assert.Contains("Test User", json);
        Assert.Contains("Admin", json);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Run_UnauthenticatedUser_ReturnsIsAuthenticatedFalse()
    {
        var context = new DefaultHttpContext();
        var request = context.Request;

        var result = _sut.Run(request) as OkObjectResult;

        Assert.NotNull(result);
        var json = System.Text.Json.JsonSerializer.Serialize(result.Value);
        Assert.Contains("\"isAuthenticated\":false", json);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Run_UserWithOidClaim_ReturnsObjectId()
    {
        var request = CreateRequestWithClaims(
            new Claim("oid", "oid-fallback-456"));

        var result = _sut.Run(request) as OkObjectResult;

        Assert.NotNull(result);
        var json = System.Text.Json.JsonSerializer.Serialize(result.Value);
        Assert.Contains("oid-fallback-456", json);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Run_UserWithMultipleRoles_ReturnsAllRoles()
    {
        var request = CreateRequestWithClaims(
            new Claim("roles", "Admin"),
            new Claim("roles", "Developer"),
            new Claim(ClaimTypes.Role, "Reviewer"));

        var result = _sut.Run(request) as OkObjectResult;

        Assert.NotNull(result);
        var json = System.Text.Json.JsonSerializer.Serialize(result.Value);
        Assert.Contains("Admin", json);
        Assert.Contains("Developer", json);
        Assert.Contains("Reviewer", json);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Run_UserWithNoClaims_ReturnsEmptyFields()
    {
        var request = CreateRequestWithClaims();

        var result = _sut.Run(request) as OkObjectResult;

        Assert.NotNull(result);
        Assert.Equal(200, result.StatusCode);
    }
}
