namespace Nexus.Contracts;

public sealed record StructuredQuery
{
    public string? Table { get; init; }

    public IReadOnlyList<string> Select { get; init; } = [];

    public IReadOnlyList<StructuredQueryFilter> Filters { get; init; } = [];

    public IReadOnlyList<StructuredQueryOrderBy> OrderBy { get; init; } = [];

    public int? Limit { get; init; }
}

public sealed record StructuredQueryFilter
{
    public string? Column { get; init; }

    public string? Op { get; init; }

    public string? Value { get; init; }

    public string? Value2 { get; init; }
}

public sealed record StructuredQueryOrderBy
{
    public string? Column { get; init; }

    public string? Dir { get; init; }
}
