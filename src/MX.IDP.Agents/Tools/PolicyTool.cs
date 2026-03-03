using System.ComponentModel;
using System.Text.Json;

using Microsoft.ApplicationInsights;
using Microsoft.SemanticKernel;

using MX.IDP.Agents.Services;

namespace MX.IDP.Agents.Tools;

public class PolicyTool
{
    private readonly IResourceGraphService _argService;
    private readonly TelemetryClient? _telemetryClient;

    public PolicyTool(IResourceGraphService argService, TelemetryClient? telemetryClient = null)
    {
        _argService = argService;
        _telemetryClient = telemetryClient;
    }

    [KernelFunction("get_policy_compliance")]
    [Description("Gets Azure Policy compliance summary across all subscriptions. Shows compliant vs non-compliant resource counts per subscription and policy.")]
    public async Task<string> GetPolicyComplianceAsync(
        [Description("Optional: specific subscription ID to scope the query. Leave empty for all subscriptions.")] string? subscriptionId = null)
    {
        _telemetryClient?.TrackEvent("ToolInvocation", new Dictionary<string, string>
        {
            ["Tool"] = "get_policy_compliance",
            ["SubscriptionId"] = subscriptionId ?? "all"
        });

        var result = await _argService.GetPolicyComplianceSummaryAsync(subscriptionId);
        return FormatResult(result);
    }

    [KernelFunction("get_non_compliant_resources")]
    [Description("Gets a list of non-compliant Azure resources. Shows resource name, type, policy definition, and compliance details.")]
    public async Task<string> GetNonCompliantResourcesAsync(
        [Description("Optional: subscription ID to scope the query. Leave empty for all subscriptions.")] string? subscriptionId = null,
        [Description("Optional: maximum number of results to return. Default 25.")] int maxResults = 25)
    {
        _telemetryClient?.TrackEvent("ToolInvocation", new Dictionary<string, string>
        {
            ["Tool"] = "get_non_compliant_resources",
            ["SubscriptionId"] = subscriptionId ?? "all"
        });

        var result = await _argService.GetNonCompliantResourcesAsync(subscriptionId, maxResults);
        return FormatResult(result);
    }

    private static string FormatResult(ResourceGraphResult result) =>
        JsonSerializer.Serialize(new
        {
            totalRecords = result.TotalRecords,
            count = result.Count,
            data = result.Data
        }, new JsonSerializerOptions { WriteIndented = true });
}
