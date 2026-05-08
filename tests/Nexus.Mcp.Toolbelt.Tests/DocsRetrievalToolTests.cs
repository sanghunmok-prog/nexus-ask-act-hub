using Microsoft.AspNetCore.Http;
using Nexus.Contracts;
using Nexus.Embeddings;
using Nexus.Mcp.Toolbelt.Tools;

namespace Nexus.Mcp.Toolbelt.Tests;

public sealed class DocsRetrievalToolTests
{
    [Fact]
    public async Task Docs_search_rejects_empty_query()
    {
        var result = await SearchTool(new FakeDocumentRetrievalRepository())
            .SearchAsync(new DocsSearchRequest { Query = "   " });

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.Equal("DOCS_QUERY_INVALID", result.Error?.Code);
        Assert.Contains("Query is required.", result.Error?.Errors ?? []);
    }

    [Fact]
    public async Task Docs_search_defaults_top_k_to_five()
    {
        var repository = new FakeDocumentRetrievalRepository();

        var result = await SearchTool(repository).SearchAsync(new DocsSearchRequest { Query = "shipping policy" });

        Assert.True(result.Succeeded);
        Assert.Equal(5, result.Response?.TopK);
        Assert.Equal(5, repository.LastTopK);
    }

    [Fact]
    public async Task Docs_search_rejects_non_positive_top_k()
    {
        var result = await SearchTool(new FakeDocumentRetrievalRepository())
            .SearchAsync(new DocsSearchRequest { Query = "shipping policy", TopK = 0 });

        Assert.False(result.Succeeded);
        Assert.Equal("DOCS_QUERY_INVALID", result.Error?.Code);
        Assert.Contains("TopK must be greater than 0.", result.Error?.Errors ?? []);
    }

    [Fact]
    public async Task Docs_search_clamps_top_k_above_twenty()
    {
        var repository = new FakeDocumentRetrievalRepository();

        var result = await SearchTool(repository).SearchAsync(new DocsSearchRequest
        {
            Query = "shipping policy",
            TopK = 200
        });

        Assert.True(result.Succeeded);
        Assert.Equal(20, result.Response?.TopK);
        Assert.Equal(20, repository.LastTopK);
    }

    [Fact]
    public async Task Docs_search_maps_repository_results_to_citation_ready_response()
    {
        var docId = Guid.NewGuid();
        var chunkId = Guid.NewGuid();
        var repository = new FakeDocumentRetrievalRepository
        {
            SearchResults =
            [
                new DocumentSearchRepositoryResult(
                    docId,
                    "Shipping Delay Policy",
                    "nexus-shipping-policy.md",
                    chunkId,
                    0,
                    "When an order is delayed, the shipping team must review carrier status.",
                    """{"charStart":0,"charEnd":73}""",
                    0.21)
            ]
        };

        var result = await SearchTool(repository).SearchAsync(new DocsSearchRequest
        {
            Query = " delayed shipping policy ",
            TopK = 5
        });

        Assert.True(result.Succeeded);
        Assert.Equal("delayed shipping policy", result.Response?.Query);
        Assert.Equal(1, result.Response?.ResultCount);

        var searchResult = Assert.Single(result.Response?.Results ?? []);
        Assert.Equal($"{docId}:0", searchResult.CitationId);
        Assert.Equal(docId, searchResult.DocId);
        Assert.Equal(chunkId, searchResult.ChunkId);
        Assert.Equal(0, searchResult.ChunkIndex);
        Assert.Equal("Shipping Delay Policy", searchResult.Title);
        Assert.Equal("nexus-shipping-policy.md", searchResult.SourceName);
        Assert.Equal(0.21, searchResult.Distance);
    }

