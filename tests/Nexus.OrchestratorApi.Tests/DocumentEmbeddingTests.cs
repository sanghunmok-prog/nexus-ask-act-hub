using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Nexus.Embeddings;
using Nexus.OrchestratorApi.Documents;

namespace Nexus.OrchestratorApi.Tests;

public sealed class DocumentEmbeddingTests
{
    [Fact]
    public async Task Missing_document_returns_document_not_found()
    {
        var repository = new FakeDocumentEmbeddingRepository(documentExists: false);

        var result = await Service(repository).IngestAsync(Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status404NotFound, result.StatusCode);
        Assert.Equal("DOCUMENT_NOT_FOUND", result.Error?.Code);
    }

    [Fact]
    public async Task Document_with_no_chunks_returns_chunks_not_found()
    {
        var repository = new FakeDocumentEmbeddingRepository(documentExists: true);

        var result = await Service(repository).IngestAsync(Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status404NotFound, result.StatusCode);
        Assert.Equal("DOCUMENT_CHUNKS_NOT_FOUND", result.Error?.Code);
    }

    [Fact]
    public async Task Pending_chunks_are_embedded_and_counts_are_returned()
    {
        var docId = Guid.NewGuid();
        var repository = new FakeDocumentEmbeddingRepository(documentExists: true)
        {
            Chunks =
            [
                Chunk(docId, 0, "shipping delay policy", hasEmbedding: false),
                Chunk(docId, 1, "customer update policy", hasEmbedding: false)
            ]
        };

        var result = await Service(repository).IngestAsync(docId);

        Assert.True(result.Succeeded);
        Assert.Equal("Embedded", result.Response?.Status);
        Assert.Equal("mock-token-hashing", result.Response?.EmbeddingProvider);
        Assert.Equal(1536, result.Response?.EmbeddingDimension);
        Assert.Equal(2, result.Response?.EmbeddedChunkCount);
        Assert.Equal(0, result.Response?.SkippedChunkCount);
        Assert.Equal(2, repository.UpdatedEmbeddings.Count);
        Assert.All(repository.UpdatedEmbeddings, update => Assert.Equal(1536, update.Embedding.Length));
    }

    [Fact]
    public async Task Already_embedded_chunks_are_skipped()
    {
        var docId = Guid.NewGuid();
        var repository = new FakeDocumentEmbeddingRepository(documentExists: true)
        {
            Chunks =
            [
                Chunk(docId, 0, "shipping delay policy", hasEmbedding: true),
                Chunk(docId, 1, "customer update policy", hasEmbedding: true)
            ]
        };

        var result = await Service(repository).IngestAsync(docId);

        Assert.True(result.Succeeded);
        Assert.Equal("AlreadyEmbedded", result.Response?.Status);
        Assert.Equal(0, result.Response?.EmbeddedChunkCount);
        Assert.Equal(2, result.Response?.SkippedChunkCount);
        Assert.Empty(repository.UpdatedEmbeddings);
    }

    [Fact]
    public async Task Mixed_chunks_embed_pending_and_skip_existing()
    {
        var docId = Guid.NewGuid();
        var repository = new FakeDocumentEmbeddingRepository(documentExists: true)
        {
            Chunks =
            [
                Chunk(docId, 0, "shipping delay policy", hasEmbedding: true),
                Chunk(docId, 1, "customer update policy", hasEmbedding: false)
            ]
        };

        var result = await Service(repository).IngestAsync(docId);

        Assert.True(result.Succeeded);
        Assert.Equal("Embedded", result.Response?.Status);
        Assert.Equal(1, result.Response?.EmbeddedChunkCount);
        Assert.Equal(1, result.Response?.SkippedChunkCount);
    }

    [Fact]
    public async Task Response_does_not_include_embedding_values_or_chunk_text()
    {
        var docId = Guid.NewGuid();
        var repository = new FakeDocumentEmbeddingRepository(documentExists: true)
        {
            Chunks =
            [
                Chunk(docId, 0, "secret chunk text", hasEmbedding: false)
            ]
        };

        var result = await Service(repository).IngestAsync(docId);
        var json = JsonSerializer.Serialize(result.Response, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.DoesNotContain("secret chunk text", json);
        Assert.DoesNotContain("embedding\":", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("embeddedChunkCount", json);
    }

    private static DocumentEmbeddingService Service(FakeDocumentEmbeddingRepository repository) =>
        new(new MockEmbeddingProvider(), repository);

    private static DocumentEmbeddingChunkRecord Chunk(
        Guid docId,
        int chunkIndex,
        string text,
        bool hasEmbedding) =>
        new(Guid.NewGuid(), docId, chunkIndex, text, hasEmbedding);

    private sealed class FakeDocumentEmbeddingRepository : IDocumentEmbeddingRepository
    {
        private readonly bool documentExists;

        public FakeDocumentEmbeddingRepository(bool documentExists)
        {
            this.documentExists = documentExists;
        }

        public IReadOnlyList<DocumentEmbeddingChunkRecord> Chunks { get; init; } = [];

        public List<DocumentChunkEmbeddingUpdate> UpdatedEmbeddings { get; } = [];

        public Task<bool> DocumentExistsAsync(Guid docId, CancellationToken cancellationToken = default) =>
            Task.FromResult(documentExists);

        public Task<IReadOnlyList<DocumentEmbeddingChunkRecord>> GetChunksAsync(
            Guid docId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Chunks);

        public Task<int> UpdatePendingEmbeddingsAsync(
            Guid docId,
            IReadOnlyList<DocumentChunkEmbeddingUpdate> updates,
            CancellationToken cancellationToken = default)
        {
            UpdatedEmbeddings.AddRange(updates);
            return Task.FromResult(updates.Count);
        }
    }
}
