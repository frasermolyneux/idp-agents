using System.ComponentModel;
using System.Text.Json;

using Azure.ResourceManager;
using Azure.ResourceManager.ResourceGraph;
using Azure.ResourceManager.ResourceGraph.Models;

using Microsoft.ApplicationInsights;
using Microsoft.SemanticKernel;

namespace MX.IDP.Agents.Tools;

public class AdvisorTool
{
    private readonly ArmClient _armClient;
    private readonly TelemetryClient? _telemetryClient;

    public AdvisorTool(ArmClient armClient, TelemetryClient? telemetryClient = null)
    {
        _armClient = armClient;
        _telemetryClient = telemetryClient;
    }

    [KernelFunction("get_advisor_recommendations")]
    [Description("Gets Azure Advisor recommendations across all subscriptions. Recommendations cover cost, security, reliability, operational excellence, and performance. Optionally filter by category and impact.")]
    public async Task<string> GetRecommendationsAsync(
        [Description("Optional: filter by category — Cost, Security, Reliability, OperationalExcellence, Performance. Leave empty for all.")] string? category = null,
        [Description("Optional: filter by impact — High, Medium, Low. Leave empty for all.")] string? impact = null,
        [Description("Optional: maximum number of recommendations to return. Default 25.")] int maxResults = 25)
    {
        _telemetryClient?.TrackEvent("ToolInvocation", new Dictionary<string, string>
        {
            ["Tool"] = "get_advisor_recommendations",
            ["Category"] = category ?? "all",
            ["Impact"] = impact ?? "all"
        });

        var filters = new List<string>();
        if (!string.IsNullOrEmpty(category))
            filters.Add($"| where properties.category == '{category}'");
        if (!string.IsNullOrEmpty(impact))
            filters.Add($"| where properties.impact == '{impact}'");

        var query = $@"
            AdvisorResources
            | where type == 'microsoft.advisor/recommendations'
            {string.Join("\n            ", filters)}
            | extend category = tostring(properties.category),
                     impact = tostring(properties.impact),
                     problem = tostring(properties.shortDescription.problem),
                     solution = tostring(properties.shortDescription.solution),
                     impactedField = tostring(properties.impactedField),
                     impactedValue = tostring(properties.impactedValue)
            | project subscriptionId, category, impact, problem, solution, impactedField, impactedValue
            | take {maxResults}";

        var tenant = _armClient.GetTenants().First();
        var content = new ResourceQueryContent(query);

        await foreach (var sub in _armClient.GetSubscriptions().GetAllAsync())
        {
            content.Subscriptions.Add(sub.Data.SubscriptionId);
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
