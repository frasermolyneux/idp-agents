using System.Text.Json;

using Azure.ResourceManager;
using Azure.ResourceManager.ResourceGraph;
using Azure.ResourceManager.ResourceGraph.Models;

using Microsoft.Extensions.Logging;

using MX.IDP.Agents.Models;

namespace MX.IDP.Agents.Services.CampaignSources;

public class PolicyCampaignSource : ICampaignDataSource
{
    public string SourceType => "policy";

    private readonly ArmClient _armClient;
    private readonly IResourceRepoMapper _repoMapper;
    private readonly ILogger<PolicyCampaignSource> _logger;

    public PolicyCampaignSource(ArmClient armClient, IResourceRepoMapper repoMapper, ILogger<PolicyCampaignSource> logger)
    {
        _armClient = armClient;
        _repoMapper = repoMapper;
        _logger = logger;
    }

    public async Task<List<CampaignFinding>> ScanAsync(CampaignFilter? filter)
    {
        var findings = new List<CampaignFinding>();

        var query = "policyresources | where type == 'microsoft.policyinsights/policystates' | where properties.complianceState == 'NonCompliant'";
        query += " | project id, resourceGroup, subscriptionId, properties";

        var request = new ResourceQueryContent(query);
        await foreach (var sub in _armClient.GetSubscriptions().GetAllAsync())
        {
            request.Subscriptions.Add(sub.Data.SubscriptionId);
        }

        var tenant = _armClient.GetTenants().First();
        var result = await tenant.GetResourcesAsync(request);

        var rows = JsonDocument.Parse(result.Value.Data.ToString()).RootElement;
        foreach (var row in rows.EnumerateArray())
        {
                var resourceId = row.GetProperty("id").GetString() ?? "";
                var properties = row.GetProperty("properties");
                var policyName = properties.TryGetProperty("policyDefinitionName", out var pn) ? pn.GetString() : "Unknown policy";
                var policyAction = properties.TryGetProperty("policyDefinitionAction", out var pa) ? pa.GetString() : "";
                var resourceType = properties.TryGetProperty("resourceType", out var rt) ? rt.GetString() : "";
                var resourceLocation = properties.TryGetProperty("resourceLocation", out var rl) ? rl.GetString() : "";

                var repo = await _repoMapper.MapResourceToRepoAsync(resourceId);
                if (filter?.Repos is not null && repo is not null && !filter.Repos.Contains(repo, StringComparer.OrdinalIgnoreCase))
                    continue;

                findings.Add(new CampaignFinding
                {
                    SourceType = "policy",
                    Title = $"[Policy] Non-compliant: {policyName}",
                    Description = $"**Policy**: {policyName}\n**Action**: {policyAction}\n**Resource Type**: {resourceType}\n**Location**: {resourceLocation}\n**Resource**: `{resourceId}`",
                    Severity = "High",
                    ResourceId = resourceId,
                    Repo = repo,
                    DeduplicationKey = $"policy:{resourceId}:{policyName}"
                });
            }

            _logger.LogInformation("Policy scan found {Count} findings", findings.Count);
        return findings;
    }
}
