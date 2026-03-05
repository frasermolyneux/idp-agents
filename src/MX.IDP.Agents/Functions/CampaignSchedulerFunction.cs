using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

using MX.IDP.Agents.Services;

using NCrontab;

namespace MX.IDP.Agents.Functions;

public class CampaignSchedulerFunction
{
    private readonly ICampaignService _campaignService;
    private readonly ICampaignOrchestrationService _orchestrationService;
    private readonly ILogger<CampaignSchedulerFunction> _logger;

    public CampaignSchedulerFunction(
        ICampaignService campaignService,
        ICampaignOrchestrationService orchestrationService,
        ILogger<CampaignSchedulerFunction> logger)
    {
        _campaignService = campaignService;
        _orchestrationService = orchestrationService;
        _logger = logger;
    }

    [Function("RunScheduledCampaigns")]
    public async Task RunScheduledCampaigns(
        [TimerTrigger("0 */15 * * * *")] TimerInfo timer)
    {
        _logger.LogInformation("Campaign scheduler triggered at {Time}", DateTimeOffset.UtcNow);

        var dueCampaigns = await _campaignService.GetScheduledCampaignsAsync();
        _logger.LogInformation("Found {Count} due campaigns", dueCampaigns.Count);

        foreach (var campaign in dueCampaigns)
        {
            try
            {
                _logger.LogInformation("Running scheduled campaign '{Name}' (ID: {Id})", campaign.Name, campaign.Id);

                await _orchestrationService.RunCampaignAsync(campaign);

                // Update schedule: record last run and compute next run
                campaign.Schedule!.LastScheduledRun = DateTimeOffset.UtcNow;
                campaign.Schedule.NextRun = ComputeNextRun(campaign.Schedule.CronExpression);
                await _campaignService.UpdateAsync(campaign);

                _logger.LogInformation("Scheduled campaign '{Name}' completed. Next run: {NextRun}",
                    campaign.Name, campaign.Schedule.NextRun);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to run scheduled campaign '{Name}' (ID: {Id})", campaign.Name, campaign.Id);
            }
        }
    }

    /// <summary>
    /// Computes the next run time from a cron expression using NCrontab.
    /// Supports standard 5-field cron expressions (minute hour day month weekday).
    /// </summary>
    internal static DateTimeOffset? ComputeNextRun(string cronExpression)
    {
        try
        {
            var schedule = CrontabSchedule.Parse(cronExpression);
            var next = schedule.GetNextOccurrence(DateTime.UtcNow);
            return new DateTimeOffset(next, TimeSpan.Zero);
        }
        catch
        {
            return null;
        }
    }
}
