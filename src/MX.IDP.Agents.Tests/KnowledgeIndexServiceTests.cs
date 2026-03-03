using MX.IDP.Agents.Services;

using Xunit;

namespace MX.IDP.Agents.Tests;

public class KnowledgeIndexServiceTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void ChunkText_EmptyString_ReturnsEmptyList()
    {
        var result = KnowledgeIndexService.ChunkText("");
        Assert.Empty(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ChunkText_NullString_ReturnsEmptyList()
    {
        var result = KnowledgeIndexService.ChunkText(null!);
        Assert.Empty(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ChunkText_WhitespaceOnly_ReturnsEmptyList()
    {
        var result = KnowledgeIndexService.ChunkText("   \n\n   ");
        Assert.Empty(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ChunkText_ShortText_ReturnsSingleChunk()
    {
        var text = "This is a short document about DNS delegation.";
        var result = KnowledgeIndexService.ChunkText(text);
        Assert.Single(result);
        Assert.Equal(text, result[0]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ChunkText_LongText_ReturnsMultipleChunks()
    {
        // Create text longer than chunk size (2000 chars)
        var text = string.Join("\n\n", Enumerable.Range(1, 50).Select(i => $"Paragraph {i}: " + new string('x', 80)));
        var result = KnowledgeIndexService.ChunkText(text);
        Assert.True(result.Count > 1, $"Expected multiple chunks but got {result.Count}");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ChunkText_PrefersParagraphBoundaries()
    {
        // Create text with clear paragraph boundaries
        var para1 = new string('a', 1800);
        var para2 = new string('b', 500);
        var text = $"{para1}\n\n{para2}";
        var result = KnowledgeIndexService.ChunkText(text);

        // Should break at the paragraph boundary rather than mid-text
        Assert.True(result.Count >= 2);
        Assert.DoesNotContain("\n\n", result[0]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ChunkText_AllContentPreserved()
    {
        var text = "First section about DNS.\n\nSecond section about Terraform.\n\nThird section about monitoring.";
        var result = KnowledgeIndexService.ChunkText(text);

        var combined = string.Join(" ", result);
        Assert.Contains("DNS", combined);
        Assert.Contains("Terraform", combined);
        Assert.Contains("monitoring", combined);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GenerateDocumentId_SameInput_SameOutput()
    {
        var id1 = KnowledgeIndexService.GenerateDocumentId("github_repo", "idp-core", "docs/readme.md", 0);
        var id2 = KnowledgeIndexService.GenerateDocumentId("github_repo", "idp-core", "docs/readme.md", 0);
        Assert.Equal(id1, id2);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GenerateDocumentId_DifferentInput_DifferentOutput()
    {
        var id1 = KnowledgeIndexService.GenerateDocumentId("github_repo", "idp-core", "docs/readme.md", 0);
        var id2 = KnowledgeIndexService.GenerateDocumentId("github_repo", "idp-core", "docs/readme.md", 1);
        Assert.NotEqual(id1, id2);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GenerateDocumentId_Returns32CharHex()
    {
        var id = KnowledgeIndexService.GenerateDocumentId("blob_storage", "knowledge-docs", "runbook.md", 0);
        Assert.Equal(32, id.Length);
        Assert.Matches("^[0-9a-f]+$", id);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GenerateDocumentId_DifferentSourceTypes_DifferentIds()
    {
        var id1 = KnowledgeIndexService.GenerateDocumentId("github_repo", "test", "file.md", 0);
        var id2 = KnowledgeIndexService.GenerateDocumentId("blob_storage", "test", "file.md", 0);
        Assert.NotEqual(id1, id2);
    }
}
