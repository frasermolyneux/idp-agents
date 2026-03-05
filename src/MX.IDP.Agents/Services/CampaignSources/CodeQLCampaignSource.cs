using Microsoft.Extensions.Logging;

using MX.IDP.Agents.Models;

namespace MX.IDP.Agents.Services.CampaignSources;

public class CodeQLCampaignSource : ICampaignDataSource
{
    public string SourceType => "codeql";

    private readonly IGitHubQueryService _ghService;
    private readonly ILogger<CodeQLCampaignSource> _logger;

    public CodeQLCampaignSource(IGitHubQueryService ghService, ILogger<CodeQLCampaignSource> logger)
    {
        _ghService = ghService;
        _logger = logger;
    }

    public async Task<List<CampaignFinding>> ScanAsync(CampaignFilter? filter)
    {
        var alerts = await _ghService.GetCodeScanningAlertsAsync(filter?.Repos, filter?.Impact);

        var findings = alerts
            .Where(a => filter?.ExcludeRepos is null || !filter.ExcludeRepos.Contains(a.Repo, StringComparer.OrdinalIgnoreCase))
            .Where(a => filter?.CreatedAfter is null || (a.CreatedAt.HasValue && a.CreatedAt.Value >= filter.CreatedAfter.Value))
            .Select(a => new CampaignFinding
            {
                SourceType = "codeql",
                Title = $"[CodeQL] {(string.IsNullOrEmpty(a.Description) ? a.RuleId : a.Description)} in {a.Repo}",
                Description = $"**Rule**: {a.RuleId} — {a.Description}\n**Severity**: {a.Severity}\n**Location**: `{a.Location}`\n**Tool**: {a.ToolName}\n**Category**: {a.Category ?? "N/A"}",
                Severity = a.Severity,
                Repo = a.Repo,
                ResourceId = $"codeql:{a.Repo}:{a.Number}",
                DeduplicationKey = $"codeql:{a.Repo}:{a.Number}"
            }).ToList();

        _logger.LogInformation("CodeQL scan found {Count} findings", findings.Count);
        return findings;
    }
}
