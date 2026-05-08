using Nexus.Contracts;
using Nexus.Embeddings;

namespace Nexus.Mcp.Toolbelt.Tools;

public sealed class DocsSearchTool
{
    public const int DefaultTopK = 5;
    public const int MaxTopK = 20;
    private const int MaxSnippetLength = 240;

    private readonly IEmbeddingProvider embeddingProvider;
    private readonly IDocumentRetrievalRepository repository;

    public DocsSearchTool(
        IEmbeddingProvider embeddingProvider,
        IDocumentRetrievalRepository repository)
    {
        this.embeddingProvider = embeddingProvider;
        this.repository = repository;
    }

    public async Task<DocsSearchToolResult> SearchAsync(
        DocsSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = Validate(request);
        if (validation.Errors.Count > 0)
        {
            return DocsSearchToolResult.ValidationFailed(validation.Errors);
        }

        var embedding = await embeddingProvider.GenerateEmbeddingAsync(validation.Query, cancellationToken);
        var rows = await repository.SearchAsync(embedding.Vector, validation.TopK, cancellationToken);

        var results = rows
            .OrderBy(row => row.Distance)
            .Select(row => new DocsSearchResult
            {
                CitationId = CreateCitationId(row.DocId, row.ChunkIndex),
                DocId = row.DocId,
                ChunkId = row.ChunkId,
                ChunkIndex = row.ChunkIndex,
                Title = row.Title,
                SourceName = row.SourceName,
                Snippet = CreateSnippet(row.ChunkText),
                Distance = row.Distance
            })
            .ToArray();

        return DocsSearchToolResult.Success(new DocsSearchResponse
        {
            Query = validation.Query,
            TopK = validation.TopK,
            ResultCount = results.Length,
            Results = results
        });
    }

    private static ValidatedDocsSearchRequest Validate(DocsSearchRequest request)
    {
        var errors = new List<string>();
        var query = request.Query?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(query))
        {
            errors.Add("Query is required.");
        }

        var topK = request.TopK ?? DefaultTopK;
        if (topK <= 0)
        {
            errors.Add("TopK must be greater than 0.");
        }
        else if (topK > MaxTopK)
        {
            topK = MaxTopK;
        }

        return new ValidatedDocsSearchRequest(query, topK, errors);
    }

    private static string CreateCitationId(Guid docId, int chunkIndex) =>
        $"{docId}:{chunkIndex}";

    private static string CreateSnippet(string chunkText)
    {
        var snippet = string.Join(' ', chunkText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return snippet.Length <= MaxSnippetLength ? snippet : snippet[..MaxSnippetLength];
    }

    private sealed record ValidatedDocsSearchRequest(
        string Query,
        int TopK,
        IReadOnlyList<string> Errors);
}

public sealed record DocsSearchToolResult(
    bool Succeeded,
    int StatusCode,
    DocsSearchResponse? Response,
    DocsToolErrorResponse? Error)
{
    public static DocsSearchToolResult Success(DocsSearchResponse response) =>
        new(true, StatusCodes.Status200OK, response, null);

    public static DocsSearchToolResult ValidationFailed(IReadOnlyList<string> errors) =>
        new(
            false,
            StatusCodes.Status400BadRequest,
            null,
            new DocsToolErrorResponse
            {
                Code = "DOCS_QUERY_INVALID",
                Message = "Document search query is invalid.",
                Errors = errors
            });

    public static DocsSearchToolResult ConnectionNotConfigured() =>
        new(
            false,
            StatusCodes.Status500InternalServerError,
            null,
            new DocsToolErrorResponse
            {
                Code = "SQL_CONNECTION_NOT_CONFIGURED",
                Message = "SQL connection string is not configured."
            });

    public static DocsSearchToolResult SearchFailed() =>
        new(
            false,
            StatusCodes.Status500InternalServerError,
            null,
            new DocsToolErrorResponse
            {
                Code = "DOCS_SEARCH_FAILED",
                Message = "Document search failed."
            });
}
