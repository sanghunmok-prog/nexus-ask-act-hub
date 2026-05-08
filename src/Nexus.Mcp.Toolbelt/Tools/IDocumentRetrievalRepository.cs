namespace Nexus.Mcp.Toolbelt.Tools;

public interface IDocumentRetrievalRepository
{
    Task<IReadOnlyList<DocumentSearchRepositoryResult>> SearchAsync(
        float[] queryEmbedding,
        int topK,
        CancellationToken cancellationToken = default);

    Task<DocumentChunkRepositoryResult?> GetChunkByIdAsync(
        Guid chunkId,
        CancellationToken cancellationToken = default);

    Task<DocumentChunkRepositoryResult?> GetChunkByCitationAsync(
        Guid docId,
        int chunkIndex,
        CancellationToken cancellationToken = default);
}

public sealed record DocumentSearchRepositoryResult(
    Guid DocId,
    string Title,
    string SourceName,
    Guid ChunkId,
    int ChunkIndex,
    string ChunkText,
    string MetadataJson,
    double Distance);

public sealed record DocumentChunkRepositoryResult(
    Guid DocId,
    string Title,
    string SourceName,
    Guid ChunkId,
    int ChunkIndex,
    string ChunkText,
    string MetadataJson);
