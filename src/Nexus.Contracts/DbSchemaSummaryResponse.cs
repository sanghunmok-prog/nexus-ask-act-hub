namespace Nexus.Contracts;

public sealed record DbSchemaSummaryResponse
{
    public IReadOnlyList<DbSchemaTableSummary> Tables { get; init; } = [];
}

public sealed record DbSchemaTableSummary
{
    public string Name { get; init; } = string.Empty;

    public IReadOnlyList<string> Columns { get; init; } = [];
}
