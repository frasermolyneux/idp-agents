using System.ComponentModel;
using System.Text.Json;

using Azure.ResourceManager;
using Azure.ResourceManager.ResourceGraph;
using Azure.ResourceManager.ResourceGraph.Models;

using Microsoft.ApplicationInsights;
using Microsoft.SemanticKernel;

namespace MX.IDP.Agents.Tools;

public class ResourceGraphTool
{
    private readonly ArmClient _armClient;
    private readonly TelemetryClient? _telemetryClient;

    public ResourceGraphTool(ArmClient armClient, TelemetryClient? telemetryClient = null)
    {
        _armClient = armClient;
        _telemetryClient = telemetryClient;
    }

    [KernelFunction("query_resources")]
    [Description("Runs an Azure Resource Graph KQL query across all accessible subscriptions. Use this to find resources, count resource types, check tags, or investigate infrastructure. Example queries: 'Resources | summarize count() by type | order by count_ desc | take 10', 'Resources | where type =~ \"microsoft.compute/virtualmachines\" | project name, resourceGroup, location'.")]
    public async Task<string> QueryResourcesAsync(
        [Description("The KQL query to run against Azure Resource Graph")] string query,
        [Description("Optional: comma-separated subscription IDs to scope the query. Leave empty for all subscriptions.")] string? subscriptionIds = null)
    {
        _telemetryClient?.TrackEvent("ToolInvocation", new Dictionary<string, string>
        {
            ["Tool"] = "query_resources",
            ["Query"] = query.Length > 200 ? query[..200] : query
        });

        var tenant = _armClient.GetTenants().First();

        var content = new ResourceQueryContent(query);

        if (!string.IsNullOrEmpty(subscriptionIds))
        {
            foreach (var subId in subscriptionIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                content.Subscriptions.Add(subId);
            }
        }
        else
        {
            await foreach (var sub in _armClient.GetSubscriptions().GetAllAsync())
            {
                content.Subscriptions.Add(sub.Data.SubscriptionId);
            }
        }

        var response = await tenant.GetResourcesAsync(content);
        var result = response.Value;

        return JsonSerializer.Serialize(new
        {
            totalRecords = result.TotalRecords,
            count = result.Count,
            data = result.Data.ToString()
        }, new JsonSerializerOptions { WriteIndented = true });
    }
}
