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
    [Description("Create a new campaign to systematically scan and remediate issues. Source types: advisor, policy, dev_standards, repo_config, dependabot, codeql, kql. Action modes: audit (log only), issue (create GitHub issues), copilot_agent (create issues and assign to Copilot).")]
    public async Task<string> CreateCampaignAsync(
        [Description("Campaign name")] string name,
        [Description("Source type: advisor, policy, dev_standards, repo_config, dependabot, codeql, or kql")] string sourceType,
        [Description("Campaign description")] string? description = null,
        [Description("For advisor: filter by category (Cost, Security, Reliability, Performance)")] string? category = null,
        [Description("For advisor/dependabot/codeql: filter by impact (High, Medium, Low)")] string? impact = null,
        [Description("Comma-separated repo names to scope the campaign to")] string? repos = null,
        [Description("Assignee for created issues (use 'copilot' for Copilot coding agent)")] string? assignTo = null,
        [Description("For kql source type: the ARG KQL query to execute")] string? kqlQuery = null,
        [Description("Action mode: audit (findings only), issue (create GitHub issues), copilot_agent (create and assign to Copilot). Default: issue")] string? actionMode = null,
        [Description("Require approval before creating issues. Default: false")] bool requireApproval = false,
        [Description("Cron expression for scheduled runs (e.g., '0 8 * * 1' for weekly Monday 8am). Leave empty for manual-only.")] string? cronSchedule = null,
        [Description("Comma-separated repo topics to dynamically resolve target repos")] string? repoTopics = null,
        [Description("Comma-separated repo names to exclude")] string? excludeRepos = null,
        [Description("Comma-separated Azure resource group names to scope to")] string? resourceGroups = null,
        [Description("Cross-source severity filter: High, Medium, Low")] string? severity = null)
    {
        var campaign = new Campaign
        {
            Name = name,
            Description = description ?? $"Campaign scanning {sourceType}",
            SourceType = sourceType,
            KqlQuery = kqlQuery,
            UserId = "system",
            ActionMode = actionMode ?? "issue",
            RequireApproval = requireApproval,
            Filter = new CampaignFilter
            {
                Category = category,
                Impact = impact,
                Repos = repos?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                AssignTo = assignTo,
                RepoTopics = repoTopics?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                ExcludeRepos = excludeRepos?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                ResourceGroups = resourceGroups?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                Severity = severity
            }
        };

        if (!string.IsNullOrEmpty(cronSchedule))
        {
            campaign.Schedule = new CampaignSchedule
            {
                CronExpression = cronSchedule,
                Enabled = true,
                NextRun = Functions.CampaignSchedulerFunction.ComputeNextRun(cronSchedule)
            };
        }

        var created = await _campaignService.CreateAsync(campaign);
        return JsonSerializer.Serialize(new { id = created.Id, name = created.Name, status = created.Status, sourceType = created.SourceType, actionMode = created.ActionMode, requireApproval = created.RequireApproval, scheduled = campaign.Schedule?.Enabled ?? false });
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
    [Description("Get findings for a campaign, optionally filtered by status (new, pending_approval, issue_created, resolved, skipped, duplicate, dismissed)")]
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

    [KernelFunction("list_campaign_templates")]
    [Description("List available campaign templates — pre-built campaigns for common scenarios like security hardening, cost optimisation, and compliance.")]
    public Task<string> ListCampaignTemplatesAsync()
    {
        var templates = CampaignTemplateLibrary.GetAll().Select(t => new
        {
            t.Id, t.Name, t.Description, t.Category, t.SourceType, t.Icon
        });
        return Task.FromResult(JsonSerializer.Serialize(new { count = CampaignTemplateLibrary.GetAll().Count, templates },
            new JsonSerializerOptions { WriteIndented = true }));
    }

    [KernelFunction("create_campaign_from_template")]
    [Description("Create a campaign from a pre-built template by template ID. Use list_campaign_templates to see available templates.")]
    public async Task<string> CreateCampaignFromTemplateAsync(
        [Description("Template ID (e.g., security-hardening, dependency-updates, cost-optimisation)")] string templateId,
        [Description("Optional custom name for the campaign")] string? name = null,
        [Description("Assignee for created issues (use 'copilot' for Copilot coding agent)")] string? assignTo = null,
        [Description("Action mode override: audit, issue, copilot_agent")] string? actionMode = null,
        [Description("Require approval before creating issues")] bool? requireApproval = null,
        [Description("Cron expression for scheduled runs (e.g., '0 8 * * 1' for weekly Monday 8am)")] string? cronSchedule = null,
        [Description("Comma-separated repo names to scope the campaign to")] string? repos = null,
        [Description("Comma-separated repo topics to dynamically resolve target repos")] string? repoTopics = null,
        [Description("Comma-separated repo names to exclude")] string? excludeRepos = null)
    {
        var template = CampaignTemplateLibrary.GetById(templateId);
        if (template is null) return $"Template '{templateId}' not found. Use list_campaign_templates to see available templates.";

        var campaign = CampaignTemplateLibrary.CreateFromTemplate(template, name);

        if (actionMode is not null) campaign.ActionMode = actionMode;
        if (requireApproval.HasValue) campaign.RequireApproval = requireApproval.Value;

        campaign.Filter ??= new CampaignFilter();
        if (assignTo is not null) campaign.Filter.AssignTo = assignTo;
        if (repos is not null) campaign.Filter.Repos = repos.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (repoTopics is not null) campaign.Filter.RepoTopics = repoTopics.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (excludeRepos is not null) campaign.Filter.ExcludeRepos = excludeRepos.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        if (!string.IsNullOrEmpty(cronSchedule))
        {
            campaign.Schedule = new CampaignSchedule
            {
                CronExpression = cronSchedule,
                Enabled = true,
                NextRun = Functions.CampaignSchedulerFunction.ComputeNextRun(cronSchedule)
            };
        }

        var created = await _campaignService.CreateAsync(campaign);
        return JsonSerializer.Serialize(new { id = created.Id, name = created.Name, status = created.Status, sourceType = created.SourceType, template = templateId, actionMode = created.ActionMode });
    }

    [KernelFunction("preview_campaign")]
    [Description("Dry-run a campaign — scans for findings without creating GitHub issues. Use to preview what a campaign would do before running it for real.")]
    public async Task<string> PreviewCampaignAsync(
        [Description("Campaign ID to preview")] string campaignId)
    {
        var campaign = await _campaignService.GetAsync(campaignId, "system");
        if (campaign is null) return "Campaign not found";

        var result = await _orchestrationService.RunCampaignAsync(campaign, dryRun: true);
        var findings = await _campaignService.GetFindingsAsync(campaignId, "preview");
        var summary = findings.Select(f => new { f.Title, f.Severity, f.Repo });
        return JsonSerializer.Serialize(new
        {
            result.Id, result.Name, result.Status, previewFindings = findings.Count,
            findings = summary
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    [KernelFunction("pause_campaign")]
    [Description("Pause a running campaign. It can be resumed later.")]
    public async Task<string> PauseCampaignAsync(
        [Description("Campaign ID to pause")] string campaignId)
    {
        var campaign = await _campaignService.GetAsync(campaignId, "system");
        if (campaign is null) return "Campaign not found";
        if (campaign.Status is not ("running" or "created"))
            return $"Campaign cannot be paused (status: {campaign.Status})";

        campaign.Status = "paused";
        await _campaignService.UpdateAsync(campaign);
        return JsonSerializer.Serialize(new { campaign.Id, campaign.Name, campaign.Status });
    }

    [KernelFunction("resume_campaign")]
    [Description("Resume a paused campaign. Continues scanning and issue creation.")]
    public async Task<string> ResumeCampaignAsync(
        [Description("Campaign ID to resume")] string campaignId)
    {
        var campaign = await _campaignService.GetAsync(campaignId, "system");
        if (campaign is null) return "Campaign not found";
        if (campaign.Status != "paused")
            return $"Campaign is not paused (status: {campaign.Status})";

        var result = await _orchestrationService.RunCampaignAsync(campaign);
        return JsonSerializer.Serialize(new
        {
            result.Id, result.Name, result.Status,
            result.Stats.TotalFindings, result.Stats.IssuesCreated, result.Stats.ProgressPercent
        });
    }

    [KernelFunction("cancel_campaign")]
    [Description("Cancel a campaign. Cannot be resumed after cancellation.")]
    public async Task<string> CancelCampaignAsync(
        [Description("Campaign ID to cancel")] string campaignId)
    {
        var campaign = await _campaignService.GetAsync(campaignId, "system");
        if (campaign is null) return "Campaign not found";
        if (campaign.Status is not ("running" or "paused" or "created"))
            return $"Campaign cannot be cancelled (status: {campaign.Status})";

        campaign.Status = "cancelled";
        await _campaignService.UpdateAsync(campaign);
        return JsonSerializer.Serialize(new { campaign.Id, campaign.Name, campaign.Status });
    }
}
