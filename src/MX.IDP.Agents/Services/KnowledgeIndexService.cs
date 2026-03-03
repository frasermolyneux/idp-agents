using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;

using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.Embeddings;

namespace MX.IDP.Agents.Services;

public interface IKnowledgeIndexService
{
    Task EnsureIndexExistsAsync();
    Task IndexDocumentAsync(string content, string title, string sourceType, string sourceName, string filePath);
    Task<string> SearchAsync(string query, string? sourceType = null, string? sourceName = null, int maxResults = 5);
    Task<string> ListSourcesAsync();
    Task DeleteSourceAsync(string sourceType, string sourceName);
}

#pragma warning disable SKEXP0001
public class KnowledgeIndexService : IKnowledgeIndexService
{
    private const string IndexName = "knowledge-index";
    private const int ChunkSize = 2000;
    private const int ChunkOverlap = 200;
    private const int EmbeddingDimensions = 1536;

    private readonly SearchIndexClient _indexClient;
    private readonly SearchClient _searchClient;
    private readonly ITextEmbeddingGenerationService _embeddingService;
    private readonly ILogger<KnowledgeIndexService> _logger;
    private bool _indexEnsured;

    public KnowledgeIndexService(
        SearchIndexClient indexClient,
        SearchClient searchClient,
        ITextEmbeddingGenerationService embeddingService,
        ILogger<KnowledgeIndexService> logger)
    {
        _indexClient = indexClient;
        _searchClient = searchClient;
        _embeddingService = embeddingService;
        _logger = logger;
    }

