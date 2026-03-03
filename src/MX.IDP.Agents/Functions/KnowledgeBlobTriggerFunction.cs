using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

using MX.IDP.Agents.Services;

namespace MX.IDP.Agents.Functions;

/// <summary>
/// Blob trigger for knowledge document indexing.
/// Separated into its own class so a blob connection failure doesn't prevent
/// other knowledge functions (HTTP reindex, timer) from registering.
/// </summary>
public class KnowledgeBlobTriggerFunction
{
    private readonly IKnowledgeIndexService _indexService;
    private readonly ILogger<KnowledgeBlobTriggerFunction> _logger;

    public KnowledgeBlobTriggerFunction(
        IKnowledgeIndexService indexService,
        ILogger<KnowledgeBlobTriggerFunction> logger)
    {
        _indexService = indexService;
        _logger = logger;
    }

    [Function("IndexBlobDocument")]
    public async Task IndexBlobDocument(
        [BlobTrigger("knowledge-docs/{blobpath}", Connection = "KnowledgeStorage")] string content,
        string blobpath)
    {
        var fileName = Path.GetFileName(blobpath);
        if (!KnowledgeIndexerFunctions.IsTextFile(fileName))
        {
            _logger.LogInformation("Skipping non-text file: {Path}", blobpath);
            return;
        }

        _logger.LogInformation("Indexing blob document: {Path}", blobpath);
        await _indexService.EnsureIndexExistsAsync();

        var folder = Path.GetDirectoryName(blobpath)?.Replace('\\', '/') ?? "knowledge-docs";
        if (string.IsNullOrEmpty(folder)) folder = "knowledge-docs";
        await _indexService.IndexDocumentAsync(content, fileName, "blob_storage", folder, blobpath);
        _logger.LogInformation("Successfully indexed blob document: {Path}", blobpath);
    }
}
