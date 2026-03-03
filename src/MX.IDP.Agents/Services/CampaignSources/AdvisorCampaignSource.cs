using System.Text.Json;

using Azure.ResourceManager;
using Azure.ResourceManager.ResourceGraph;
using Azure.ResourceManager.ResourceGraph.Models;

using Microsoft.Extensions.Logging;

using MX.IDP.Agents.Models;

namespace MX.IDP.Agents.Services.CampaignSources;

public class AdvisorCampaignSource : ICampaignDataSource
{
    public string SourceType => "advisor";

    private readonly ArmClient _armClient;
    private readonly IResourceRepoMapper _repoMapper;
    private readonly ILogger<AdvisorCampaignSource> _logger;

    public AdvisorCampaignSource(ArmClient armClient, IResourceRepoMapper repoMapper, ILogger<AdvisorCampaignSource> logger)
    {
        _armClient = armClient;
        _repoMapper = repoMapper;
        _logger = logger;
    }

    public async Task<List<CampaignFinding>> ScanAsync(CampaignFilter? filter)
    {
        var findings = new List<CampaignFinding>();

        var query = "advisorresources | where type == 'microsoft.advisor/recommendations'";

        if (filter?.Category is not null)
            query += $" | where properties.category == '{filter.Category}'";
        if (filter?.Impact is not null)
            query += $" | where properties.impact == '{filter.Impact}'";

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
            var category = properties.TryGetProperty("category", out var cat) ? cat.GetString() : "Unknown";
            var impact = properties.TryGetProperty("impact", out var imp) ? imp.GetString() : "Medium";
            var shortDesc = properties.TryGetProperty("shortDescription", out var sd) && sd.TryGetProperty("problem", out var prob) ? prob.GetString() : "Advisor recommendation";
            var recommendation = properties.TryGetProperty("shortDescription", out var sd2) && sd2.TryGetProperty("solution", out var sol) ? sol.GetString() : "";

            var repo = await _repoMapper.MapResourceToRepoAsync(resourceId);
            if (filter?.Repos is not null && repo is not null && !filter.Repos.Contains(repo, StringComparer.OrdinalIgnoreCase))
                continue;

            findings.Add(new CampaignFinding
            {
                SourceType = "advisor",
                Title = $"[Advisor/{category}] {shortDesc}",
                Description = $"**Category**: {category}\n**Impact**: {impact}\n**Resource**: `{resourceId}`\n\n**Recommendation**: {recommendation}",
                Severity = MapImpactToSeverity(impact),
                ResourceId = resourceId,
                Repo = repo,
                DeduplicationKey = $"advisor:{resourceId}:{shortDesc}"
            });
        }

        _logger.LogInformation("Advisor scan found {Count} findings", findings.Count);
        return findings;
    }

    private static string MapImpactToSeverity(string? impact) => impact switch
    {
        "High" => "High",
        "Medium" => "Medium",
        "Low" => "Low",
        _ => "Medium"
    };
}
