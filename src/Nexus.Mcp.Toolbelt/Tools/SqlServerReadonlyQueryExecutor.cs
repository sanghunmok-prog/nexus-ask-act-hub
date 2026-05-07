using Microsoft.Data.SqlClient;
using Nexus.Contracts;
using Nexus.QuerySafety;

namespace Nexus.Mcp.Toolbelt.Tools;

public sealed class SqlServerReadonlyQueryExecutor : IReadonlyQueryExecutor
{
    private readonly string? connectionString;

    public SqlServerReadonlyQueryExecutor(IConfiguration configuration)
    {
        connectionString = configuration["NEXUS_SQL_CONNECTION_STRING"] ??
            configuration.GetConnectionString("NexusSql");
    }

    public async Task<DbQueryReadonlyResponse> ExecuteAsync(
        CompiledSqlQuery query,
        IReadOnlyList<string> selectedColumns,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new SqlConnectionNotConfiguredException();
        }

        var rows = new List<IReadOnlyDictionary<string, object?>>();

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = query.SqlText;
        command.CommandTimeout = 2;

        foreach (var parameter in query.Parameters)
        {
            command.Parameters.Add(new SqlParameter(parameter.Key, parameter.Value ?? DBNull.Value));
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>(StringComparer.Ordinal);

            foreach (var column in selectedColumns)
            {
                var value = reader[column];
                row[column] = value == DBNull.Value ? null : value;
            }

            rows.Add(row);
        }

        return new DbQueryReadonlyResponse
        {
            RowCount = rows.Count,
            Rows = rows
        };
    }
}

public sealed class SqlConnectionNotConfiguredException : Exception
{
    public SqlConnectionNotConfiguredException()
        : base("SQL connection string is not configured.")
    {
    }
}
