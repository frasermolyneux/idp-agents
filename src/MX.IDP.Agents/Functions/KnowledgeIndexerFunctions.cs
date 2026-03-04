using System.Text;

using Azure.Storage.Blobs;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

using MX.IDP.Agents.Services;

namespace MX.IDP.Agents.Functions;

/// <summary>
/// HTTP trigger for manual reindex and timer trigger for scheduled GitHub docs indexing.
/// Separated from blob trigger to avoid startup failures cascading.
/// </summary>
public class KnowledgeIndexerFunctions
{
    private static readonly string[] TextExtensions = [".md", ".txt", ".json", ".yaml", ".yml"];

    private readonly IKnowledgeIndexService _indexService;
    private readonly IGitHubClientFactory _gitHubClientFactory;
    private readonly BlobServiceClient? _blobServiceClient;
    private readonly ILogger<KnowledgeIndexerFunctions> _logger;

    public KnowledgeIndexerFunctions(
        IKnowledgeIndexService indexService,
        IGitHubClientFactory gitHubClientFactory,
        ILogger<KnowledgeIndexerFunctions> logger,
        BlobServiceClient? blobServiceClient = null)
    {
        _indexService = indexService;
        _gitHubClientFactory = gitHubClientFactory;
        _blobServiceClient = blobServiceClient;
        _logger = logger;
    }

    /// <summary>
    /// Daily timer trigger — indexes docs/ folders from all GitHub repos.
    /// Runs at 2 AM UTC every day.
    /// </summary>
    [Function("IndexGitHubDocs")]
    public async Task IndexGitHubDocs(
        [TimerTrigger("0 0 2 * * *")] TimerInfo timer)
    {
        _logger.LogInformation("Starting GitHub docs indexing run");
        await _indexService.EnsureIndexExistsAsync();

        var client = await _gitHubClientFactory.CreateClientAsync();
        var repos = await client.GitHubApps.Installation.GetAllRepositoriesForCurrent();
        var indexedCount = 0;

        foreach (var repo in repos.Repositories)
        {
            try
            {
                indexedCount += await IndexGitHubDirectoryAsync(client, repo.Owner.Login, repo.Name, "docs", repo.DefaultBranch);
            }
            catch (Octokit.NotFoundException)
            {
                // No docs/ folder in this repo — skip
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to index docs for repo {Repo}", repo.Name);
            }
        }

        _logger.LogInformation("GitHub docs indexing complete. Indexed {Count} files across {RepoCount} repos",
            indexedCount, repos.Repositories.Count);
    }

    /// <summary>
    /// HTTP trigger — get knowledge index statistics.
    /// GET /api/knowledge/stats
    /// </summary>
    [Function("GetKnowledgeStats")]
    public async Task<IActionResult> GetKnowledgeStats(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "knowledge/stats")] HttpRequest req)
    {
        _logger.LogInformation("Knowledge stats requested");
        var stats = await _indexService.GetIndexStatsAsync();
        return new OkObjectResult(stats);
    }

    /// <summary>
    /// HTTP trigger — manual reindex endpoint.
    /// POST /api/knowledge/reindex?sourceType=github_repo|blob_storage|all
    /// </summary>
    [Function("TriggerReindex")]
    public async Task<IActionResult> TriggerReindex(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "knowledge/reindex")] HttpRequest req)
    {
        var sourceType = req.Query["sourceType"].FirstOrDefault() ?? "all";
        _logger.LogInformation("Manual reindex triggered for source type: {SourceType}", sourceType);

        await _indexService.EnsureIndexExistsAsync();
        var result = new { message = "", indexed = 0 };

        if (sourceType is "github_repo" or "all")
        {
            // Delete existing GitHub docs and re-index
            await _indexService.DeleteSourceAsync("github_repo", "all");

            var client = await _gitHubClientFactory.CreateClientAsync();
            var repos = await client.GitHubApps.Installation.GetAllRepositoriesForCurrent();
            var count = 0;

            foreach (var repo in repos.Repositories)
            {
                try
                {
                    count += await IndexGitHubDirectoryAsync(client, repo.Owner.Login, repo.Name, "docs", repo.DefaultBranch);
                }
                catch (Octokit.NotFoundException) { }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to reindex docs for repo {Repo}", repo.Name);
                }
            }

            result = new { message = $"Reindexed {count} GitHub doc files", indexed = count };
        }

        if (sourceType is "blob_storage" or "all")
        {
            await _indexService.DeleteSourceAsync("blob_storage", "all");

            var blobCount = 0;
            if (_blobServiceClient is not null)
            {
                _logger.LogInformation("BlobServiceClient available, enumerating knowledge-docs container");
                try
                {
                    var container = _blobServiceClient.GetBlobContainerClient("knowledge-docs");
                    await foreach (var blob in container.GetBlobsAsync())
                    {
                        var fileName = Path.GetFileName(blob.Name);
                        _logger.LogInformation("Found blob: {BlobName}, IsTextFile: {IsText}", blob.Name, IsTextFile(fileName));
                        if (!IsTextFile(fileName)) continue;

                        try
                        {
                            var blobClient = container.GetBlobClient(blob.Name);
                            var download = await blobClient.DownloadContentAsync();
                            var content = download.Value.Content.ToString();

                            var folder = Path.GetDirectoryName(blob.Name)?.Replace('\\', '/') ?? "knowledge-docs";
                            if (string.IsNullOrEmpty(folder)) folder = "knowledge-docs";
                            await _indexService.IndexDocumentAsync(content, fileName, "blob_storage", folder, blob.Name);
                            blobCount++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to reindex blob {Blob}", blob.Name);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to enumerate blob container for reindex");
                }
            }
            else
            {
                _logger.LogWarning("BlobServiceClient is null — KnowledgeStorage config may be missing. Cannot reindex blob storage.");
            }

            _logger.LogInformation("Blob storage reindex complete: {Count} files indexed", blobCount);
            result = result with { message = result.message + $" | Reindexed {blobCount} blob storage files", indexed = result.indexed + blobCount };
        }

        return new OkObjectResult(result);
    }

    internal static async Task<int> IndexGitHubDirectoryAsync(
        Octokit.IGitHubClient client, string owner, string repo, string path, string branch,
        IKnowledgeIndexService indexService, ILogger logger)
    {
        var indexed = 0;

        try
        {
            var contents = await client.Repository.Content.GetAllContentsByRef(owner, repo, path, branch);

            foreach (var item in contents)
            {
                if (item.Type == Octokit.ContentType.File && IsTextFile(item.Name))
                {
                    try
                    {
                        var rawContent = await client.Repository.Content.GetRawContentByRef(owner, repo, item.Path, branch);
                        var text = Encoding.UTF8.GetString(rawContent);
                        await indexService.IndexDocumentAsync(text, item.Name, "github_repo", repo, item.Path);
                        indexed++;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to index file {Repo}/{Path}", repo, item.Path);
                    }
                }
                else if (item.Type == Octokit.ContentType.Dir)
                {
                    indexed += await IndexGitHubDirectoryAsync(client, owner, repo, item.Path, branch, indexService, logger);
                }
            }
        }
        catch (Octokit.NotFoundException)
        {
            // Directory doesn't exist — skip
        }

        return indexed;
    }

    private async Task<int> IndexGitHubDirectoryAsync(
        Octokit.IGitHubClient client, string owner, string repo, string path, string branch)
    {
        return await IndexGitHubDirectoryAsync(client, owner, repo, path, branch, _indexService, _logger);
    }

    internal static bool IsTextFile(string fileName) =>
        TextExtensions.Any(ext => fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
}
