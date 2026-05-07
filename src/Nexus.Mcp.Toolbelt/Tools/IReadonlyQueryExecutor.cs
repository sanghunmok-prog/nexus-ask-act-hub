using Nexus.Contracts;
using Nexus.QuerySafety;

namespace Nexus.Mcp.Toolbelt.Tools;

public interface IReadonlyQueryExecutor
{
    Task<DbQueryReadonlyResponse> ExecuteAsync(
        CompiledSqlQuery query,
        IReadOnlyList<string> selectedColumns,
        CancellationToken cancellationToken = default);
}
