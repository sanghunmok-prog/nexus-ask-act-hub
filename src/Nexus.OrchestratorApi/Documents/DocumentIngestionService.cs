using System.Text.Json;
using Nexus.Contracts;

namespace Nexus.OrchestratorApi.Documents;

public sealed class DocumentIngestionService
{
    public const string ChunkedPendingEmbeddingStatus = "ChunkedPendingEmbedding";

    private static readonly JsonSerializerOptions MetadataJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly DocumentTextExtractor textExtractor;
    private readonly DocumentChunker chunker;
    private readonly IDocumentIngestionRepository repository;

    public DocumentIngestionService(
        DocumentTextExtractor textExtractor,
        DocumentChunker chunker,
        IDocumentIngestionRepository repository)
    {
        this.textExtractor = textExtractor;
        this.chunker = chunker;
        this.repository = repository;
    }

    public async Task<DocumentUploadResult> UploadAsync(
        DocumentUploadInput input,
        CancellationToken cancellationToken = default)
    {
        if (input.Content is null || string.IsNullOrWhiteSpace(input.FileName))
        {
            return DocumentUploadResult.Failure(
                StatusCodes.Status400BadRequest,
                "DOCUMENT_FILE_REQUIRED",
                "Document file is required.");
        }

        if (input.Length <= 0)
        {
            return DocumentUploadResult.Failure(
                StatusCodes.Status400BadRequest,
                "DOCUMENT_FILE_EMPTY",
                "Document file must not be empty.");
        }

        if (!textExtractor.IsSupportedExtension(input.FileName))
        {
            return DocumentUploadResult.Failure(
                StatusCodes.Status400BadRequest,
                "DOCUMENT_TYPE_NOT_SUPPORTED",
                "Document type is not supported.");
        }

        string text;
        try
        {
            text = await textExtractor.ExtractAsync(input.FileName, input.Content, cancellationToken);
        }
        catch (Exception)
        {
            return DocumentUploadResult.Failure(
                StatusCodes.Status400BadRequest,
                "DOCUMENT_TEXT_EMPTY",
                "Document text could not be extracted.");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return DocumentUploadResult.Failure(
                StatusCodes.Status400BadRequest,
                "DOCUMENT_TEXT_EMPTY",
                "Document text could not be extracted.");
        }

        var title = NormalizeTitle(input.Title, input.FileName);
        var sourceName = string.IsNullOrWhiteSpace(input.SourceName)
            ? Path.GetFileName(input.FileName)
            : input.SourceName.Trim();

        var textChunks = chunker.Chunk(text);
        var docId = Guid.NewGuid();
        var chunkRecords = textChunks
            .Select(chunk => ToChunkRecord(docId, title, sourceName, chunk))
            .ToArray();

        var document = new DocumentIngestionRecord(docId, title, sourceName, chunkRecords);

        try
        {
            await repository.InsertAsync(document, cancellationToken);
        }
        catch (SqlConnectionNotConfiguredException)
        {
            return DocumentUploadResult.Failure(
                StatusCodes.Status500InternalServerError,
                "SQL_CONNECTION_NOT_CONFIGURED",
                "SQL connection string is not configured.");
        }
        catch (Exception)
        {
            return DocumentUploadResult.Failure(
                StatusCodes.Status500InternalServerError,
                "DOCUMENT_INGESTION_FAILED",
                "Document ingestion failed.");
        }

        var chunkSummaries = chunkRecords
            .Select(chunk => new DocumentUploadChunkSummary
            {
                ChunkIndex = chunk.ChunkIndex,
                CharStart = chunk.CharStart,
                CharEnd = chunk.CharEnd,
                Preview = CreatePreview(chunk.ChunkText)
            })
            .ToArray();

        return DocumentUploadResult.Success(new DocumentUploadResponse
        {
            DocId = docId,
            Title = title,
            SourceName = sourceName,
            Status = ChunkedPendingEmbeddingStatus,
            ChunkCount = chunkRecords.Length,
            Chunks = chunkSummaries
        });
    }

    private static string NormalizeTitle(string? title, string fileName)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            return title.Trim();
        }

        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        return string.IsNullOrWhiteSpace(fileNameWithoutExtension)
            ? "Untitled document"
            : fileNameWithoutExtension;
    }

    private static DocumentChunkRecord ToChunkRecord(
        Guid docId,
        string title,
        string sourceName,
        DocumentTextChunk chunk)
    {
        var metadataJson = JsonSerializer.Serialize(
            new
            {
                title,
                sourceName,
                chunk.ChunkIndex,
                chunk.CharStart,
                chunk.CharEnd,
                chunkSize = DocumentChunker.DefaultChunkSize,
                overlap = DocumentChunker.DefaultOverlap
            },
            MetadataJsonOptions);

        return new DocumentChunkRecord(
            Guid.NewGuid(),
            docId,
            chunk.ChunkIndex,
            chunk.CharStart,
            chunk.CharEnd,
            chunk.Text,
            metadataJson);
    }

    private static string CreatePreview(string text)
    {
        var preview = text.Trim();
        return preview.Length <= 200 ? preview : preview[..200];
    }
}
