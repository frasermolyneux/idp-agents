using System.Text.Json;

using Azure.ResourceManager;
using Azure.ResourceManager.ResourceGraph;
using Azure.ResourceManager.ResourceGraph.Models;

using Microsoft.Extensions.Logging;

using MX.IDP.Agents.Models;

namespace MX.IDP.Agents.Services;

public interface IResourceRepoMapper
{
    Task<string?> MapResourceToRepoAsync(string resourceId);
    Task<Dictionary<string, string>> MapResourceGroupsToReposAsync();
}

public class ResourceRepoMapper : IResourceRepoMapper
{
    private readonly ArmClient _armClient;
    private readonly ILogger<ResourceRepoMapper> _logger;
    private Dictionary<string, string>? _cachedMappings;

    public ResourceRepoMapper(ArmClient armClient, ILogger<ResourceRepoMapper> logger)
    {
        _armClient = armClient;
        _logger = logger;
    }

    public async Task<string?> MapResourceToRepoAsync(string resourceId)
    {
        var parts = resourceId.Split('/');
        var rgIndex = Array.FindIndex(parts, p => p.Equals("resourceGroups", StringComparison.OrdinalIgnoreCase));
        if (rgIndex < 0 || rgIndex + 1 >= parts.Length) return null;

        var rgName = parts[rgIndex + 1];
        var mappings = await MapResourceGroupsToReposAsync();
        return mappings.GetValueOrDefault(rgName);
    }

    public async Task<Dictionary<string, string>> MapResourceGroupsToReposAsync()
    {
        if (_cachedMappings is not null) return _cachedMappings;

        var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var query = "resourcecontainers | where type == 'microsoft.resources/subscriptions/resourcegroups' | where isnotnull(tags.Workload) | project name, workload=tags.Workload";
        var request = new ResourceQueryContent(query);

        await foreach (var sub in _armClient.GetSubscriptions().GetAllAsync())
        {
            request.Subscriptions.Add(sub.Data.SubscriptionId);
        }

        try
        {
            var tenant = _armClient.GetTenants().First();
            var result = await tenant.GetResourcesAsync(request);

            var rows = JsonDocument.Parse(result.Value.Data.ToString()).RootElement;
            foreach (var row in rows.EnumerateArray())
            {
                var name = row.GetProperty("name").GetString();
                var workload = row.GetProperty("workload").GetString();
                if (name is not null && workload is not null)
                {
                    mappings[name] = workload;
                }
            }

            _logger.LogInformation("Mapped {Count} resource groups to workload repos", mappings.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query resource groups for Workload tags");
        }

        _cachedMappings = mappings;
        return mappings;
    }
}
