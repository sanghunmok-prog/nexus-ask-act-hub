using System.Security.Cryptography;
using System.Text;

namespace Nexus.Embeddings;

public sealed class MockEmbeddingProvider : IEmbeddingProvider
{
    public const string StableProviderName = "mock-token-hashing";
    public const int StableDimension = 1536;

    public string ProviderName => StableProviderName;

    public int Dimension => StableDimension;

    public Task<EmbeddingResult> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var vector = new float[StableDimension];

        foreach (var token in Tokenize(text))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            var index = (int)(BitConverter.ToUInt32(hash, 0) % StableDimension);
            var sign = (hash[4] & 1) == 0 ? 1.0f : -1.0f;
            vector[index] += sign;
        }

        Normalize(vector);

        return Task.FromResult(new EmbeddingResult(ProviderName, Dimension, vector));
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        var builder = new StringBuilder();

        foreach (var character in text)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                continue;
            }

            if (builder.Length > 0)
            {
                yield return builder.ToString();
                builder.Clear();
            }
        }

        if (builder.Length > 0)
        {
            yield return builder.ToString();
        }
    }

    private static void Normalize(float[] vector)
    {
        double sumSquares = 0;

        foreach (var value in vector)
        {
            sumSquares += value * value;
        }

        if (sumSquares <= 0)
        {
            return;
        }

        var norm = Math.Sqrt(sumSquares);
        for (var index = 0; index < vector.Length; index++)
        {
            vector[index] = (float)(vector[index] / norm);
        }
    }
}
