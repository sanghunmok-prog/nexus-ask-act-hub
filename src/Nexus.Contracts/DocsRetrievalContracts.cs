namespace Nexus.Contracts;

public sealed record DocsSearchRequest
{
    public string? Query { get; init; }

    public int? TopK { get; init; }
}

public sealed record DocsSearchResponse
{
    public string Query { get; init; } = string.Empty;

    public int TopK { get; init; }

    public int ResultCount { get; init; }

    public IReadOnlyList<DocsSearchResult> Results { get; init; } = [];
}

public sealed record DocsSearchResult
{
    public string CitationId { get; init; } = string.Empty;

    public Guid DocId { get; init; }

    public Guid ChunkId { get; init; }

    public int ChunkIndex { get; init; }

    public string Title { get; init; } = string.Empty;

    public string SourceName { get; init; } = string.Empty;

    public string Snippet { get; init; } = string.Empty;

    public double Distance { get; init; }
}

public sealed record DocsGetChunkRequest
{
    public string? ChunkId { get; init; }

    public string? CitationId { get; init; }
}

public sealed record DocsGetChunkResponse
{
    public string CitationId { get; init; } = string.Empty;

    public Guid DocId { get; init; }

    public Guid ChunkId { get; init; }

    public int ChunkIndex { get; init; }

    public string Title { get; init; } = string.Empty;

    public string SourceName { get; init; } = string.Empty;

    public string ChunkText { get; init; } = string.Empty;

    public IReadOnlyDictionary<string, int> Metadata { get; init; } =
        new Dictionary<string, int>(StringComparer.Ordinal);
}

public sealed record DocsToolErrorResponse
{
    public string Code { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public IReadOnlyList<string> Errors { get; init; } = [];
}
