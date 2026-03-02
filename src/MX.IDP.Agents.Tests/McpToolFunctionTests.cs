using System.Reflection;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;

using MX.IDP.Agents.Functions;

using Xunit;

namespace MX.IDP.Agents.Tests;

public class McpToolFunctionTests
{
    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("mcp_list_subscriptions", "ListSubscriptions")]
    [InlineData("mcp_query_resources", "QueryResources")]
    [InlineData("mcp_get_advisor_recommendations", "GetAdvisorRecommendations")]
    [InlineData("mcp_get_policy_compliance", "GetPolicyCompliance")]
    [InlineData("mcp_get_non_compliant_resources", "GetNonCompliantResources")]
    public void McpToolFunction_HasFunctionAttribute(string functionName, string methodName)
    {
        var method = typeof(McpToolFunctions).GetMethod(methodName);
        Assert.NotNull(method);

        var attr = method!.GetCustomAttribute<FunctionAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(functionName, attr!.Name);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("ListSubscriptions", "list_subscriptions")]
    [InlineData("QueryResources", "query_resources")]
    [InlineData("GetAdvisorRecommendations", "get_advisor_recommendations")]
    [InlineData("GetPolicyCompliance", "get_policy_compliance")]
    [InlineData("GetNonCompliantResources", "get_non_compliant_resources")]
    public void McpToolFunction_HasMcpToolTrigger(string methodName, string toolName)
    {
        var method = typeof(McpToolFunctions).GetMethod(methodName);
        Assert.NotNull(method);

        var parameters = method!.GetParameters();
        var triggerParam = parameters.FirstOrDefault(p =>
            p.GetCustomAttribute<McpToolTriggerAttribute>() is not null);

        Assert.NotNull(triggerParam);
        var triggerAttr = triggerParam!.GetCustomAttribute<McpToolTriggerAttribute>();
        Assert.Equal(toolName, triggerAttr!.ToolName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void McpToolFunctions_ExposesFiveTools()
    {
        var methods = typeof(McpToolFunctions)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<FunctionAttribute>() is not null)
            .ToList();

        Assert.Equal(5, methods.Count);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void QueryResources_HasRequiredQueryProperty()
    {
        var method = typeof(McpToolFunctions).GetMethod("QueryResources");
        var queryParam = method!.GetParameters()
            .FirstOrDefault(p => p.GetCustomAttribute<McpToolPropertyAttribute>() is not null
                              && p.Name == "query");

        Assert.NotNull(queryParam);
        var propAttr = queryParam!.GetCustomAttribute<McpToolPropertyAttribute>();
        Assert.True(propAttr!.IsRequired);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AllMcpFunctions_ReturnStringTask()
    {
        var methods = typeof(McpToolFunctions)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<FunctionAttribute>() is not null);

        foreach (var method in methods)
        {
            Assert.Equal(typeof(Task<string>), method.ReturnType);
        }
    }
}
