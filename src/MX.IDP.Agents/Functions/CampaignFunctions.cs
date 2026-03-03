using System.Text.Json;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

using MX.IDP.Agents.Models;
using MX.IDP.Agents.Services;

namespace MX.IDP.Agents.Functions;

public class CampaignFunctions
{
    private readonly ICampaignService _campaignService;
    private readonly ICampaignOrchestrationService _orchestrationService;
    private readonly ILogger<CampaignFunctions> _logger;

    public CampaignFunctions(
        ICampaignService campaignService,
        ICampaignOrchestrationService orchestrationService,
        ILogger<CampaignFunctions> logger)
    {
        _campaignService = campaignService;
        _orchestrationService = orchestrationService;
        _logger = logger;
    }

    [Function("CreateCampaign")]
    public async Task<IActionResult> CreateCampaign(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "campaigns")] HttpRequest req)
    {
        var body = await new StreamReader(req.Body).ReadToEndAsync();
        var campaign = JsonSerializer.Deserialize<Campaign>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (campaign is null) return new BadRequestObjectResult("Invalid campaign payload");

        // Use system user ID for now — in production, extract from auth token
        if (string.IsNullOrEmpty(campaign.UserId)) campaign.UserId = "system";

        var created = await _campaignService.CreateAsync(campaign);
        _logger.LogInformation("Created campaign {CampaignId}: {Name}", created.Id, created.Name);
        return new OkObjectResult(created);
    }

    [Function("ListCampaigns")]
    public async Task<IActionResult> ListCampaigns(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "campaigns")] HttpRequest req)
    {
        var userId = req.Query["userId"].FirstOrDefault() ?? "system";
        var status = req.Query["status"].FirstOrDefault();
        var campaigns = await _campaignService.ListAsync(userId, status);
        return new OkObjectResult(campaigns);
    }

    [Function("GetCampaign")]
    public async Task<IActionResult> GetCampaign(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "campaigns/{campaignId}")] HttpRequest req,
        string campaignId)
    {
        // Prevent route conflict with /campaigns/templates
        if (string.Equals(campaignId, "templates", StringComparison.OrdinalIgnoreCase))
            return ListTemplates(req);

        var userId = req.Query["userId"].FirstOrDefault() ?? "system";
        var campaign = await _campaignService.GetAsync(campaignId, userId);
        if (campaign is null) return new NotFoundResult();
        return new OkObjectResult(campaign);
    }

    [Function("GetCampaignFindings")]
    public async Task<IActionResult> GetCampaignFindings(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "campaigns/{campaignId}/findings")] HttpRequest req,
        string campaignId)
    {
        var status = req.Query["status"].FirstOrDefault();
        var findings = await _campaignService.GetFindingsAsync(campaignId, status);
        return new OkObjectResult(findings);
    }

    [Function("TriggerCampaign")]
    public async Task<IActionResult> TriggerCampaign(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "campaigns/{campaignId}/run")] HttpRequest req,
        string campaignId)
    {
        var userId = req.Query["userId"].FirstOrDefault() ?? "system";
        var dryRun = req.Query["dryRun"].FirstOrDefault()?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false;
        var campaign = await _campaignService.GetAsync(campaignId, userId);
        if (campaign is null) return new NotFoundResult();

        if (campaign.Status == "running")
            return new ConflictObjectResult("Campaign is already running");

        var result = await _orchestrationService.RunCampaignAsync(campaign, dryRun);
        return new OkObjectResult(result);
    }

    [Function("PauseCampaign")]
    public async Task<IActionResult> PauseCampaign(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "campaigns/{campaignId}/pause")] HttpRequest req,
        string campaignId)
    {
        var userId = req.Query["userId"].FirstOrDefault() ?? "system";
        var campaign = await _campaignService.GetAsync(campaignId, userId);
        if (campaign is null) return new NotFoundResult();

        campaign.Status = "paused";
        await _campaignService.UpdateAsync(campaign);
        return new OkObjectResult(campaign);
    }

    [Function("ResumeCampaign")]
    public async Task<IActionResult> ResumeCampaign(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "campaigns/{campaignId}/resume")] HttpRequest req,
        string campaignId)
    {
        var userId = req.Query["userId"].FirstOrDefault() ?? "system";
        var campaign = await _campaignService.GetAsync(campaignId, userId);
        if (campaign is null) return new NotFoundResult();

        if (campaign.Status != "paused")
            return new ConflictObjectResult("Campaign is not paused");

        var result = await _orchestrationService.RunCampaignAsync(campaign);
        return new OkObjectResult(result);
    }

    [Function("CancelCampaign")]
    public async Task<IActionResult> CancelCampaign(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "campaigns/{campaignId}/cancel")] HttpRequest req,
        string campaignId)
    {
        var userId = req.Query["userId"].FirstOrDefault() ?? "system";
        var campaign = await _campaignService.GetAsync(campaignId, userId);
        if (campaign is null) return new NotFoundResult();

        if (campaign.Status is not ("running" or "paused" or "created"))
            return new ConflictObjectResult($"Campaign cannot be cancelled (status: {campaign.Status})");

        campaign.Status = "cancelled";
        await _campaignService.UpdateAsync(campaign);
        _logger.LogInformation("Cancelled campaign {CampaignId}", campaignId);
        return new OkObjectResult(campaign);
    }

    [Function("DeleteCampaign")]
    public async Task<IActionResult> DeleteCampaign(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "campaigns/{campaignId}")] HttpRequest req,
        string campaignId)
    {
        var userId = req.Query["userId"].FirstOrDefault() ?? "system";
        await _campaignService.DeleteAsync(campaignId, userId);
        return new OkResult();
    }

    [Function("ApproveFinding")]
    public async Task<IActionResult> ApproveFinding(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "campaigns/{campaignId}/findings/{findingId}/approve")] HttpRequest req,
        string campaignId, string findingId)
    {
        var finding = await _campaignService.GetFindingAsync(findingId, campaignId);
        if (finding is null) return new NotFoundResult();

        if (finding.Status != "pending_approval")
            return new ConflictObjectResult($"Finding is not pending approval (status: {finding.Status})");

        // Create the GitHub issue now
        finding.Status = "issue_created";
        await _campaignService.UpsertFindingAsync(finding);
        _logger.LogInformation("Approved finding {FindingId} in campaign {CampaignId}", findingId, campaignId);
        return new OkObjectResult(finding);
    }

    [Function("RejectFinding")]
    public async Task<IActionResult> RejectFinding(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "campaigns/{campaignId}/findings/{findingId}/reject")] HttpRequest req,
        string campaignId, string findingId)
    {
        var finding = await _campaignService.GetFindingAsync(findingId, campaignId);
        if (finding is null) return new NotFoundResult();

        finding.Status = "dismissed";
        await _campaignService.UpsertFindingAsync(finding);
        _logger.LogInformation("Rejected finding {FindingId} in campaign {CampaignId}", findingId, campaignId);
        return new OkObjectResult(finding);
    }

    [Function("BulkApproveFindingsForCampaign")]
    public async Task<IActionResult> BulkApproveFindings(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "campaigns/{campaignId}/findings/approve-all")] HttpRequest req,
        string campaignId)
    {
        var findings = await _campaignService.GetFindingsAsync(campaignId, "pending_approval");
        var approved = 0;
        foreach (var finding in findings)
        {
            finding.Status = "issue_created";
            await _campaignService.UpsertFindingAsync(finding);
            approved++;
        }
        _logger.LogInformation("Bulk approved {Count} findings in campaign {CampaignId}", approved, campaignId);
        return new OkObjectResult(new { approved });
    }

    [Function("BulkRejectFindingsForCampaign")]
    public async Task<IActionResult> BulkRejectFindings(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "campaigns/{campaignId}/findings/reject-all")] HttpRequest req,
        string campaignId)
    {
        var findings = await _campaignService.GetFindingsAsync(campaignId, "pending_approval");
        var rejected = 0;
        foreach (var finding in findings)
        {
            finding.Status = "dismissed";
            await _campaignService.UpsertFindingAsync(finding);
            rejected++;
        }
        _logger.LogInformation("Bulk rejected {Count} findings in campaign {CampaignId}", rejected, campaignId);
        return new OkObjectResult(new { rejected });
    }

    [Function("ListCampaignTemplates")]
    public IActionResult ListTemplates(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "campaigns/templates")] HttpRequest req)
    {
        var templates = CampaignTemplateLibrary.GetAll();
        return new OkObjectResult(templates);
    }
}
