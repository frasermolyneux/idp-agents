using System.ComponentModel;

using Microsoft.SemanticKernel;

using MX.IDP.Agents.Services;

namespace MX.IDP.Agents.Tools;

public class KnowledgeTool
{
    private readonly IKnowledgeIndexService _indexService;

    public KnowledgeTool(IKnowledgeIndexService indexService)
    {
        _indexService = indexService;
    }

    [KernelFunction("search_knowledge_base")]
    [Description("Search the knowledge base for documentation, runbooks, ADRs, Terraform docs, and incident reports. Uses hybrid search (keyword + semantic).")]
    public async Task<string> SearchKnowledgeBaseAsync(
        [Description("Search query — describe what you're looking for")] string query,
        [Description("Optional: filter by source type (github_repo or blob_storage)")] string? sourceType = null,
        [Description("Optional: filter by source name (repository name or blob path)")] string? sourceName = null,
        [Description("Maximum results to return (default 5)")] int maxResults = 5)
    {
        return await _indexService.SearchAsync(query, sourceType, sourceName, maxResults);
    }

    [KernelFunction("list_knowledge_sources")]
    [Description("List all indexed knowledge sources showing what documentation is available in the knowledge base")]
    public async Task<string> ListKnowledgeSourcesAsync()
    {
        return await _indexService.ListSourcesAsync();
    }

    [KernelFunction("trigger_reindex")]
    [Description("Trigger a reindex of a knowledge source — deletes existing entries and marks for re-indexing on next scheduled run")]
    public async Task<string> TriggerReindexAsync(
        [Description("Source type to reindex: github_repo or blob_storage")] string sourceType,
        [Description("Source name: specific repository name, or 'all' to reindex all sources of that type")] string? sourceName = null)
    {
        await _indexService.DeleteSourceAsync(sourceType, sourceName ?? "all");
        return $"Reindex triggered for {sourceType}/{sourceName ?? "all"}. Existing entries deleted — documents will be re-indexed on next scheduled run or via manual trigger.";
    }
}
