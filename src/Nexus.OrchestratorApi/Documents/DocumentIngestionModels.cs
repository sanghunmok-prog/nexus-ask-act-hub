using Nexus.Contracts;

namespace Nexus.OrchestratorApi.Documents;

public sealed record DocumentUploadInput(
    string? FileName,
    string? Title,
    string? SourceName,
    Stream? Content,
    long Length);

public sealed record DocumentIngestionRecord(
    Guid DocId,
    string Title,
    string SourceName,
    IReadOnlyList<DocumentChunkRecord> Chunks);

public sealed record DocumentChunkRecord(
    Guid ChunkId,
    Guid DocId,
    int ChunkIndex,
    int CharStart,
    int CharEnd,
    string ChunkText,
    string MetadataJson);

public sealed record DocumentUploadResult(
    bool Succeeded,
    int StatusCode,
    DocumentUploadResponse? Response,
    DocumentUploadErrorResponse? Error)
{
    public static DocumentUploadResult Success(DocumentUploadResponse response) =>
        new(true, StatusCodes.Status200OK, response, null);

    public static DocumentUploadResult Failure(
        int statusCode,
        string code,
        string message,
        IReadOnlyList<string>? errors = null) =>
        new(
            false,
            statusCode,
            null,
            new DocumentUploadErrorResponse
            {
                Code = code,
                Message = message,
                Errors = errors ?? []
            });
}
