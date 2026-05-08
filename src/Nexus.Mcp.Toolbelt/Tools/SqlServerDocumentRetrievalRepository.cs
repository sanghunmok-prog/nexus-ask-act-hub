using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace Nexus.Mcp.Toolbelt.Tools;

public sealed class SqlServerDocumentRetrievalRepository : IDocumentRetrievalRepository
{
    private static readonly JsonSerializerOptions VectorJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string? connectionString;

    public SqlServerDocumentRetrievalRepository(IConfiguration configuration)
    {
        var environmentConnectionString = configuration["NEXUS_SQL_CONNECTION_STRING"];
        connectionString = string.IsNullOrWhiteSpace(environmentConnectionString)
            ? configuration.GetConnectionString("NexusSql")
            : environmentConnectionString;
    }

    public async Task<IReadOnlyList<DocumentSearchRepositoryResult>> SearchAsync(
        float[] queryEmbedding,
        int topK,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new SqlConnectionNotConfiguredException();
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = """
            SELECT TOP (@topK)
                d.DocId,
                d.Title,
                d.SourceName,
                c.ChunkId,
                c.ChunkIndex,
                c.ChunkText,
                c.MetadataJson,
                VECTOR_DISTANCE(
                    'cosine',
                    c.Embedding,
                    CAST(@queryEmbeddingJson AS VECTOR(1536))
                ) AS Distance
            FROM dbo.PolicyChunks c
            JOIN dbo.PolicyDocuments d ON d.DocId = c.DocId
            WHERE c.Embedding IS NOT NULL
            ORDER BY Distance ASC;
            """;
        command.Parameters.Add(new SqlParameter("@topK", SqlDbType.Int) { Value = topK });
        command.Parameters.Add(new SqlParameter("@queryEmbeddingJson", SqlDbType.NVarChar, -1)
        {
            Value = JsonSerializer.Serialize(queryEmbedding, VectorJsonOptions)
        });

        var results = new List<DocumentSearchRepositoryResult>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new DocumentSearchRepositoryResult(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetGuid(3),
                reader.GetInt32(4),
                reader.GetString(5),
                reader.GetString(6),
                Convert.ToDouble(reader.GetValue(7))));
        }

        return results;
    }

    public async Task<DocumentChunkRepositoryResult?> GetChunkByIdAsync(
        Guid chunkId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                d.DocId,
                d.Title,
                d.SourceName,
                c.ChunkId,
                c.ChunkIndex,
                c.ChunkText,
                c.MetadataJson
            FROM dbo.PolicyChunks c
            JOIN dbo.PolicyDocuments d ON d.DocId = c.DocId
            WHERE c.ChunkId = @chunkId;
            """;

        return await GetChunkAsync(
            sql,
            command => command.Parameters.Add(new SqlParameter("@chunkId", chunkId)),
            cancellationToken);
    }

    public async Task<DocumentChunkRepositoryResult?> GetChunkByCitationAsync(
        Guid docId,
        int chunkIndex,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                d.DocId,
                d.Title,
                d.SourceName,
                c.ChunkId,
                c.ChunkIndex,
                c.ChunkText,
                c.MetadataJson
            FROM dbo.PolicyChunks c
            JOIN dbo.PolicyDocuments d ON d.DocId = c.DocId
            WHERE c.DocId = @docId
              AND c.ChunkIndex = @chunkIndex;
            """;

        return await GetChunkAsync(
            sql,
            command =>
            {
                command.Parameters.Add(new SqlParameter("@docId", docId));
                command.Parameters.Add(new SqlParameter("@chunkIndex", chunkIndex));
            },
            cancellationToken);
    }

    private async Task<DocumentChunkRepositoryResult?> GetChunkAsync(
        string sql,
        Action<SqlCommand> addParameters,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new SqlConnectionNotConfiguredException();
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = sql;
        addParameters(command);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new DocumentChunkRepositoryResult(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetGuid(3),
            reader.GetInt32(4),
            reader.GetString(5),
            reader.GetString(6));
    }
}
