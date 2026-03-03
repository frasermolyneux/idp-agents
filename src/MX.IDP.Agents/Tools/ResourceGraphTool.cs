using System.ComponentModel;
using System.Text.Json;

using Microsoft.ApplicationInsights;
using Microsoft.SemanticKernel;

using MX.IDP.Agents.Services;

namespace MX.IDP.Agents.Tools;

public class ResourceGraphTool
{
    private readonly IResourceGraphService _argService;
    private readonly TelemetryClient? _telemetryClient;

    public ResourceGraphTool(IResourceGraphService argService, TelemetryClient? telemetryClient = null)
    {
        _argService = argService;
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

        var result = await _argService.QueryAsync(query, subscriptionIds);

        return JsonSerializer.Serialize(new
        {
            totalRecords = result.TotalRecords,
            count = result.Count,
            data = result.Data
        }, new JsonSerializerOptions { WriteIndented = true });
    }
}
