using System.ComponentModel;
using System.Text.Json;

using Microsoft.SemanticKernel;

using MX.IDP.Agents.Models;
using MX.IDP.Agents.Services;

namespace MX.IDP.Agents.Tools;

public class CampaignTool
{
    private readonly ICampaignService _campaignService;
    private readonly ICampaignOrchestrationService _orchestrationService;

    public CampaignTool(ICampaignService campaignService, ICampaignOrchestrationService orchestrationService)
    {
        _campaignService = campaignService;
        _orchestrationService = orchestrationService;
    }

    [KernelFunction("create_campaign")]
    [Description("Create a new campaign to systematically scan and remediate issues. Source types: advisor (Azure Advisor recommendations), policy (Azure Policy compliance), dev_standards (branch protection, required checks), repo_config (description, topics, default branch, license).")]
    public async Task<string> CreateCampaignAsync(
        [Description("Campaign name")] string name,
        [Description("Source type: advisor, policy, dev_standards, or repo_config")] string sourceType,
        [Description("Campaign description")] string? description = null,
        [Description("For advisor: filter by category (Cost, Security, Reliability, Performance)")] string? category = null,
        [Description("For advisor: filter by impact (High, Medium, Low)")] string? impact = null,
        [Description("Comma-separated repo names to scope the campaign to")] string? repos = null,
        [Description("Assignee for created issues (use 'copilot' for Copilot coding agent)")] string? assignTo = null)
    {
        var campaign = new Campaign
        {
            Name = name,
            Description = description ?? $"Campaign scanning {sourceType}",
            SourceType = sourceType,
            UserId = "system",
            Filter = new CampaignFilter
            {
                Category = category,
                Impact = impact,
                Repos = repos?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                AssignTo = assignTo
            }
        };

        var created = await _campaignService.CreateAsync(campaign);
        return JsonSerializer.Serialize(new { id = created.Id, name = created.Name, status = created.Status, sourceType = created.SourceType });
    }

    [KernelFunction("list_campaigns")]
    [Description("List all campaigns, optionally filtered by status (created, running, paused, completed, failed)")]
    public async Task<string> ListCampaignsAsync(
        [Description("Filter by status: created, running, paused, completed, failed")] string? status = null)
    {
        var campaigns = await _campaignService.ListAsync("system", status);
        var summary = campaigns.Select(c => new
        {
            c.Id, c.Name, c.SourceType, c.Status,
            c.Stats.TotalFindings, c.Stats.IssuesCreated, c.Stats.IssuesClosed,
            c.Stats.ProgressPercent, c.LastRunAt
        });
        return JsonSerializer.Serialize(new { count = campaigns.Count, campaigns = summary },
            new JsonSerializerOptions { WriteIndented = true });
    }

    [KernelFunction("run_campaign")]
    [Description("Run/trigger a campaign by ID. Scans the data source, deduplicates findings, creates GitHub issues, and tracks progress.")]
    public async Task<string> RunCampaignAsync(
        [Description("Campaign ID to run")] string campaignId)
    {
        var campaign = await _campaignService.GetAsync(campaignId, "system");
        if (campaign is null) return "Campaign not found";

        var result = await _orchestrationService.RunCampaignAsync(campaign);
        return JsonSerializer.Serialize(new
        {
            result.Id, result.Name, result.Status,
            result.Stats.TotalFindings, result.Stats.IssuesCreated, result.Stats.IssuesOpen,
            result.Stats.IssuesClosed, result.Stats.IssuesSkipped, result.Stats.ProgressPercent
        });
    }

    [KernelFunction("get_campaign_findings")]
    [Description("Get findings for a campaign, optionally filtered by status (new, issue_created, resolved, skipped, duplicate)")]
    public async Task<string> GetCampaignFindingsAsync(
        [Description("Campaign ID")] string campaignId,
        [Description("Filter by status")] string? status = null)
    {
        var findings = await _campaignService.GetFindingsAsync(campaignId, status);
        var summary = findings.Select(f => new
        {
            f.Title, f.Severity, f.Repo, f.Status, f.IssueNumber, f.IssueUrl
        });
        return JsonSerializer.Serialize(new { count = findings.Count, findings = summary },
            new JsonSerializerOptions { WriteIndented = true });
    }
}
