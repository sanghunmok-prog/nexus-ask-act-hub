using System.Text.Json;
using Nexus.Contracts;

namespace Nexus.Mcp.Toolbelt.Tools;

public sealed class DocsGetChunkTool
{
    private readonly IDocumentRetrievalRepository repository;

    public DocsGetChunkTool(IDocumentRetrievalRepository repository)
    {
        this.repository = repository;
    }

    public async Task<DocsGetChunkToolResult> GetChunkAsync(
        DocsGetChunkRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = Validate(request);
        if (validation.Errors.Count > 0)
        {
            return DocsGetChunkToolResult.ValidationFailed(validation.Errors);
        }

        DocumentChunkRepositoryResult? chunk = validation.ChunkId.HasValue
            ? await repository.GetChunkByIdAsync(validation.ChunkId.Value, cancellationToken)
            : await repository.GetChunkByCitationAsync(
                validation.CitationDocId!.Value,
                validation.CitationChunkIndex!.Value,
                cancellationToken);

        if (chunk is null)
        {
            return DocsGetChunkToolResult.NotFound();
        }

        return DocsGetChunkToolResult.Success(new DocsGetChunkResponse
        {
            CitationId = $"{chunk.DocId}:{chunk.ChunkIndex}",
            DocId = chunk.DocId,
            ChunkId = chunk.ChunkId,
            ChunkIndex = chunk.ChunkIndex,
            Title = chunk.Title,
            SourceName = chunk.SourceName,
            ChunkText = chunk.ChunkText,
            Metadata = ParseMetadata(chunk.MetadataJson)
        });
    }

    private static ValidatedDocsGetChunkRequest Validate(DocsGetChunkRequest request)
    {
        var errors = new List<string>();
        var hasChunkId = !string.IsNullOrWhiteSpace(request.ChunkId);
        var hasCitationId = !string.IsNullOrWhiteSpace(request.CitationId);

        if (hasChunkId == hasCitationId)
        {
            errors.Add("Provide exactly one of chunkId or citationId.");
            return new ValidatedDocsGetChunkRequest(null, null, null, errors);
        }

        if (hasChunkId)
        {
            if (!Guid.TryParse(request.ChunkId, out var chunkId))
            {
                errors.Add("ChunkId must be a valid GUID.");
                return new ValidatedDocsGetChunkRequest(null, null, null, errors);
            }

            return new ValidatedDocsGetChunkRequest(chunkId, null, null, errors);
        }

        var citationParts = request.CitationId!.Split(':', StringSplitOptions.TrimEntries);
        if (citationParts.Length != 2 ||
            !Guid.TryParse(citationParts[0], out var docId) ||
            !int.TryParse(citationParts[1], out var chunkIndex) ||
            chunkIndex < 0)
        {
            errors.Add("CitationId must use the format '{docId}:{chunkIndex}'.");
            return new ValidatedDocsGetChunkRequest(null, null, null, errors);
        }

        return new ValidatedDocsGetChunkRequest(null, docId, chunkIndex, errors);
    }

    private static IReadOnlyDictionary<string, int> ParseMetadata(string metadataJson)
    {
        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            var metadata = new Dictionary<string, int>(StringComparer.Ordinal);

            if (document.RootElement.TryGetProperty("charStart", out var charStart) &&
                charStart.TryGetInt32(out var charStartValue))
            {
                metadata["charStart"] = charStartValue;
            }

            if (document.RootElement.TryGetProperty("charEnd", out var charEnd) &&
                charEnd.TryGetInt32(out var charEndValue))
            {
                metadata["charEnd"] = charEndValue;
            }

            return metadata;
        }
        catch (JsonException)
        {
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }
    }

    private sealed record ValidatedDocsGetChunkRequest(
        Guid? ChunkId,
        Guid? CitationDocId,
        int? CitationChunkIndex,
        IReadOnlyList<string> Errors);
}

public sealed record DocsGetChunkToolResult(
    bool Succeeded,
    int StatusCode,
    DocsGetChunkResponse? Response,
    DocsToolErrorResponse? Error)
{
    public static DocsGetChunkToolResult Success(DocsGetChunkResponse response) =>
        new(true, StatusCodes.Status200OK, response, null);

    public static DocsGetChunkToolResult ValidationFailed(IReadOnlyList<string> errors) =>
        new(
            false,
            StatusCodes.Status400BadRequest,
            null,
            new DocsToolErrorResponse
            {
                Code = "DOCS_CHUNK_LOOKUP_INVALID",
                Message = "Document chunk lookup is invalid.",
                Errors = errors
            });

    public static DocsGetChunkToolResult NotFound() =>
        new(
            false,
            StatusCodes.Status404NotFound,
            null,
            new DocsToolErrorResponse
            {
                Code = "DOCS_CHUNK_NOT_FOUND",
                Message = "Document chunk was not found."
            });

    public static DocsGetChunkToolResult ConnectionNotConfigured() =>
        new(
            false,
            StatusCodes.Status500InternalServerError,
            null,
            new DocsToolErrorResponse
            {
                Code = "SQL_CONNECTION_NOT_CONFIGURED",
                Message = "SQL connection string is not configured."
            });

    public static DocsGetChunkToolResult LookupFailed() =>
        new(
            false,
            StatusCodes.Status500InternalServerError,
            null,
            new DocsToolErrorResponse
            {
                Code = "DOCS_CHUNK_LOOKUP_FAILED",
                Message = "Document chunk lookup failed."
            });
}
