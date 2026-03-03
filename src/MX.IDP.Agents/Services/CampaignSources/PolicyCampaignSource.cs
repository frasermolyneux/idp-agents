using System.Text.Json;

using Microsoft.Extensions.Logging;

using MX.IDP.Agents.Models;

namespace MX.IDP.Agents.Services.CampaignSources;

public class PolicyCampaignSource : ICampaignDataSource
{
    public string SourceType => "policy";

    private readonly IResourceGraphService _argService;
    private readonly IResourceRepoMapper _repoMapper;
    private readonly ILogger<PolicyCampaignSource> _logger;

    public PolicyCampaignSource(IResourceGraphService argService, IResourceRepoMapper repoMapper, ILogger<PolicyCampaignSource> logger)
    {
        _argService = argService;
        _repoMapper = repoMapper;
        _logger = logger;
    }

    public async Task<List<CampaignFinding>> ScanAsync(CampaignFilter? filter)
    {
        var findings = new List<CampaignFinding>();

        var result = await _argService.GetNonCompliantResourcesAsync(null, 200);

        var rows = JsonDocument.Parse(result.Data).RootElement;
        foreach (var row in rows.EnumerateArray())
        {
            var resourceId = row.TryGetProperty("resourceId", out var rid) ? rid.GetString() ?? "" : row.GetProperty("id").GetString() ?? "";
            var policyName = row.TryGetProperty("policyDefinition", out var pn) ? pn.GetString() : "Unknown policy";
            var resourceType = row.TryGetProperty("resourceType", out var rt) ? rt.GetString() : "";

            var repo = await _repoMapper.MapResourceToRepoAsync(resourceId);
            if (filter?.Repos is not null && repo is not null && !filter.Repos.Contains(repo, StringComparer.OrdinalIgnoreCase))
                continue;

            findings.Add(new CampaignFinding
            {
                SourceType = "policy",
                Title = $"[Policy] Non-compliant: {policyName}",
                Description = $"**Policy**: {policyName}\n**Resource Type**: {resourceType}\n**Resource**: `{resourceId}`",
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
