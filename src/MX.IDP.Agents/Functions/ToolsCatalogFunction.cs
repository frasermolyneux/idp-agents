using System.ComponentModel;
using System.Reflection;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.SemanticKernel;

using MX.IDP.Agents.Tools;

namespace MX.IDP.Agents.Functions;

public class ToolsCatalogFunction
{
    private static readonly Type[] ToolTypes =
    [
        typeof(SubscriptionTool),
        typeof(ResourceGraphTool),
        typeof(AdvisorTool),
        typeof(PolicyTool),
        typeof(GitHubTool),
        typeof(KnowledgeTool),
        typeof(CampaignTool)
    ];

    private static readonly Dictionary<string, string> PluginNames = new()
    {
        [nameof(SubscriptionTool)] = "AzureSubscriptions",
        [nameof(ResourceGraphTool)] = "AzureResourceGraph",
        [nameof(AdvisorTool)] = "AzureAdvisor",
        [nameof(PolicyTool)] = "AzurePolicy",
        [nameof(GitHubTool)] = "GitHub",
        [nameof(KnowledgeTool)] = "Knowledge",
        [nameof(CampaignTool)] = "Campaign"
    };

    private static readonly Dictionary<string, string[]> AgentPlugins = new()
    {
        ["OpsBot"] = ["AzureSubscriptions", "AzureResourceGraph", "AzureAdvisor"],
        ["ComplianceBot"] = ["AzurePolicy", "AzureResourceGraph", "GitHub"],
        ["GitHubBot"] = ["GitHub"],
        ["KnowledgeBot"] = ["Knowledge"],
        ["CampaignBot"] = ["Campaign"],
        ["GeneralBot"] = ["AzureSubscriptions", "AzureResourceGraph", "AzureAdvisor", "AzurePolicy", "GitHub", "Knowledge", "Campaign"]
    };

    [Function("GetToolsCatalog")]
    public IActionResult GetToolsCatalog(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "tools/catalog")] HttpRequest req)
    {
        var catalog = BuildCatalog();
        return new OkObjectResult(catalog);
    }

    private static object BuildCatalog()
    {
        var plugins = new List<object>();

        foreach (var toolType in ToolTypes)
        {
            var pluginName = PluginNames.GetValueOrDefault(toolType.Name, toolType.Name);
            var agents = AgentPlugins
                .Where(kv => kv.Value.Contains(pluginName))
                .Select(kv => kv.Key)
                .ToList();

            var functions = new List<object>();

            foreach (var method in toolType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var kfAttr = method.GetCustomAttribute<KernelFunctionAttribute>();
                if (kfAttr is null) continue;

                var descAttr = method.GetCustomAttribute<DescriptionAttribute>();

                var parameters = new List<object>();
                foreach (var param in method.GetParameters())
                {
                    var paramDesc = param.GetCustomAttribute<DescriptionAttribute>();
                    parameters.Add(new
                    {
                        name = param.Name,
                        type = SimplifyType(param.ParameterType),
                        description = paramDesc?.Description ?? "",
                        required = !param.HasDefaultValue,
                        defaultValue = param.HasDefaultValue ? param.DefaultValue?.ToString() : null
                    });
                }

                functions.Add(new
                {
                    name = kfAttr.Name,
                    description = descAttr?.Description ?? "",
                    parameters
                });
            }

            plugins.Add(new
            {
                plugin = pluginName,
                toolClass = toolType.Name,
                agents,
                functions
            });
        }

        return new
        {
            totalPlugins = plugins.Count,
            totalFunctions = plugins.Sum(p => ((List<object>)((dynamic)p).functions).Count),
            plugins
        };
    }

    private static string SimplifyType(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (underlying == typeof(string)) return "string";
        if (underlying == typeof(int)) return "int";
        if (underlying == typeof(bool)) return "bool";
        if (underlying == typeof(long)) return "long";
        if (underlying == typeof(double)) return "double";
        if (underlying.IsGenericType && underlying.GetGenericTypeDefinition() == typeof(List<>))
            return $"List<{SimplifyType(underlying.GetGenericArguments()[0])}>";
        return underlying.Name;
    }
}
