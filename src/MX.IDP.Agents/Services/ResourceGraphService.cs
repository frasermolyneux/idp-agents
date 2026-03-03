using System.Text.Json;

using Azure.ResourceManager;
using Azure.ResourceManager.ResourceGraph;
using Azure.ResourceManager.ResourceGraph.Models;

using Microsoft.Extensions.Logging;

namespace MX.IDP.Agents.Services;

/// <summary>
/// Shared service for Azure Resource Graph queries.
/// Eliminates duplicated subscription enumeration and query execution across tools and campaign sources.
/// </summary>
public interface IResourceGraphService
{
    Task<ResourceGraphResult> QueryAsync(string kql, string? subscriptionIds = null);
    Task<ResourceGraphResult> GetAdvisorRecommendationsAsync(string? category = null, string? impact = null, int maxResults = 25, string? subcategory = null);
    Task<ResourceGraphResult> GetPolicyComplianceSummaryAsync(string? subscriptionId = null);
    Task<ResourceGraphResult> GetNonCompliantResourcesAsync(string? subscriptionId = null, int maxResults = 25);
}

public class ResourceGraphResult
{
    public long TotalRecords { get; set; }
    public long Count { get; set; }
    public string Data { get; set; } = "[]";
}

public class ResourceGraphService : IResourceGraphService
{
    private readonly ArmClient _armClient;
    private readonly ILogger<ResourceGraphService> _logger;

    public ResourceGraphService(ArmClient armClient, ILogger<ResourceGraphService> logger)
    {
        _armClient = armClient;
        _logger = logger;
    }

    public async Task<ResourceGraphResult> QueryAsync(string kql, string? subscriptionIds = null)
    {
        var tenant = _armClient.GetTenants().First();
        var content = new ResourceQueryContent(kql);

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

        _logger.LogInformation("Executing ARG query across {SubCount} subscriptions", content.Subscriptions.Count);
        var response = await tenant.GetResourcesAsync(content);
        var result = response.Value;

        return new ResourceGraphResult
        {
            TotalRecords = result.TotalRecords,
            Count = result.Count,
            Data = result.Data.ToString()
        };
    }

    public async Task<ResourceGraphResult> GetAdvisorRecommendationsAsync(string? category = null, string? impact = null, int maxResults = 25, string? subcategory = null)
    {
        var filters = new List<string>();
        if (!string.IsNullOrEmpty(category))
            filters.Add($"| where properties.category == '{category}'");
        if (!string.IsNullOrEmpty(impact))
            filters.Add($"| where properties.impact == '{impact}'");
        if (!string.IsNullOrEmpty(subcategory))
            filters.Add($"| where properties.recommendationSubcategory == '{subcategory}'");

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
            | project id, resourceGroup, subscriptionId, category, impact, problem, solution, impactedField, impactedValue, properties
            | take {maxResults}";

        return await QueryAsync(query);
    }

    public async Task<ResourceGraphResult> GetPolicyComplianceSummaryAsync(string? subscriptionId = null)
    {
        var query = @"
            PolicyResources
            | where type == 'microsoft.policyinsights/policystates'
            | extend complianceState = tostring(properties.complianceState)
            | summarize compliant = countif(complianceState == 'Compliant'),
                        nonCompliant = countif(complianceState == 'NonCompliant')
                        by subscriptionId
            | order by nonCompliant desc";

        return await QueryAsync(query, subscriptionId);
    }

    public async Task<ResourceGraphResult> GetNonCompliantResourcesAsync(string? subscriptionId = null, int maxResults = 25)
    {
        var query = $@"
            PolicyResources
            | where type == 'microsoft.policyinsights/policystates'
            | where properties.complianceState == 'NonCompliant'
            | extend resourceId = tostring(properties.resourceId),
                     resourceType = tostring(properties.resourceType),
                     policyDefinition = tostring(properties.policyDefinitionName),
                     policyAssignment = tostring(properties.policyAssignmentName)
            | project id, resourceGroup, subscriptionId, resourceId, resourceType, policyDefinition, policyAssignment, properties
            | take {maxResults}";

        return await QueryAsync(query, subscriptionId);
    }
}
