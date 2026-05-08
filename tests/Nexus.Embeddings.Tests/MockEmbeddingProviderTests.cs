using Nexus.Embeddings;

namespace Nexus.Embeddings.Tests;

public sealed class MockEmbeddingProviderTests
{
    [Fact]
    public async Task Returns_expected_provider_metadata_and_dimension()
    {
        var result = await new MockEmbeddingProvider().GenerateEmbeddingAsync("shipping delay policy");

        Assert.Equal("mock-token-hashing", result.ProviderName);
        Assert.Equal(1536, result.Dimension);
        Assert.Equal(1536, result.Vector.Length);
    }

    [Fact]
    public async Task Same_input_returns_identical_vector_values()
    {
        var provider = new MockEmbeddingProvider();

        var first = await provider.GenerateEmbeddingAsync("Delayed shipments require updates.");
        var second = await provider.GenerateEmbeddingAsync("Delayed shipments require updates.");

        Assert.Equal(first.Vector, second.Vector);
    }

    [Fact]
    public async Task Different_input_usually_returns_different_vector_values()
    {
        var provider = new MockEmbeddingProvider();

        var first = await provider.GenerateEmbeddingAsync("delayed shipments policy");
        var second = await provider.GenerateEmbeddingAsync("invoice payment approval");

        Assert.NotEqual(first.Vector, second.Vector);
    }

    [Fact]
    public async Task Shared_tokens_share_non_zero_dimensions()
    {
        var provider = new MockEmbeddingProvider();

        var first = await provider.GenerateEmbeddingAsync("delayed shipments customer update");
        var second = await provider.GenerateEmbeddingAsync("delayed shipments carrier notice");

        var sharedNonZeroDimensions = first.Vector
            .Select((value, index) => new { value, index })
            .Where(item => item.value != 0 && second.Vector[item.index] != 0)
            .Count();

        Assert.True(sharedNonZeroDimensions >= 2);
    }

    [Fact]
    public async Task Vector_contains_no_nan_or_infinity()
    {
        var result = await new MockEmbeddingProvider().GenerateEmbeddingAsync("shipping delay policy");

        Assert.All(result.Vector, value =>
        {
            Assert.False(float.IsNaN(value));
            Assert.False(float.IsInfinity(value));
        });
    }

    [Fact]
    public async Task Non_empty_vector_is_l2_normalized()
    {
        var result = await new MockEmbeddingProvider().GenerateEmbeddingAsync("shipping delay policy");

        var norm = Math.Sqrt(result.Vector.Sum(value => value * value));

        Assert.InRange(norm, 0.99999, 1.00001);
    }

    [Fact]
    public async Task Empty_text_returns_deterministic_zero_vector()
    {
        var provider = new MockEmbeddingProvider();

        var first = await provider.GenerateEmbeddingAsync(" ");
        var second = await provider.GenerateEmbeddingAsync("");

        Assert.Equal(first.Vector, second.Vector);
        Assert.All(first.Vector, value => Assert.Equal(0, value));
    }
}
