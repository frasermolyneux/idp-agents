using System.ComponentModel;
using System.Text.Json;

using Azure.ResourceManager;
using Azure.ResourceManager.ResourceGraph;
using Azure.ResourceManager.ResourceGraph.Models;

using Microsoft.ApplicationInsights;
using Microsoft.SemanticKernel;

namespace MX.IDP.Agents.Tools;

public class PolicyTool
{
    private readonly ArmClient _armClient;
    private readonly TelemetryClient? _telemetryClient;

    public PolicyTool(ArmClient armClient, TelemetryClient? telemetryClient = null)
    {
        _armClient = armClient;
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

        var query = @"
            PolicyResources
            | where type == 'microsoft.policyinsights/policystates'
            | extend complianceState = tostring(properties.complianceState)
            | summarize compliant = countif(complianceState == 'Compliant'),
                        nonCompliant = countif(complianceState == 'NonCompliant')
                        by subscriptionId
            | order by nonCompliant desc";

        return await RunResourceGraphQuery(query, subscriptionId);
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

        var query = $@"
            PolicyResources
            | where type == 'microsoft.policyinsights/policystates'
            | where properties.complianceState == 'NonCompliant'
            | extend resourceId = tostring(properties.resourceId),
                     resourceType = tostring(properties.resourceType),
                     policyDefinition = tostring(properties.policyDefinitionName),
                     policyAssignment = tostring(properties.policyAssignmentName)
            | project subscriptionId, resourceId, resourceType, policyDefinition, policyAssignment
            | take {maxResults}";

        return await RunResourceGraphQuery(query, subscriptionId);
    }

    private async Task<string> RunResourceGraphQuery(string query, string? subscriptionId)
    {
        var tenant = _armClient.GetTenants().First();
        var content = new ResourceQueryContent(query);

        if (!string.IsNullOrEmpty(subscriptionId))
        {
            content.Subscriptions.Add(subscriptionId);
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
