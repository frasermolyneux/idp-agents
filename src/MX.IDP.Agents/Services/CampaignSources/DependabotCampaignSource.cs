using Microsoft.Extensions.Logging;

using MX.IDP.Agents.Models;

namespace MX.IDP.Agents.Services.CampaignSources;

public class DependabotCampaignSource : ICampaignDataSource
{
    public string SourceType => "dependabot";

    private readonly IGitHubClientFactory _gitHubClientFactory;
    private readonly ILogger<DependabotCampaignSource> _logger;

    public DependabotCampaignSource(IGitHubClientFactory gitHubClientFactory, ILogger<DependabotCampaignSource> logger)
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
                // Dependabot alerts via REST API (Octokit doesn't have native support yet)
                var alerts = await client.Connection.Get<List<DependabotAlert>>(
                    new Uri($"repos/{repo.Owner.Login}/{repo.Name}/dependabot/alerts?state=open&per_page=100", UriKind.Relative),
                    null, "application/vnd.github+json");

                if (alerts?.Body is null) continue;

                foreach (var alert in alerts.Body)
                {
                    var severity = MapSeverity(alert.SecurityAdvisory?.Severity ?? alert.SecurityVulnerability?.Severity ?? "medium");

                    if (filter?.Impact is not null && !string.Equals(severity, filter.Impact, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var packageName = alert.SecurityVulnerability?.Package?.Name ?? "unknown";
                    var ecosystem = alert.SecurityVulnerability?.Package?.Ecosystem ?? "unknown";

                    findings.Add(new CampaignFinding
                    {
                        SourceType = "dependabot",
                        Title = $"[Dependabot] {alert.SecurityAdvisory?.Summary ?? $"Vulnerable {packageName}"} in {repo.Name}",
                        Description = $"**Package**: {packageName} ({ecosystem})\n**Advisory**: {alert.SecurityAdvisory?.Summary ?? "N/A"}\n**Severity**: {severity}\n**CVE**: {alert.SecurityAdvisory?.CveId ?? "N/A"}\n**Fix**: Update to {alert.SecurityVulnerability?.FirstPatchedVersion?.Identifier ?? "latest version"}",
                        Severity = severity,
                        Repo = repo.Name,
                        ResourceId = $"dependabot:{repo.Name}:{alert.Number}",
                        DeduplicationKey = $"dependabot:{repo.Name}:{alert.Number}"
                    });
                }
            }
            catch (Octokit.NotFoundException)
            {
                // Dependabot not enabled for this repo
            }
            catch (Octokit.ForbiddenException)
            {
                // No permission to read Dependabot alerts
                _logger.LogWarning("No permission to read Dependabot alerts for {Repo}", repo.Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to scan Dependabot alerts for {Repo}", repo.Name);
            }
        }

        _logger.LogInformation("Dependabot scan found {Count} findings", findings.Count);
        return findings;
    }

    private static string MapSeverity(string severity) => severity.ToLowerInvariant() switch
    {
        "critical" => "High",
        "high" => "High",
        "medium" => "Medium",
        "low" => "Low",
        _ => "Medium"
    };

    // DTOs for Dependabot REST API response
    private class DependabotAlert
    {
        public int Number { get; set; }
        public string? State { get; set; }
        public SecurityAdvisoryInfo? SecurityAdvisory { get; set; }
        public SecurityVulnerabilityInfo? SecurityVulnerability { get; set; }
    }

    private class SecurityAdvisoryInfo
    {
        public string? Summary { get; set; }
        public string? Severity { get; set; }
        public string? CveId { get; set; }
    }

    private class SecurityVulnerabilityInfo
    {
        public string? Severity { get; set; }
        public PackageInfo? Package { get; set; }
        public PatchedVersion? FirstPatchedVersion { get; set; }
    }

    private class PackageInfo
    {
        public string? Name { get; set; }
        public string? Ecosystem { get; set; }
    }

    private class PatchedVersion
    {
        public string? Identifier { get; set; }
    }
}
