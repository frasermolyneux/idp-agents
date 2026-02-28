using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace MX.IDP.Agents.Tests;

public class HealthFunctionTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void Run_ReturnsHealthy()
    {
        var function = new HealthFunction();
        var context = new DefaultHttpContext();

        var result = function.Run(context.Request) as OkObjectResult;

        Assert.NotNull(result);
        Assert.Equal(200, result.StatusCode);
    }
}
