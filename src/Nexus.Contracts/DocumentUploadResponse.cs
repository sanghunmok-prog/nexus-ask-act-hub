namespace Nexus.Contracts;

public sealed record DocumentUploadResponse
{
    public Guid DocId { get; init; }

    public string Title { get; init; } = string.Empty;

    public string SourceName { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public int ChunkCount { get; init; }

    public IReadOnlyList<DocumentUploadChunkSummary> Chunks { get; init; } = [];
}

public sealed record DocumentUploadChunkSummary
{
    public int ChunkIndex { get; init; }

    public int CharStart { get; init; }

    public int CharEnd { get; init; }

    public string Preview { get; init; } = string.Empty;
}

public sealed record DocumentUploadErrorResponse
{
    public string Code { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public IReadOnlyList<string> Errors { get; init; } = [];
}
