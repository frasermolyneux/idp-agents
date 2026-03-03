using System.Text;

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
    private readonly ILogger<KnowledgeIndexerFunctions> _logger;

    public KnowledgeIndexerFunctions(
        IKnowledgeIndexService indexService,
        IGitHubClientFactory gitHubClientFactory,
        ILogger<KnowledgeIndexerFunctions> logger)
    {
        _indexService = indexService;
        _gitHubClientFactory = gitHubClientFactory;
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
    /// HTTP trigger — manual reindex endpoint.
    /// POST /api/knowledge/reindex?sourceType=github_repo|blob_storage|all
    /// </summary>
    [Function("TriggerReindex")]
    public async Task<IActionResult> TriggerReindex(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "knowledge/reindex")] HttpRequest req)
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
            // For blob storage, we just clear the index — blobs will re-trigger on next upload
            await _indexService.DeleteSourceAsync("blob_storage", "all");
            result = result with { message = result.message + " | Blob storage index cleared (re-upload docs to re-index)" };
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
