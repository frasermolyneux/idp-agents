using System.Text.Json;

using Microsoft.Extensions.Logging;

using MX.IDP.Agents.Models;

namespace MX.IDP.Agents.Services.CampaignSources;

public class AdvisorCampaignSource : ICampaignDataSource
{
    public string SourceType => "advisor";

    private readonly IResourceGraphService _argService;
    private readonly IResourceRepoMapper _repoMapper;
    private readonly ILogger<AdvisorCampaignSource> _logger;

    public AdvisorCampaignSource(IResourceGraphService argService, IResourceRepoMapper repoMapper, ILogger<AdvisorCampaignSource> logger)
    {
        _argService = argService;
        _repoMapper = repoMapper;
        _logger = logger;
    }

    public async Task<List<CampaignFinding>> ScanAsync(CampaignFilter? filter)
    {
        var findings = new List<CampaignFinding>();

        var result = await _argService.GetAdvisorRecommendationsAsync(filter?.Category, filter?.Impact, 200);

        var rows = JsonDocument.Parse(result.Data).RootElement;
        foreach (var row in rows.EnumerateArray())
        {
            var resourceId = row.GetProperty("id").GetString() ?? "";
            var category = row.TryGetProperty("category", out var cat) ? cat.GetString() : "Unknown";
            var impact = row.TryGetProperty("impact", out var imp) ? imp.GetString() : "Medium";
            var problem = row.TryGetProperty("problem", out var prob) ? prob.GetString() : "Advisor recommendation";
            var solution = row.TryGetProperty("solution", out var sol) ? sol.GetString() : "";

            var repo = await _repoMapper.MapResourceToRepoAsync(resourceId);
            if (filter?.Repos is not null && repo is not null && !filter.Repos.Contains(repo, StringComparer.OrdinalIgnoreCase))
                continue;

            findings.Add(new CampaignFinding
            {
                SourceType = "advisor",
                Title = $"[Advisor/{category}] {problem}",
                Description = $"**Category**: {category}\n**Impact**: {impact}\n**Resource**: `{resourceId}`\n\n**Recommendation**: {solution}",
                Severity = MapImpactToSeverity(impact),
                ResourceId = resourceId,
                Repo = repo,
                DeduplicationKey = $"advisor:{resourceId}:{problem}"
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
