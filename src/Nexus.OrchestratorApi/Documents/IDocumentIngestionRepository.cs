namespace Nexus.OrchestratorApi.Documents;

public interface IDocumentIngestionRepository
{
    Task InsertAsync(DocumentIngestionRecord document, CancellationToken cancellationToken = default);
}

public interface IDocumentEmbeddingRepository
{
    Task<bool> DocumentExistsAsync(Guid docId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentEmbeddingChunkRecord>> GetChunksAsync(
        Guid docId,
        CancellationToken cancellationToken = default);

    Task<int> UpdatePendingEmbeddingsAsync(
        Guid docId,
        IReadOnlyList<DocumentChunkEmbeddingUpdate> updates,
        CancellationToken cancellationToken = default);
}
