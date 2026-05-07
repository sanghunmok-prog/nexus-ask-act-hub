namespace Nexus.OrchestratorApi.Documents;

public sealed class DocumentChunker
{
    public const int DefaultChunkSize = 1000;
    public const int DefaultOverlap = 150;

    private readonly int chunkSize;
    private readonly int overlap;

    public DocumentChunker()
        : this(DefaultChunkSize, DefaultOverlap)
    {
    }

    public DocumentChunker(int chunkSize, int overlap)
    {
        if (chunkSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be greater than zero.");
        }

        if (overlap < 0 || overlap >= chunkSize)
        {
            throw new ArgumentOutOfRangeException(nameof(overlap), "Overlap must be greater than or equal to zero and less than chunk size.");
        }

        this.chunkSize = chunkSize;
        this.overlap = overlap;
    }

    public IReadOnlyList<DocumentTextChunk> Chunk(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var chunks = new List<DocumentTextChunk>();
        var start = 0;

        while (start < text.Length)
        {
            var end = Math.Min(start + chunkSize, text.Length);
            var chunkText = text[start..end];

            chunks.Add(new DocumentTextChunk(
                chunks.Count,
                start,
                end,
                chunkText));

            if (end >= text.Length)
            {
                break;
            }

            var nextStart = end - overlap;
            start = nextStart > start ? nextStart : start + 1;
        }

        return chunks;
    }
}

public sealed record DocumentTextChunk(
    int ChunkIndex,
    int CharStart,
    int CharEnd,
    string Text);
