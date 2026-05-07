using Nexus.Contracts;
using Nexus.QuerySafety;

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
        var allowlist = await QueryAllowlist.LoadAsync(allowlistPath, cancellationToken);

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
}