    [Fact]
    public async Task Docs_search_result_count_equals_results_count_and_orders_by_distance()
    {
        var docId = Guid.NewGuid();
        var repository = new FakeDocumentRetrievalRepository
        {
            SearchResults =
            [
                SearchResult(docId, Guid.NewGuid(), 1, "second", 0.5),
                SearchResult(docId, Guid.NewGuid(), 0, "first", 0.1)
            ]
        };

        var result = await SearchTool(repository).SearchAsync(new DocsSearchRequest { Query = "shipping policy" });

        Assert.True(result.Succeeded);
        Assert.Equal(result.Response?.Results.Count, result.Response?.ResultCount);
        Assert.Collection(
            result.Response?.Results ?? [],
            item => Assert.Equal(0.1, item.Distance),
            item => Assert.Equal(0.5, item.Distance));
    }

    [Fact]
    public async Task Docs_search_snippet_is_limited_and_does_not_expose_full_long_chunk_text()
    {
        var longChunkText = string.Join(' ', Enumerable.Repeat("shipping-delay-policy", 40));
        var repository = new FakeDocumentRetrievalRepository
        {
            SearchResults =
            [
                SearchResult(Guid.NewGuid(), Guid.NewGuid(), 0, longChunkText, 0.2)
            ]
        };

        var result = await SearchTool(repository).SearchAsync(new DocsSearchRequest { Query = "shipping policy" });

        var snippet = Assert.Single(result.Response?.Results ?? []).Snippet;
        Assert.True(snippet.Length <= 240);
        Assert.NotEqual(longChunkText, snippet);
    }

    [Fact]
    public async Task Docs_get_chunk_rejects_request_with_neither_chunk_id_nor_citation_id()
    {
        var result = await GetChunkTool(new FakeDocumentRetrievalRepository())
            .GetChunkAsync(new DocsGetChunkRequest());

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.Equal("DOCS_CHUNK_LOOKUP_INVALID", result.Error?.Code);
        Assert.Contains("Provide exactly one of chunkId or citationId.", result.Error?.Errors ?? []);
    }

    [Fact]
    public async Task Docs_get_chunk_rejects_request_with_both_chunk_id_and_citation_id()
    {
        var result = await GetChunkTool(new FakeDocumentRetrievalRepository())
            .GetChunkAsync(new DocsGetChunkRequest
            {
                ChunkId = Guid.NewGuid().ToString(),
                CitationId = $"{Guid.NewGuid()}:0"
            });

        Assert.False(result.Succeeded);
        Assert.Equal("DOCS_CHUNK_LOOKUP_INVALID", result.Error?.Code);
        Assert.Contains("Provide exactly one of chunkId or citationId.", result.Error?.Errors ?? []);
    }

    [Fact]
    public async Task Docs_get_chunk_validates_malformed_citation_id()
    {
        var result = await GetChunkTool(new FakeDocumentRetrievalRepository())
            .GetChunkAsync(new DocsGetChunkRequest { CitationId = "not-a-citation" });

        Assert.False(result.Succeeded);
        Assert.Equal("DOCS_CHUNK_LOOKUP_INVALID", result.Error?.Code);
        Assert.Contains("CitationId must use the format '{docId}:{chunkIndex}'.", result.Error?.Errors ?? []);
    }

    [Fact]
    public async Task Docs_get_chunk_returns_full_chunk_text_for_valid_chunk_id_lookup()
    {
        var docId = Guid.NewGuid();
        var chunkId = Guid.NewGuid();
        var fullChunkText = "Full selected policy chunk text with all details.";
        var repository = new FakeDocumentRetrievalRepository
        {
            ChunkById = Chunk(docId, chunkId, 2, fullChunkText, """{"charStart":10,"charEnd":54}""")
        };

        var result = await GetChunkTool(repository).GetChunkAsync(new DocsGetChunkRequest
        {
            ChunkId = chunkId.ToString()
        });

        Assert.True(result.Succeeded);
        Assert.Equal($"{docId}:2", result.Response?.CitationId);
        Assert.Equal(docId, result.Response?.DocId);
        Assert.Equal(chunkId, result.Response?.ChunkId);
        Assert.Equal(2, result.Response?.ChunkIndex);
        Assert.Equal(fullChunkText, result.Response?.ChunkText);
        Assert.Equal(10, result.Response?.Metadata["charStart"]);
        Assert.Equal(54, result.Response?.Metadata["charEnd"]);
    }

