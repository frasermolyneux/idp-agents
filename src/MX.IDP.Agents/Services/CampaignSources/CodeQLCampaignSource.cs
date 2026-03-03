using Microsoft.Extensions.Logging;

using MX.IDP.Agents.Models;

namespace MX.IDP.Agents.Services.CampaignSources;

public class CodeQLCampaignSource : ICampaignDataSource
{
    public string SourceType => "codeql";

    private readonly IGitHubClientFactory _gitHubClientFactory;
    private readonly ILogger<CodeQLCampaignSource> _logger;

    public CodeQLCampaignSource(IGitHubClientFactory gitHubClientFactory, ILogger<CodeQLCampaignSource> logger)
    {
        _gitHubClientFactory = gitHubClientFactory;
        _logger = logger;
    }

    public async Task<List<CampaignFinding>> ScanAsync(CampaignFilter? filter)
    {
        var findings = new List<CampaignFinding>();
        var client = await _gitHubClientFactory.CreateClientAsync();
        var repos = await client.GitHubApps.Installation.GetAllRepositoriesForCurrent();

        foreach (var repo in repos.Repositories)
        {
            if (repo.Archived) continue;
            if (filter?.Repos is not null && !filter.Repos.Contains(repo.Name, StringComparer.OrdinalIgnoreCase))
                continue;

            try
            {
                // Code scanning alerts via REST API
                var alerts = await client.Connection.Get<List<CodeScanningAlert>>(
                    new Uri($"repos/{repo.Owner.Login}/{repo.Name}/code-scanning/alerts?state=open&per_page=100", UriKind.Relative),
                    null, "application/vnd.github+json");

                if (alerts?.Body is null) continue;

                foreach (var alert in alerts.Body)
                {
                    var severity = MapSeverity(alert.Rule?.SecuritySeverityLevel ?? alert.Rule?.Severity ?? "warning");

                    if (filter?.Impact is not null && !string.Equals(severity, filter.Impact, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var location = alert.MostRecentInstance?.Location;
                    var locationStr = location is not null ? $"{location.Path}:{location.StartLine}" : "unknown";

                    findings.Add(new CampaignFinding
                    {
                        SourceType = "codeql",
                        Title = $"[CodeQL] {alert.Rule?.Description ?? alert.Rule?.Id ?? "Alert"} in {repo.Name}",
                        Description = $"**Rule**: {alert.Rule?.Id} — {alert.Rule?.Description}\n**Severity**: {severity}\n**Location**: `{locationStr}`\n**Tool**: {alert.Tool?.Name ?? "CodeQL"}\n**Category**: {alert.Rule?.Tags?.FirstOrDefault() ?? "N/A"}",
                        Severity = severity,
                        Repo = repo.Name,
                        ResourceId = $"codeql:{repo.Name}:{alert.Number}",
                        DeduplicationKey = $"codeql:{repo.Name}:{alert.Number}"
                    });
                }
            }
            catch (Octokit.NotFoundException)
            {
                // Code scanning not enabled
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to scan CodeQL alerts for {Repo}", repo.Name);
            }
        }

        _logger.LogInformation("CodeQL scan found {Count} findings", findings.Count);
        return findings;
    }

    private static string MapSeverity(string severity) => severity.ToLowerInvariant() switch
    {
        "critical" => "High",
        "high" => "High",
        "error" => "High",
        "medium" => "Medium",
        "warning" => "Medium",
        "low" => "Low",
        "note" => "Low",
        _ => "Medium"
    };

    // DTOs for Code Scanning REST API
    private class CodeScanningAlert
    {
        public int Number { get; set; }
        public string? State { get; set; }
        public RuleInfo? Rule { get; set; }
        public ToolInfo? Tool { get; set; }
        public AlertInstance? MostRecentInstance { get; set; }
    }

    private class RuleInfo
    {
        public string? Id { get; set; }
        public string? Severity { get; set; }
        public string? SecuritySeverityLevel { get; set; }
        public string? Description { get; set; }
        public List<string>? Tags { get; set; }
    }

    private class ToolInfo
    {
        public string? Name { get; set; }
    }

    private class AlertInstance
    {
        public LocationInfo? Location { get; set; }
    }

    private class LocationInfo
    {
        public string? Path { get; set; }
        public int? StartLine { get; set; }
    }
}
