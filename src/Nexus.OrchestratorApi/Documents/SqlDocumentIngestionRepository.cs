using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.Json;

namespace Nexus.OrchestratorApi.Documents;

public sealed class SqlDocumentIngestionRepository : IDocumentIngestionRepository, IDocumentEmbeddingRepository
{
    private static readonly JsonSerializerOptions VectorJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string? connectionString;

    public SqlDocumentIngestionRepository(IConfiguration configuration)
    {
        var environmentConnectionString = configuration["NEXUS_SQL_CONNECTION_STRING"];
        connectionString = string.IsNullOrWhiteSpace(environmentConnectionString)
            ? configuration.GetConnectionString("NexusSql")
            : environmentConnectionString;
    }

    public async Task InsertAsync(
        DocumentIngestionRecord document,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new SqlConnectionNotConfiguredException();
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (transaction)
        {
            try
            {
                await InsertDocumentAsync(connection, transaction, document, cancellationToken);

                foreach (var chunk in document.Chunks)
                {
                    await InsertChunkAsync(connection, transaction, chunk, cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
    }

    public async Task<bool> DocumentExistsAsync(
        Guid docId,
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
            SELECT COUNT(1)
            FROM dbo.PolicyDocuments
            WHERE DocId = @docId;
            """;
        command.Parameters.Add(new SqlParameter("@docId", docId));

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result) > 0;
    }

    public async Task<IReadOnlyList<DocumentEmbeddingChunkRecord>> GetChunksAsync(
        Guid docId,
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
            SELECT
                ChunkId,
                DocId,
                ChunkIndex,
                ChunkText,
                CASE WHEN Embedding IS NULL THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END AS HasEmbedding
            FROM dbo.PolicyChunks
            WHERE DocId = @docId
            ORDER BY ChunkIndex;
            """;
        command.Parameters.Add(new SqlParameter("@docId", docId));

        var chunks = new List<DocumentEmbeddingChunkRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            chunks.Add(new DocumentEmbeddingChunkRecord(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetBoolean(4)));
        }

        return chunks;
    }

    public async Task<int> UpdatePendingEmbeddingsAsync(
        Guid docId,
        IReadOnlyList<DocumentChunkEmbeddingUpdate> updates,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new SqlConnectionNotConfiguredException();
        }

        if (updates.Count == 0)
        {
            return 0;
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (transaction)
        {
            try
            {
                var updatedCount = 0;
                foreach (var update in updates)
                {
                    updatedCount += await UpdateChunkEmbeddingAsync(
                        connection,
                        transaction,
                        docId,
                        update,
                        cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                return updatedCount;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
    }

    private static async Task InsertDocumentAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        DocumentIngestionRecord document,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = 2;
        command.CommandText = """
            INSERT INTO dbo.PolicyDocuments (DocId, Title, SourceName)
            VALUES (@docId, @title, @sourceName);
            """;
        command.Parameters.Add(new SqlParameter("@docId", document.DocId));
        command.Parameters.Add(new SqlParameter("@title", document.Title));
        command.Parameters.Add(new SqlParameter("@sourceName", document.SourceName));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertChunkAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        DocumentChunkRecord chunk,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = 2;
        command.CommandText = """
            INSERT INTO dbo.PolicyChunks (
                ChunkId,
                DocId,
                ChunkIndex,
                ChunkText,
                Embedding,
                MetadataJson
            )
            VALUES (
                @chunkId,
                @docId,
                @chunkIndex,
                @chunkText,
                NULL,
                @metadataJson
            );
            """;
        command.Parameters.Add(new SqlParameter("@chunkId", chunk.ChunkId));
        command.Parameters.Add(new SqlParameter("@docId", chunk.DocId));
        command.Parameters.Add(new SqlParameter("@chunkIndex", chunk.ChunkIndex));
        command.Parameters.Add(new SqlParameter("@chunkText", chunk.ChunkText));
        command.Parameters.Add(new SqlParameter("@metadataJson", chunk.MetadataJson));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> UpdateChunkEmbeddingAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid docId,
        DocumentChunkEmbeddingUpdate update,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = 2;
        command.CommandText = """
            UPDATE dbo.PolicyChunks
            SET Embedding = CAST(@embeddingJson AS VECTOR(1536))
            WHERE ChunkId = @chunkId
              AND DocId = @docId
              AND Embedding IS NULL;
            """;
        command.Parameters.Add(new SqlParameter("@chunkId", update.ChunkId));
        command.Parameters.Add(new SqlParameter("@docId", docId));
        command.Parameters.Add(new SqlParameter("@embeddingJson", SqlDbType.NVarChar, -1)
        {
            Value = JsonSerializer.Serialize(update.Embedding, VectorJsonOptions)
        });

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

public sealed class SqlConnectionNotConfiguredException : Exception
{
    public SqlConnectionNotConfiguredException()
        : base("SQL connection string is not configured.")
    {
    }
}
