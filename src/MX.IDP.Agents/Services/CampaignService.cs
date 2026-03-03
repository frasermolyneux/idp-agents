using System.Text.Json;

using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using MX.IDP.Agents.Models;

namespace MX.IDP.Agents.Services;

public interface ICampaignService
{
    Task<Campaign> CreateAsync(Campaign campaign);
    Task<Campaign?> GetAsync(string campaignId, string userId);
    Task<List<Campaign>> ListAsync(string userId, string? status = null);
    Task<Campaign> UpdateAsync(Campaign campaign);
    Task DeleteAsync(string campaignId, string userId);
    Task<List<CampaignFinding>> GetFindingsAsync(string campaignId, string? status = null);
    Task UpsertFindingAsync(CampaignFinding finding);
    Task UpsertFindingsBatchAsync(IEnumerable<CampaignFinding> findings);
}

public class CampaignService : ICampaignService
{
    private readonly Container _campaignsContainer;
    private readonly Container _findingsContainer;
    private readonly ILogger<CampaignService> _logger;

    public CampaignService(CosmosClient cosmosClient, IConfiguration configuration, ILogger<CampaignService> logger)
    {
        _logger = logger;
        var databaseName = configuration["CosmosDb:DatabaseName"] ?? "idp";
        _campaignsContainer = cosmosClient.GetContainer(databaseName, "campaigns");
        _findingsContainer = cosmosClient.GetContainer(databaseName, "campaign-findings");
    }

    public async Task<Campaign> CreateAsync(Campaign campaign)
    {
        campaign.CreatedAt = DateTimeOffset.UtcNow;
        campaign.UpdatedAt = DateTimeOffset.UtcNow;
        var response = await _campaignsContainer.CreateItemAsync(campaign, new PartitionKey(campaign.UserId));
        _logger.LogInformation("Created campaign {CampaignId} for user {UserId}", campaign.Id, campaign.UserId);
        return response.Resource;
    }

    public async Task<Campaign?> GetAsync(string campaignId, string userId)
    {
        try
        {
            var response = await _campaignsContainer.ReadItemAsync<Campaign>(campaignId, new PartitionKey(userId));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<List<Campaign>> ListAsync(string userId, string? status = null)
    {
        var queryable = _campaignsContainer.GetItemLinqQueryable<Campaign>()
            .Where(c => c.UserId == userId);

        if (status is not null)
            queryable = queryable.Where(c => c.Status == status);

        var campaigns = new List<Campaign>();
        using var iterator = queryable.OrderByDescending(c => c.CreatedAt).ToFeedIterator();
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            campaigns.AddRange(response);
        }
        return campaigns;
    }

    public async Task<Campaign> UpdateAsync(Campaign campaign)
    {
        campaign.UpdatedAt = DateTimeOffset.UtcNow;
        var response = await _campaignsContainer.ReplaceItemAsync(campaign, campaign.Id, new PartitionKey(campaign.UserId));
        return response.Resource;
    }

    public async Task DeleteAsync(string campaignId, string userId)
    {
        await _campaignsContainer.DeleteItemAsync<Campaign>(campaignId, new PartitionKey(userId));
        _logger.LogInformation("Deleted campaign {CampaignId}", campaignId);
    }

    public async Task<List<CampaignFinding>> GetFindingsAsync(string campaignId, string? status = null)
    {
        var queryable = _findingsContainer.GetItemLinqQueryable<CampaignFinding>()
            .Where(f => f.CampaignId == campaignId);

        if (status is not null)
            queryable = queryable.Where(f => f.Status == status);

        var findings = new List<CampaignFinding>();
        using var iterator = queryable.ToFeedIterator();
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            findings.AddRange(response);
        }
        return findings;
    }

    public async Task UpsertFindingAsync(CampaignFinding finding)
    {
        await _findingsContainer.UpsertItemAsync(finding, new PartitionKey(finding.CampaignId));
    }

    public async Task UpsertFindingsBatchAsync(IEnumerable<CampaignFinding> findings)
    {
        foreach (var finding in findings)
        {
            await UpsertFindingAsync(finding);
        }
    }
}
