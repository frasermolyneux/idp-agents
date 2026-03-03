using Newtonsoft.Json;

namespace MX.IDP.Agents.Models;

public class Campaign
{
    [JsonProperty("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonProperty("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;

    [JsonProperty("sourceType")]
    public string SourceType { get; set; } = string.Empty; // advisor, policy, dev_standards, repo_config, dependabot, codeql, kql

    [JsonProperty("status")]
    public string Status { get; set; } = "created"; // created, running, paused, completed, failed

    [JsonProperty("filter")]
    public CampaignFilter? Filter { get; set; }

    [JsonProperty("kqlQuery")]
    public string? KqlQuery { get; set; } // Custom KQL query for kql source type

    [JsonProperty("stats")]
    public CampaignStats Stats { get; set; } = new();

    [JsonProperty("createdAt")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonProperty("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonProperty("lastRunAt")]
    public DateTimeOffset? LastRunAt { get; set; }
}

public class CampaignFilter
{
    [JsonProperty("category")]
    public string? Category { get; set; } // For advisor: Cost, Security, etc.

    [JsonProperty("impact")]
    public string? Impact { get; set; } // High, Medium, Low

    [JsonProperty("subcategory")]
    public string? Subcategory { get; set; } // For advisor: e.g. ServiceUpgradeAndRetirement

    [JsonProperty("subscriptionIds")]
    public List<string>? SubscriptionIds { get; set; }

    [JsonProperty("repos")]
    public List<string>? Repos { get; set; }

    [JsonProperty("assignTo")]
    public string? AssignTo { get; set; } // e.g., "copilot"
}

public class CampaignStats
{
    [JsonProperty("totalFindings")]
    public int TotalFindings { get; set; }

    [JsonProperty("issuesCreated")]
    public int IssuesCreated { get; set; }

    [JsonProperty("issuesOpen")]
    public int IssuesOpen { get; set; }

    [JsonProperty("issuesClosed")]
    public int IssuesClosed { get; set; }

    [JsonProperty("issuesSkipped")]
    public int IssuesSkipped { get; set; }

    [JsonProperty("progressPercent")]
    public double ProgressPercent { get; set; }
}

public class CampaignFinding
{
    [JsonProperty("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonProperty("campaignId")]
    public string CampaignId { get; set; } = string.Empty;

    [JsonProperty("sourceType")]
    public string SourceType { get; set; } = string.Empty;

    [JsonProperty("title")]
    public string Title { get; set; } = string.Empty;

    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;

    [JsonProperty("severity")]
    public string Severity { get; set; } = "Medium"; // High, Medium, Low

    [JsonProperty("resourceId")]
    public string? ResourceId { get; set; }

    [JsonProperty("repo")]
    public string? Repo { get; set; }

    [JsonProperty("issueNumber")]
    public int? IssueNumber { get; set; }

    [JsonProperty("issueUrl")]
    public string? IssueUrl { get; set; }

    [JsonProperty("status")]
    public string Status { get; set; } = "new"; // new, pending_approval, issue_created, resolved, skipped, duplicate, dismissed, stale

    [JsonProperty("deduplicationKey")]
    public string DeduplicationKey { get; set; } = string.Empty;

    [JsonProperty("createdAt")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
