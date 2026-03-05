using System.Text.Json;
using System.Text.Json.Serialization;

namespace MX.IDP.Agents.Models;

public class CampaignTemplate
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty; // security, cost, compliance, hygiene

    [JsonPropertyName("sourceType")]
    public string SourceType { get; set; } = string.Empty;

    [JsonPropertyName("icon")]
    public string Icon { get; set; } = "📋";

    [JsonPropertyName("actionMode")]
    public string ActionMode { get; set; } = "issue"; // audit, issue, copilot_agent

    [JsonPropertyName("requireApproval")]
    public bool RequireApproval { get; set; }

    [JsonPropertyName("defaultFilter")]
    public CampaignFilter? DefaultFilter { get; set; }

    [JsonPropertyName("kqlQuery")]
    public string? KqlQuery { get; set; }
}

public static class CampaignTemplateLibrary
{
    private static readonly List<CampaignTemplate> Templates =
    [
        new()
        {
            Id = "security-hardening",
            Name = "Security Hardening",
            Description = "Scan for critical and high severity CodeQL findings across all repositories. Creates issues for code vulnerabilities detected by static analysis.",
            Category = "security",
            SourceType = "codeql",
            Icon = "🔒",
            DefaultFilter = new CampaignFilter { Impact = "High" }
        },
        new()
        {
            Id = "dependency-updates",
            Name = "Dependency Updates",
            Description = "Scan for Dependabot security alerts across all repositories. Creates issues for vulnerable dependencies that need updating.",
            Category = "security",
            SourceType = "dependabot",
            Icon = "📦"
        },
        new()
        {
            Id = "cost-optimisation",
            Name = "Cost Optimisation",
            Description = "Review Azure Advisor cost recommendations. Creates issues for cost-saving opportunities like right-sizing VMs, reserved instances, and unused resources.",
            Category = "cost",
            SourceType = "advisor",
            Icon = "💰",
            DefaultFilter = new CampaignFilter { Category = "Cost" }
        },
        new()
        {
            Id = "branch-protection",
            Name = "Branch Protection Compliance",
            Description = "Check all repositories have proper branch protection rules on their default branch, including required status checks and PR reviews.",
            Category = "compliance",
            SourceType = "dev_standards",
            Icon = "🛡️"
        },
        new()
        {
            Id = "repo-hygiene",
            Name = "Repository Hygiene",
            Description = "Check all repositories have descriptions, topics, proper default branch naming, and license files. Ensures consistent repository configuration.",
            Category = "hygiene",
            SourceType = "repo_config",
            Icon = "🧹"
        },
        new()
        {
            Id = "policy-compliance",
            Name = "Policy Compliance",
            Description = "Scan for non-compliant Azure Policy resources across all subscriptions. Creates issues for resources that violate organisational policies.",
            Category = "compliance",
            SourceType = "policy",
            Icon = "📜"
        },
        new()
        {
            Id = "vm-rightsizing",
            Name = "VM Right-Sizing",
            Description = "Find virtual machines that may be oversized or underutilised using Azure Resource Graph. Reviews VM SKUs and recommends right-sizing opportunities.",
            Category = "cost",
            SourceType = "kql",
            Icon = "📊",
            KqlQuery = "resources | where type == 'microsoft.compute/virtualmachines' | extend vmSize = properties.hardwareProfile.vmSize | project name, type, resourceGroup, subscriptionId, vmSize"
        },
        new()
        {
            Id = "orphaned-resources",
            Name = "Orphaned Resources",
            Description = "Find orphaned Azure resources — unattached managed disks, unused network interfaces, and empty resource groups that may be incurring unnecessary costs.",
            Category = "cost",
            SourceType = "kql",
            Icon = "🗑️",
            KqlQuery = "resources | where type == 'microsoft.compute/disks' | where properties.diskState == 'Unattached' | project name, type, resourceGroup, subscriptionId, severity='Medium', description=strcat('Unattached disk: ', name, ' in ', resourceGroup)"
        },
        new()
        {
            Id = "service-retirement",
            Name = "Service Retirement & Deprecation",
            Description = "Identify Azure services and features scheduled for retirement or deprecation. Creates tracking issues with retirement dates and migration guidance so you can plan ahead.",
            Category = "compliance",
            SourceType = "advisor",
            Icon = "⏳",
            DefaultFilter = new CampaignFilter { Category = "Reliability", Subcategory = "ServiceUpgradeAndRetirement" }
        }
    ];

    public static IReadOnlyList<CampaignTemplate> GetAll() => Templates;

    public static CampaignTemplate? GetById(string id) =>
        Templates.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<CampaignTemplate> GetByCategory(string category) =>
        Templates.Where(t => string.Equals(t.Category, category, StringComparison.OrdinalIgnoreCase)).ToList();

    public static Campaign CreateFromTemplate(CampaignTemplate template, string? name = null, string userId = "system")
    {
        return new Campaign
        {
            Name = name ?? template.Name,
            Description = template.Description,
            SourceType = template.SourceType,
            ActionMode = template.ActionMode,
            RequireApproval = template.RequireApproval,
            KqlQuery = template.KqlQuery,
            Filter = template.DefaultFilter,
            UserId = userId
        };
    }
}
