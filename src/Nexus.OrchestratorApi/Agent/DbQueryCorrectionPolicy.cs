using System.Text.Json;
using Nexus.Contracts;

namespace Nexus.OrchestratorApi.Agent;

public sealed class DbQueryCorrectionPolicy
{
    public const int MaxAttempts = 2;

    private static readonly HashSet<string> RecoverableErrorCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "QUERY_VALIDATION_FAILED"
    };

    public bool IsRecoverable(ToolbeltClientException exception) =>
        exception.ToolName == ToolNames.DbQueryReadonly &&
        exception.StatusCode == System.Net.HttpStatusCode.BadRequest &&
        !string.IsNullOrWhiteSpace(exception.ErrorCode) &&
        RecoverableErrorCodes.Contains(exception.ErrorCode) &&
        exception.ErrorDetails.Any(IsSchemaOrAllowlistError);

    public bool TryCorrect(StructuredQuery query, JsonElement? schemaSummary, out StructuredQuery correctedQuery)
    {
        correctedQuery = query;
        var schema = ReadSchema(schemaSummary);
        if (schema.Count == 0 || string.IsNullOrWhiteSpace(query.Table))
        {
            return false;
        }

        var tableName = ResolveTable(query.Table, schema.Keys);
        if (tableName is null)
        {
            return false;
        }

        var columns = schema[tableName];
        correctedQuery = query with
        {
            Table = tableName,
            Select = query.Select.Select(column => CorrectSelectColumn(column, columns)).ToArray(),
            Filters = query.Filters
                .Select(filter => filter with { Column = CorrectColumn(filter.Column, columns) })
                .ToArray(),
            OrderBy = query.OrderBy
                .Select(orderBy => orderBy with { Column = CorrectColumn(orderBy.Column, columns) })
                .ToArray()
        };

        return !QueriesEqual(query, correctedQuery);
    }

    private static bool IsSchemaOrAllowlistError(string error) =>
        error.Contains("not allowlisted", StringComparison.OrdinalIgnoreCase) ||
        error.Contains("unknown table", StringComparison.OrdinalIgnoreCase) ||
        error.Contains("unknown column", StringComparison.OrdinalIgnoreCase) ||
        error.Contains("invalid selected column", StringComparison.OrdinalIgnoreCase) ||
        error.Contains("invalid filter column", StringComparison.OrdinalIgnoreCase) ||
        error.Contains("invalid orderBy column", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, IReadOnlyList<string>> ReadSchema(JsonElement? schemaSummary)
    {
        var schema = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        if (schemaSummary is null ||
            !schemaSummary.Value.TryGetProperty("tables", out var tables) ||
            tables.ValueKind != JsonValueKind.Array)
        {
            return schema;
        }

        foreach (var table in tables.EnumerateArray())
        {
            if (!table.TryGetProperty("name", out var nameProperty) ||
                nameProperty.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(nameProperty.GetString()) ||
                !table.TryGetProperty("columns", out var columnsProperty) ||
                columnsProperty.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var columns = columnsProperty
                .EnumerateArray()
                .Where(column => column.ValueKind == JsonValueKind.String)
                .Select(column => column.GetString())
                .Where(column => !string.IsNullOrWhiteSpace(column))
                .Select(column => column!)
                .ToArray();

            schema[nameProperty.GetString()!] = columns;
        }

        return schema;
    }

    private static string? ResolveTable(string table, IEnumerable<string> tableNames) =>
        tableNames.FirstOrDefault(candidate => string.Equals(candidate, table, StringComparison.OrdinalIgnoreCase));

    private static string? CorrectColumn(string? column, IReadOnlyList<string> columns)
    {
        if (string.IsNullOrWhiteSpace(column))
        {
            return column;
        }

        var exact = columns.FirstOrDefault(candidate => string.Equals(candidate, column, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        var utcSuffix = columns.FirstOrDefault(candidate =>
            string.Equals(candidate, column + "Utc", StringComparison.OrdinalIgnoreCase));
        if (utcSuffix is not null)
        {
            return utcSuffix;
        }

        return column;
    }

    private static string CorrectSelectColumn(string column, IReadOnlyList<string> columns) =>
        CorrectColumn(column, columns) ?? column;

    private static bool QueriesEqual(StructuredQuery first, StructuredQuery second) =>
        JsonSerializer.Serialize(first, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ==
        JsonSerializer.Serialize(second, new JsonSerializerOptions(JsonSerializerDefaults.Web));
}
