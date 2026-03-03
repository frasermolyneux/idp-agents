using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

using MX.IDP.Agents.Services;

namespace MX.IDP.Agents.Functions;

/// <summary>
/// Semi-autonomous swarm scheduler. Runs all active campaigns daily,
/// queuing new findings for human approval rather than auto-creating issues.
/// </summary>
public class SwarmSchedulerFunction
{
    private readonly ICampaignService _campaignService;
    private readonly ICampaignOrchestrationService _orchestrationService;
    private readonly ILogger<SwarmSchedulerFunction> _logger;

    public SwarmSchedulerFunction(
        ICampaignService campaignService,
        ICampaignOrchestrationService orchestrationService,
        ILogger<SwarmSchedulerFunction> logger)
    {
        _campaignService = campaignService;
        _orchestrationService = orchestrationService;
        _logger = logger;
    }

    /// <summary>
    /// Runs daily at 3 AM UTC. Scans all active campaigns and queues findings for approval.
    /// </summary>
    [Function("SwarmScheduler")]
    public async Task RunSwarm([TimerTrigger("0 0 3 * * *")] TimerInfo timer)
    {
        _logger.LogInformation("Swarm scheduler starting daily campaign scan");

        // Get all active campaigns (not paused or completed)
        var allCampaigns = await _campaignService.ListAsync("system");
        var activeCampaigns = allCampaigns
            .Where(c => c.Status is "created" or "completed" or "failed")
            .ToList();

        _logger.LogInformation("Found {Count} active campaigns to process", activeCampaigns.Count);

        var results = new List<(string Name, int NewFindings, bool Success)>();

        foreach (var campaign in activeCampaigns)
        {
            try
            {
                _logger.LogInformation("Swarm: Running campaign '{Name}'", campaign.Name);
                var result = await _orchestrationService.RunCampaignAsync(campaign);

                var newFindings = result.Stats.TotalFindings;
                results.Add((campaign.Name, newFindings, true));

                _logger.LogInformation("Swarm: Campaign '{Name}' completed with {Findings} total findings",
                    campaign.Name, newFindings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Swarm: Campaign '{Name}' failed", campaign.Name);
                results.Add((campaign.Name, 0, false));
            }
        }

        var succeeded = results.Count(r => r.Success);
        var failed = results.Count(r => !r.Success);
        _logger.LogInformation(
            "Swarm scheduler completed. Campaigns: {Total} processed, {Succeeded} succeeded, {Failed} failed",
            results.Count, succeeded, failed);
    }
}
