using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nexus.QuerySafety;

public sealed record QueryAllowlist
{
    public IReadOnlyDictionary<string, QueryAllowlistTable> Tables { get; init; } =
        new Dictionary<string, QueryAllowlistTable>(StringComparer.OrdinalIgnoreCase);

    public int MaxLimit { get; init; }

    public bool SingleTableOnly { get; init; }

    public static async Task<QueryAllowlist> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);

        var allowlist = await JsonSerializer.DeserializeAsync<QueryAllowlist>(
            stream,
            QueryAllowlistJsonContext.Default.QueryAllowlist,
            cancellationToken);

        return Normalize(allowlist ?? throw new InvalidOperationException("Allowlist file is empty or invalid."));
    }

    private static QueryAllowlist Normalize(QueryAllowlist allowlist) =>
        allowlist with
        {
            Tables = allowlist.Tables.ToDictionary(
                table => table.Key,
                table => table.Value with
                {
                    ColumnTypes = table.Value.ColumnTypes.ToDictionary(
                        columnType => columnType.Key,
                        columnType => columnType.Value,
                        StringComparer.OrdinalIgnoreCase)
                },
                StringComparer.OrdinalIgnoreCase)
        };
}

public sealed record QueryAllowlistTable
{
    public IReadOnlyList<string> Select { get; init; } = [];

    public IReadOnlyList<string> Filter { get; init; } = [];

    public IReadOnlyList<string> OrderBy { get; init; } = [];

    public IReadOnlyDictionary<string, string> ColumnTypes { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(QueryAllowlist))]
internal sealed partial class QueryAllowlistJsonContext : JsonSerializerContext;
