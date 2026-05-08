namespace Nexus.Embeddings;

public interface IEmbeddingProvider
{
    string ProviderName { get; }

    int Dimension { get; }

    Task<EmbeddingResult> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);
}

public sealed record EmbeddingResult(
    string ProviderName,
    int Dimension,
    float[] Vector);
