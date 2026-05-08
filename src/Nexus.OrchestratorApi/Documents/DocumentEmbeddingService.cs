using Nexus.Contracts;
using Nexus.Embeddings;

namespace Nexus.OrchestratorApi.Documents;

public sealed class DocumentEmbeddingService
{
    public const string EmbeddedStatus = "Embedded";
    public const string AlreadyEmbeddedStatus = "AlreadyEmbedded";

    private readonly IEmbeddingProvider embeddingProvider;
    private readonly IDocumentEmbeddingRepository repository;

    public DocumentEmbeddingService(
        IEmbeddingProvider embeddingProvider,
        IDocumentEmbeddingRepository repository)
    {
        this.embeddingProvider = embeddingProvider;
        this.repository = repository;
    }

    public async Task<DocumentEmbeddingResult> IngestAsync(
        Guid docId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await repository.DocumentExistsAsync(docId, cancellationToken))
            {
                return DocumentEmbeddingResult.Failure(
                    StatusCodes.Status404NotFound,
                    "DOCUMENT_NOT_FOUND",
                    "Document was not found.");
            }

            var chunks = await repository.GetChunksAsync(docId, cancellationToken);
            if (chunks.Count == 0)
            {
                return DocumentEmbeddingResult.Failure(
                    StatusCodes.Status404NotFound,
                    "DOCUMENT_CHUNKS_NOT_FOUND",
                    "Document chunks were not found.");
            }

            var pendingChunks = chunks
                .Where(chunk => !chunk.HasEmbedding)
                .OrderBy(chunk => chunk.ChunkIndex)
                .ToArray();

            var updates = new List<DocumentChunkEmbeddingUpdate>(pendingChunks.Length);
            foreach (var chunk in pendingChunks)
            {
                if (string.IsNullOrWhiteSpace(chunk.ChunkText))
                {
                    return DocumentEmbeddingResult.Failure(
                        StatusCodes.Status500InternalServerError,
                        "DOCUMENT_EMBEDDING_FAILED",
                        "Document embedding failed.");
                }

                var embedding = await embeddingProvider.GenerateEmbeddingAsync(chunk.ChunkText, cancellationToken);
                if (embedding.Dimension != embeddingProvider.Dimension ||
                    embedding.Vector.Length != embeddingProvider.Dimension)
                {
                    return DocumentEmbeddingResult.Failure(
                        StatusCodes.Status500InternalServerError,
                        "DOCUMENT_EMBEDDING_FAILED",
                        "Document embedding failed.");
                }

                updates.Add(new DocumentChunkEmbeddingUpdate(chunk.ChunkId, embedding.Vector));
            }

            var embeddedCount = updates.Count == 0
                ? 0
                : await repository.UpdatePendingEmbeddingsAsync(docId, updates, cancellationToken);

            var skippedCount = chunks.Count(chunk => chunk.HasEmbedding);
            var status = embeddedCount > 0 ? EmbeddedStatus : AlreadyEmbeddedStatus;

            return DocumentEmbeddingResult.Success(new DocumentIngestResponse
            {
                DocId = docId,
                Status = status,
                EmbeddingProvider = embeddingProvider.ProviderName,
                EmbeddingDimension = embeddingProvider.Dimension,
                EmbeddedChunkCount = embeddedCount,
                SkippedChunkCount = skippedCount
            });
        }
        catch (SqlConnectionNotConfiguredException)
        {
            return DocumentEmbeddingResult.Failure(
                StatusCodes.Status500InternalServerError,
                "SQL_CONNECTION_NOT_CONFIGURED",
                "SQL connection string is not configured.");
        }
        catch (Exception)
        {
            return DocumentEmbeddingResult.Failure(
                StatusCodes.Status500InternalServerError,
                "DOCUMENT_EMBEDDING_FAILED",
                "Document embedding failed.");
        }
    }
}
