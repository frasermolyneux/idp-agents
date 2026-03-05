using Microsoft.Extensions.Logging;

using MX.IDP.Agents.Models;

namespace MX.IDP.Agents.Services.CampaignSources;

public class DependabotCampaignSource : ICampaignDataSource
{
    public string SourceType => "dependabot";

    private readonly IGitHubQueryService _ghService;
    private readonly ILogger<DependabotCampaignSource> _logger;

    public DependabotCampaignSource(IGitHubQueryService ghService, ILogger<DependabotCampaignSource> logger)
    {
        _ghService = ghService;
        _logger = logger;
    }

    public async Task<List<CampaignFinding>> ScanAsync(CampaignFilter? filter)
    {
        var alerts = await _ghService.GetDependabotAlertsAsync(filter?.Repos, filter?.Impact);

        var findings = alerts
            .Where(a => filter?.ExcludeRepos is null || !filter.ExcludeRepos.Contains(a.Repo, StringComparer.OrdinalIgnoreCase))
            .Where(a => filter?.CreatedAfter is null || (a.CreatedAt.HasValue && a.CreatedAt.Value >= filter.CreatedAfter.Value))
            .Select(a => new CampaignFinding
            {
                SourceType = "dependabot",
                Title = $"[Dependabot] {a.Summary} in {a.Repo}",
                Description = $"**Package**: {a.PackageName} ({a.Ecosystem})\n**Advisory**: {a.Summary}\n**Severity**: {a.Severity}\n**CVE**: {a.CveId ?? "N/A"}\n**Fix**: Update to {a.FixVersion ?? "latest version"}",
                Severity = a.Severity,
                Repo = a.Repo,
                ResourceId = $"dependabot:{a.Repo}:{a.Number}",
                DeduplicationKey = $"dependabot:{a.Repo}:{a.Number}"
            }).ToList();

        _logger.LogInformation("Dependabot scan found {Count} findings", findings.Count);
        return findings;
    }
}
