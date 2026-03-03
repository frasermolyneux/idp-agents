using MX.IDP.Agents.Models;

namespace MX.IDP.Agents.Services.CampaignSources;

public interface ICampaignDataSource
{
    string SourceType { get; }
    Task<List<CampaignFinding>> ScanAsync(CampaignFilter? filter);
}
