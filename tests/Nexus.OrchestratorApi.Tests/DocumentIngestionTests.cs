using System.Text;
using System.Text.Json;
using Nexus.OrchestratorApi.Documents;

namespace Nexus.OrchestratorApi.Tests;

public sealed class DocumentIngestionTests
{
    [Fact]
    public void Text_chunker_returns_one_chunk_for_short_text()
    {
        var chunks = new DocumentChunker(chunkSize: 1000, overlap: 150).Chunk("short policy text");

        var chunk = Assert.Single(chunks);
        Assert.Equal(0, chunk.ChunkIndex);
        Assert.Equal(0, chunk.CharStart);
        Assert.Equal("short policy text".Length, chunk.CharEnd);
        Assert.Equal("short policy text", chunk.Text);
    }

    [Fact]
    public void Text_chunker_preserves_order()
    {
        var chunks = new DocumentChunker(chunkSize: 5, overlap: 1).Chunk("abcdefghijkl");

        Assert.Collection(
            chunks,
            chunk => Assert.Equal("abcde", chunk.Text),
            chunk => Assert.Equal("efghi", chunk.Text),
            chunk => Assert.Equal("ijkl", chunk.Text));
    }

    [Fact]
    public void Text_chunker_applies_overlap()
    {
        var chunks = new DocumentChunker(chunkSize: 5, overlap: 2).Chunk("abcdefghijkl");

        Assert.Equal(0, chunks[0].CharStart);
        Assert.Equal(5, chunks[0].CharEnd);
        Assert.Equal(3, chunks[1].CharStart);
        Assert.Equal(8, chunks[1].CharEnd);
        Assert.Equal("abcde", chunks[0].Text);
        Assert.Equal("defgh", chunks[1].Text);
    }

    [Fact]
    public void Text_chunker_does_not_infinite_loop_when_overlap_is_high()
    {
        var chunks = new DocumentChunker(chunkSize: 3, overlap: 2).Chunk("abcdefghijklmnopqrst");

        Assert.True(chunks.Count <= 20);
        Assert.Equal(20, chunks[^1].CharEnd);
    }

    [Fact]
    public async Task Unsupported_extension_is_rejected()
    {
        var repository = new FakeDocumentIngestionRepository();

        var result = await Service(repository).UploadAsync(Input("policy.docx", "text"));

        Assert.False(result.Succeeded);
        Assert.Equal("DOCUMENT_TYPE_NOT_SUPPORTED", result.Error?.Code);
        Assert.Null(repository.InsertedDocument);
    }

    [Fact]
    public async Task Empty_extracted_text_is_rejected()
    {
        var repository = new FakeDocumentIngestionRepository();

        var result = await Service(repository).UploadAsync(Input("policy.txt", "   \r\n\t"));

        Assert.False(result.Succeeded);
        Assert.Equal("DOCUMENT_TEXT_EMPTY", result.Error?.Code);
        Assert.Null(repository.InsertedDocument);
    }

    [Fact]
    public async Task Upload_ingestion_service_inserts_document_and_chunk_records()
    {
        var repository = new FakeDocumentIngestionRepository();

        var result = await Service(repository).UploadAsync(Input("shipping-policy.md", "Delayed shipments require a customer update."));

        Assert.True(result.Succeeded);
        Assert.NotNull(repository.InsertedDocument);
        Assert.Equal(result.Response?.DocId, repository.InsertedDocument.DocId);
        Assert.Equal("shipping-policy", repository.InsertedDocument.Title);
        Assert.Equal("shipping-policy.md", repository.InsertedDocument.SourceName);
        Assert.Single(repository.InsertedDocument.Chunks);
        Assert.Equal("Delayed shipments require a customer update.", repository.InsertedDocument.Chunks.Single().ChunkText);
    }

    [Fact]
    public async Task Chunk_metadata_contains_char_offsets()
    {
        var repository = new FakeDocumentIngestionRepository();

        await Service(repository).UploadAsync(Input("policy.txt", "abcdef"));

        var chunk = Assert.Single(repository.InsertedDocument?.Chunks ?? []);
        using var metadata = JsonDocument.Parse(chunk.MetadataJson);
        Assert.Equal(0, metadata.RootElement.GetProperty("charStart").GetInt32());
        Assert.Equal(6, metadata.RootElement.GetProperty("charEnd").GetInt32());
        Assert.Equal(0, metadata.RootElement.GetProperty("chunkIndex").GetInt32());
        Assert.Equal(1000, metadata.RootElement.GetProperty("chunkSize").GetInt32());
        Assert.Equal(150, metadata.RootElement.GetProperty("overlap").GetInt32());
    }

    [Fact]
    public async Task Response_chunk_count_matches_inserted_chunk_count()
    {
        var repository = new FakeDocumentIngestionRepository();
        var text = string.Join(string.Empty, Enumerable.Repeat("abcdefghij", 120));

        var result = await Service(repository).UploadAsync(Input("policy.md", text));

        Assert.True(result.Succeeded);
        Assert.Equal(repository.InsertedDocument?.Chunks.Count, result.Response?.ChunkCount);
        Assert.Equal(result.Response?.ChunkCount, result.Response?.Chunks.Count);
        Assert.All(result.Response?.Chunks ?? [], chunk => Assert.True(chunk.Preview.Length <= 200));
        Assert.Equal(DocumentIngestionService.ChunkedPendingEmbeddingStatus, result.Response?.Status);
    }

    private static DocumentIngestionService Service(FakeDocumentIngestionRepository repository) =>
        new(new DocumentTextExtractor(), new DocumentChunker(), repository);

    private static DocumentUploadInput Input(string fileName, string content) =>
        new(
            fileName,
            null,
            null,
            new MemoryStream(Encoding.UTF8.GetBytes(content)),
            Encoding.UTF8.GetByteCount(content));

    private sealed class FakeDocumentIngestionRepository : IDocumentIngestionRepository
    {
        public DocumentIngestionRecord? InsertedDocument { get; private set; }

        public Task InsertAsync(DocumentIngestionRecord document, CancellationToken cancellationToken = default)
        {
            InsertedDocument = document;
            return Task.CompletedTask;
        }
    }
}
