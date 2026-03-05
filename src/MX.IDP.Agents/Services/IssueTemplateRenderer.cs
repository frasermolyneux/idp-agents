using MX.IDP.Agents.Models;

namespace MX.IDP.Agents.Services;

public interface IIssueTemplateRenderer
{
    string RenderTitle(CampaignIssueTemplate template, CampaignFinding finding, Campaign campaign);
    string RenderBody(CampaignIssueTemplate template, CampaignFinding finding, Campaign campaign);
    List<string> GetLabels(CampaignIssueTemplate template, Campaign campaign);
    List<string> GetAssignees(CampaignIssueTemplate template, Campaign campaign);
}

public class IssueTemplateRenderer : IIssueTemplateRenderer
{
    public string RenderTitle(CampaignIssueTemplate template, CampaignFinding finding, Campaign campaign)
    {
        return ReplacePlaceholders(template.TitlePattern, finding, campaign);
    }

    public string RenderBody(CampaignIssueTemplate template, CampaignFinding finding, Campaign campaign)
    {
        return ReplacePlaceholders(template.BodyTemplate, finding, campaign);
    }

    public List<string> GetLabels(CampaignIssueTemplate template, Campaign campaign)
    {
        var labels = new List<string>();

        // Default campaign labels
        labels.Add($"campaign:{campaign.SourceType}");

        // Template-specific labels
        if (template.Labels is not null)
            labels.AddRange(template.Labels);

        return labels.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public List<string> GetAssignees(CampaignIssueTemplate template, Campaign campaign)
    {
        var assignees = new List<string>();

        // Copilot agent mode
        if (campaign.ActionMode == "copilot_agent")
            assignees.Add("copilot");

        // Campaign-level assignTo (legacy)
        if (campaign.Filter?.AssignTo is not null)
            assignees.Add(campaign.Filter.AssignTo);

        // Template assignees
        if (template.Assignees is not null)
            assignees.AddRange(template.Assignees);

        return assignees.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string ReplacePlaceholders(string template, CampaignFinding finding, Campaign campaign)
    {
        return template
            .Replace("{title}", finding.Title)
            .Replace("{description}", finding.Description)
            .Replace("{severity}", finding.Severity)
            .Replace("{resourceId}", finding.ResourceId ?? "N/A")
            .Replace("{repo}", finding.Repo ?? "N/A")
            .Replace("{sourceType}", finding.SourceType)
            .Replace("{campaignName}", campaign.Name)
            .Replace("{campaignId}", campaign.Id);
    }
}
