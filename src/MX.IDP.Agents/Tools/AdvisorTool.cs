using System.ComponentModel;
using System.Text.Json;

using Microsoft.ApplicationInsights;
using Microsoft.SemanticKernel;

using MX.IDP.Agents.Services;

namespace MX.IDP.Agents.Tools;

public class AdvisorTool
{
    private readonly IResourceGraphService _argService;
    private readonly TelemetryClient? _telemetryClient;

    public AdvisorTool(IResourceGraphService argService, TelemetryClient? telemetryClient = null)
    {
        _argService = argService;
        _telemetryClient = telemetryClient;
    }

    [KernelFunction("get_advisor_recommendations")]
    [Description("Gets Azure Advisor recommendations across all subscriptions. Recommendations cover cost, security, reliability, operational excellence, and performance. Optionally filter by category, impact, and subcategory.")]
    public async Task<string> GetRecommendationsAsync(
        [Description("Optional: filter by category — Cost, Security, Reliability, OperationalExcellence, Performance. Leave empty for all.")] string? category = null,
        [Description("Optional: filter by impact — High, Medium, Low. Leave empty for all.")] string? impact = null,
        [Description("Optional: maximum number of recommendations to return. Default 25.")] int maxResults = 25,
        [Description("Optional: filter by subcategory — e.g. ServiceUpgradeAndRetirement for deprecated/retiring services. Leave empty for all.")] string? subcategory = null)
    {
        _telemetryClient?.TrackEvent("ToolInvocation", new Dictionary<string, string>
        {
            ["Tool"] = "get_advisor_recommendations",
            ["Category"] = category ?? "all",
            ["Impact"] = impact ?? "all",
            ["Subcategory"] = subcategory ?? "all"
        });

        var result = await _argService.GetAdvisorRecommendationsAsync(category, impact, maxResults, subcategory);

        return JsonSerializer.Serialize(new
        {
            totalRecords = result.TotalRecords,
            count = result.Count,
            data = result.Data
        }, new JsonSerializerOptions { WriteIndented = true });
    }
}
