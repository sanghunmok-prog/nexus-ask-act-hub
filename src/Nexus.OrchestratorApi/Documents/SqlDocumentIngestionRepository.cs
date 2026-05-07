using Microsoft.Data.SqlClient;

namespace Nexus.OrchestratorApi.Documents;

public sealed class SqlDocumentIngestionRepository : IDocumentIngestionRepository
{
    private readonly string? connectionString;

    public SqlDocumentIngestionRepository(IConfiguration configuration)
    {
        connectionString = configuration["NEXUS_SQL_CONNECTION_STRING"] ??
            configuration.GetConnectionString("NexusSql");
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
}

public sealed class SqlConnectionNotConfiguredException : Exception
{
    public SqlConnectionNotConfiguredException()
        : base("SQL connection string is not configured.")
    {
    }
}
