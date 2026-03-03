using System.Text.Json;

using Azure.ResourceManager.ResourceGraph;
using Azure.ResourceManager.ResourceGraph.Models;
using Azure.ResourceManager;

using Microsoft.Extensions.Logging;

using MX.IDP.Agents.Models;

namespace MX.IDP.Agents.Services.CampaignSources;

/// <summary>
/// Executes user-defined ARG KQL queries and maps results to campaign findings.
/// The KQL query must return columns: name, type, resourceGroup, subscriptionId.
/// Optional columns: severity, description.
/// </summary>
public class KqlCampaignSource : ICampaignDataSource
{
    public string SourceType => "kql";

    private readonly ArmClient _armClient;
    private readonly IResourceRepoMapper _repoMapper;
    private readonly ILogger<KqlCampaignSource> _logger;

    public KqlCampaignSource(ArmClient armClient, IResourceRepoMapper repoMapper, ILogger<KqlCampaignSource> logger)
    {
        _armClient = armClient;
        _repoMapper = repoMapper;
        _logger = logger;
    }

    public async Task<List<CampaignFinding>> ScanAsync(CampaignFilter? filter)
    {
        return await ScanWithQueryAsync(null, filter);
    }

    public async Task<List<CampaignFinding>> ScanWithQueryAsync(string? kqlQuery, CampaignFilter? filter)
    {
        var findings = new List<CampaignFinding>();

        if (string.IsNullOrWhiteSpace(kqlQuery))
        {
            _logger.LogWarning("KQL campaign source called without a query");
            return findings;
        }

        // Validate query doesn't contain dangerous operations
        if (!ValidateQuery(kqlQuery))
        {
            _logger.LogWarning("KQL query rejected by validation: {Query}", kqlQuery);
            return findings;
        }

        try
        {
            var tenant = _armClient.GetTenants().First();
            var queryContent = new ResourceQueryContent(kqlQuery);

            // Add subscription filters if specified
            if (filter?.SubscriptionIds is not null)
            {
                foreach (var subId in filter.SubscriptionIds)
                    queryContent.Subscriptions.Add(subId);
            }

            var result = await tenant.GetResourcesAsync(queryContent);
            var data = JsonDocument.Parse(result.Value.Data.ToString()).RootElement;

            if (!data.TryGetProperty("rows", out var rows))
            {
                _logger.LogInformation("KQL query returned no rows");
                return findings;
            }

            // Get column names from response
            var columns = new List<string>();
            if (data.TryGetProperty("columns", out var cols))
            {
                foreach (var col in cols.EnumerateArray())
                    columns.Add(col.GetProperty("name").GetString() ?? "");
            }

            var nameIdx = columns.IndexOf("name");
            var typeIdx = columns.IndexOf("type");
            var rgIdx = columns.IndexOf("resourceGroup");
            var subIdx = columns.IndexOf("subscriptionId");
            var severityIdx = columns.IndexOf("severity");
            var descIdx = columns.IndexOf("description");

            foreach (var row in rows.EnumerateArray())
            {
                var resourceName = nameIdx >= 0 ? row[nameIdx].GetString() ?? "unknown" : "unknown";
                var resourceType = typeIdx >= 0 ? row[typeIdx].GetString() ?? "" : "";
                var resourceGroup = rgIdx >= 0 ? row[rgIdx].GetString() ?? "" : "";
                var subscriptionId = subIdx >= 0 ? row[subIdx].GetString() ?? "" : "";
                var severity = severityIdx >= 0 ? row[severityIdx].GetString() ?? "Medium" : "Medium";
                var description = descIdx >= 0 ? row[descIdx].GetString() ?? "" : "";

                if (filter?.Impact is not null && !string.Equals(severity, filter.Impact, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Map resource to repo via Workload tag
                var fullResourceId = $"/subscriptions/{subscriptionId}/resourceGroups/{resourceGroup}/providers/{resourceType}/{resourceName}";
                var repo = await _repoMapper.MapResourceToRepoAsync(fullResourceId);

                if (filter?.Repos is not null && repo is not null && !filter.Repos.Contains(repo, StringComparer.OrdinalIgnoreCase))
                    continue;

                var resourceId = fullResourceId;

                findings.Add(new CampaignFinding
                {
                    SourceType = "kql",
                    Title = $"[KQL] {resourceName} ({resourceType.Split('/').LastOrDefault() ?? resourceType})",
                    Description = string.IsNullOrEmpty(description)
                        ? $"Resource `{resourceName}` of type `{resourceType}` in resource group `{resourceGroup}` matched the KQL campaign query."
                        : description,
                    Severity = MapSeverity(severity),
                    ResourceId = resourceId,
                    Repo = repo,
                    DeduplicationKey = $"kql:{resourceId}"
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute KQL campaign query");
        }

        _logger.LogInformation("KQL scan found {Count} findings", findings.Count);
        return findings;
    }

    private static bool ValidateQuery(string query)
    {
        var lower = query.ToLowerInvariant().Trim();
        // Block mutations — ARG is read-only anyway, but be explicit
        if (lower.Contains("update ") || lower.Contains("delete ") || lower.Contains("drop "))
            return false;
        // Must start with a resource type query
        return lower.StartsWith("resources") || lower.StartsWith("resourcecontainers") ||
               lower.StartsWith("servicehealthresources") || lower.StartsWith("advisorresources") ||
               lower.StartsWith("securityresources") || lower.StartsWith("policyresources");
    }

    private static string MapSeverity(string severity) => severity.ToLowerInvariant() switch
    {
        "critical" or "high" => "High",
        "medium" or "warning" => "Medium",
        "low" or "informational" => "Low",
        _ => "Medium"
    };
}
