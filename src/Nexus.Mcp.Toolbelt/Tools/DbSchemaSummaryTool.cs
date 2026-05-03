using System.Text.Json;
using System.Text.Json.Serialization;
using Nexus.Contracts;

namespace Nexus.Mcp.Toolbelt.Tools;

public sealed class DbSchemaSummaryTool
{
    private readonly string allowlistPath;

    public DbSchemaSummaryTool()
        : this(Path.Combine(AppContext.BaseDirectory, "Security", "allowlist.json"))
    {
    }

    public DbSchemaSummaryTool(string allowlistPath)
    {
        this.allowlistPath = allowlistPath;
    }

    public async Task<DbSchemaSummaryResponse> GetSchemaSummaryAsync(CancellationToken cancellationToken = default)
    {
        var allowlist = await LoadAllowlistAsync(cancellationToken);

        var tables = allowlist.Tables
            .Select(table => new DbSchemaTableSummary
            {
                Name = table.Key,
                Columns = table.Value.Select
            })
            .ToArray();

        return new DbSchemaSummaryResponse
        {
            Tables = tables
        };
    }

    private async Task<ToolbeltAllowlist> LoadAllowlistAsync(CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(allowlistPath);

        var allowlist = await JsonSerializer.DeserializeAsync(
            stream,
            ToolbeltAllowlistJsonContext.Default.ToolbeltAllowlist,
            cancellationToken);

        return allowlist ?? throw new InvalidOperationException("Allowlist file is empty or invalid.");
    }
}

internal sealed record ToolbeltAllowlist
{
    public IReadOnlyDictionary<string, ToolbeltAllowlistTable> Tables { get; init; } =
        new Dictionary<string, ToolbeltAllowlistTable>(StringComparer.OrdinalIgnoreCase);
}

internal sealed record ToolbeltAllowlistTable
{
    public IReadOnlyList<string> Select { get; init; } = [];
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ToolbeltAllowlist))]
internal sealed partial class ToolbeltAllowlistJsonContext : JsonSerializerContext;
