namespace Nexus.Contracts;

public sealed record DbQueryReadonlyResponse
{
    public int RowCount { get; init; }

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows { get; init; } = [];
}

public sealed record DbQueryReadonlyErrorResponse
{
    public string Code { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public IReadOnlyList<string> Errors { get; init; } = [];
}
