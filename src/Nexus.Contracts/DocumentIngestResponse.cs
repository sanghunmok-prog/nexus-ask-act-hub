namespace Nexus.Contracts;

public sealed record DocumentIngestResponse
{
    public Guid DocId { get; init; }

    public string Status { get; init; } = string.Empty;

    public string EmbeddingProvider { get; init; } = string.Empty;

    public int EmbeddingDimension { get; init; }

    public int EmbeddedChunkCount { get; init; }

    public int SkippedChunkCount { get; init; }
}