    public async Task EnsureIndexExistsAsync()
    {
        if (_indexEnsured) return;

        var fields = new List<SearchField>
        {
            new("id", SearchFieldDataType.String) { IsKey = true, IsFilterable = true },
            new("content", SearchFieldDataType.String) { IsSearchable = true, AnalyzerName = LexicalAnalyzerName.EnMicrosoft },
            new("content_vector", SearchFieldDataType.Collection(SearchFieldDataType.Single))
            {
                IsSearchable = true,
                VectorSearchDimensions = EmbeddingDimensions,
                VectorSearchProfileName = "default-profile"
            },
            new("title", SearchFieldDataType.String) { IsSearchable = true, IsFilterable = true },
            new("source_type", SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
            new("source_name", SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
            new("file_path", SearchFieldDataType.String) { IsFilterable = true },
            new("chunk_index", SearchFieldDataType.Int32),
            new("last_updated", SearchFieldDataType.DateTimeOffset) { IsFilterable = true, IsSortable = true }
        };

        var vectorSearch = new VectorSearch();
        vectorSearch.Profiles.Add(new VectorSearchProfile("default-profile", "default-hnsw"));
        vectorSearch.Algorithms.Add(new HnswAlgorithmConfiguration("default-hnsw"));

        var index = new SearchIndex(IndexName)
        {
            Fields = fields,
            VectorSearch = vectorSearch
        };

        await _indexClient.CreateOrUpdateIndexAsync(index);
        _indexEnsured = true;
        _logger.LogInformation("Search index '{IndexName}' ensured", IndexName);
    }

    public async Task IndexDocumentAsync(string content, string title, string sourceType, string sourceName, string filePath)
    {
        var chunks = ChunkText(content);
        var batch = new IndexDocumentsBatch<SearchDocument>();

        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            var id = GenerateDocumentId(sourceType, sourceName, filePath, i);
            var embedding = await GenerateEmbeddingAsync(chunk);

            var doc = new SearchDocument
            {
                ["id"] = id,
                ["content"] = chunk,
                ["content_vector"] = embedding.ToArray(),
                ["title"] = title,
                ["source_type"] = sourceType,
                ["source_name"] = sourceName,
                ["file_path"] = filePath,
                ["chunk_index"] = i,
                ["last_updated"] = DateTimeOffset.UtcNow
            };

            batch.Actions.Add(IndexDocumentsAction.Upload(doc));
        }

        if (batch.Actions.Count > 0)
        {
            await _searchClient.IndexDocumentsAsync(batch);
            _logger.LogInformation("Indexed {Count} chunks for {SourceType}/{SourceName}/{FilePath}",
                chunks.Count, sourceType, sourceName, filePath);
        }
    }

    public async Task<string> SearchAsync(string query, string? sourceType = null, string? sourceName = null, int maxResults = 5)
    {
        await EnsureIndexExistsAsync();

        var searchOptions = new SearchOptions
        {
            Size = maxResults,
            SearchMode = SearchMode.Any,
            Select = { "title", "content", "source_type", "source_name", "file_path", "chunk_index" }
        };

        // Add vector search
        try
        {
            var embedding = await GenerateEmbeddingAsync(query);
            searchOptions.VectorSearch = new VectorSearchOptions();
            searchOptions.VectorSearch.Queries.Add(new VectorizedQuery(embedding)
            {
                KNearestNeighborsCount = maxResults * 2,
                Fields = { "content_vector" }
            });
        }
        catch (Exception ex)
        {
            // Fall back to text-only search if embedding fails
            _logger.LogWarning(ex, "Failed to generate embedding for query, falling back to text search");
        }

        var filters = new List<string>();
        if (sourceType is not null) filters.Add($"source_type eq '{sourceType}'");
        if (sourceName is not null) filters.Add($"source_name eq '{sourceName}'");
        if (filters.Count > 0) searchOptions.Filter = string.Join(" and ", filters);

        var results = await _searchClient.SearchAsync<SearchDocument>(query, searchOptions);

        var hits = new List<object>();
        await foreach (var result in results.Value.GetResultsAsync())
        {
            hits.Add(new
            {
                title = result.Document.GetString("title"),
                content = result.Document.GetString("content"),
                source_type = result.Document.GetString("source_type"),
                source_name = result.Document.GetString("source_name"),
                file_path = result.Document.GetString("file_path"),
                score = result.Score
            });
        }

        _logger.LogInformation("Knowledge search for '{Query}' returned {Count} results", query, hits.Count);

        return JsonSerializer.Serialize(new { count = hits.Count, results = hits },
            new JsonSerializerOptions { WriteIndented = true });
    }

    public async Task<string> ListSourcesAsync()
    {
        await EnsureIndexExistsAsync();
        var searchOptions = new SearchOptions
        {
            Size = 0,
            Facets = { "source_type,count:10", "source_name,count:100" }
        };

        var results = await _searchClient.SearchAsync<SearchDocument>("*", searchOptions);

        var sources = new List<object>();
        if (results.Value.Facets.TryGetValue("source_name", out var facets))
        {
            foreach (var facet in facets)
            {
                sources.Add(new { name = facet.Value, count = facet.Count });
            }
        }

        return JsonSerializer.Serialize(new { sources },
            new JsonSerializerOptions { WriteIndented = true });
    }

    public async Task DeleteSourceAsync(string sourceType, string sourceName)
    {
        await EnsureIndexExistsAsync();
        var searchOptions = new SearchOptions
        {
            Size = 1000,
            Select = { "id" },
            Filter = sourceName == "all"
                ? $"source_type eq '{sourceType}'"
                : $"source_type eq '{sourceType}' and source_name eq '{sourceName}'"
        };

        var results = await _searchClient.SearchAsync<SearchDocument>("*", searchOptions);
        var batch = new IndexDocumentsBatch<SearchDocument>();

        await foreach (var result in results.Value.GetResultsAsync())
        {
            var doc = new SearchDocument { ["id"] = result.Document.GetString("id") };
            batch.Actions.Add(IndexDocumentsAction.Delete(doc));
        }

        if (batch.Actions.Count > 0)
        {
            await _searchClient.IndexDocumentsAsync(batch);
            _logger.LogInformation("Deleted {Count} documents for {SourceType}/{SourceName}",
                batch.Actions.Count, sourceType, sourceName);
        }
    }

    public static List<string> ChunkText(string text)
    {
        var chunks = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return chunks;

        var currentPos = 0;
        while (currentPos < text.Length)
        {
            var endPos = Math.Min(currentPos + ChunkSize, text.Length);

            // Try to break at a paragraph boundary
            if (endPos < text.Length)
            {
                var searchLength = Math.Min(endPos - currentPos, 500);
                var breakPos = text.LastIndexOf("\n\n", endPos, searchLength);
                if (breakPos > currentPos) endPos = breakPos;
            }

            var chunk = text[currentPos..endPos].Trim();
            if (chunk.Length > 0) chunks.Add(chunk);

            // If we've reached the end, stop
            if (endPos >= text.Length) break;

            // Advance with overlap
            currentPos = endPos - ChunkOverlap;
        }

        return chunks;
    }

    private async Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(string text)
    {
        var embeddings = await _embeddingService.GenerateEmbeddingsAsync([text]);
        return embeddings[0];
    }

    public static string GenerateDocumentId(string sourceType, string sourceName, string filePath, int chunkIndex)
    {
        var input = $"{sourceType}:{sourceName}:{filePath}:{chunkIndex}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }
}
#pragma warning restore SKEXP0001