    [Fact]
    public async Task Docs_get_chunk_returns_full_chunk_text_for_valid_citation_lookup()
    {
        var docId = Guid.NewGuid();
        var chunkId = Guid.NewGuid();
        var repository = new FakeDocumentRetrievalRepository
        {
            ChunkByCitation = Chunk(docId, chunkId, 3, "Full citation-selected chunk.", "{}")
        };

        var result = await GetChunkTool(repository).GetChunkAsync(new DocsGetChunkRequest
        {
            CitationId = $"{docId}:3"
        });

        Assert.True(result.Succeeded);
        Assert.Equal(chunkId, result.Response?.ChunkId);
        Assert.Equal("Full citation-selected chunk.", result.Response?.ChunkText);
        Assert.Equal(docId, repository.LastCitationDocId);
        Assert.Equal(3, repository.LastCitationChunkIndex);
    }

    [Fact]
    public async Task Docs_get_chunk_maps_missing_chunk_to_not_found()
    {
        var result = await GetChunkTool(new FakeDocumentRetrievalRepository())
            .GetChunkAsync(new DocsGetChunkRequest { ChunkId = Guid.NewGuid().ToString() });

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status404NotFound, result.StatusCode);
        Assert.Equal("DOCS_CHUNK_NOT_FOUND", result.Error?.Code);
        Assert.Equal("Document chunk was not found.", result.Error?.Message);
    }

    private static DocsSearchTool SearchTool(FakeDocumentRetrievalRepository repository) =>
        new(new MockEmbeddingProvider(), repository);

    private static DocsGetChunkTool GetChunkTool(FakeDocumentRetrievalRepository repository) =>
        new(repository);

    private static DocumentSearchRepositoryResult SearchResult(
        Guid docId,
        Guid chunkId,
        int chunkIndex,
        string chunkText,
        double distance) =>
        new(
            docId,
            "Shipping Delay Policy",
            "nexus-shipping-policy.md",
            chunkId,
            chunkIndex,
            chunkText,
            "{}",
            distance);

    private static DocumentChunkRepositoryResult Chunk(
        Guid docId,
        Guid chunkId,
        int chunkIndex,
        string chunkText,
        string metadataJson) =>
        new(
            docId,
            "Shipping Delay Policy",
            "nexus-shipping-policy.md",
            chunkId,
            chunkIndex,
            chunkText,
            metadataJson);

    private sealed class FakeDocumentRetrievalRepository : IDocumentRetrievalRepository
    {
        public IReadOnlyList<DocumentSearchRepositoryResult> SearchResults { get; init; } = [];

        public DocumentChunkRepositoryResult? ChunkById { get; init; }

        public DocumentChunkRepositoryResult? ChunkByCitation { get; init; }

        public int? LastTopK { get; private set; }

        public Guid? LastCitationDocId { get; private set; }

        public int? LastCitationChunkIndex { get; private set; }

        public Task<IReadOnlyList<DocumentSearchRepositoryResult>> SearchAsync(
            float[] queryEmbedding,
            int topK,
            CancellationToken cancellationToken = default)
        {
            LastTopK = topK;
            return Task.FromResult(SearchResults);
        }

        public Task<DocumentChunkRepositoryResult?> GetChunkByIdAsync(
            Guid chunkId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ChunkById);

        public Task<DocumentChunkRepositoryResult?> GetChunkByCitationAsync(
            Guid docId,
            int chunkIndex,
            CancellationToken cancellationToken = default)
        {
            LastCitationDocId = docId;
            LastCitationChunkIndex = chunkIndex;
            return Task.FromResult(ChunkByCitation);
        }
    }
}
