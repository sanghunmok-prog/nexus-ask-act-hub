using Nexus.Contracts;
using Nexus.QuerySafety;

namespace Nexus.Mcp.Toolbelt.Tools;

public sealed class DbQueryReadonlyTool
{
    private readonly string allowlistPath;
    private readonly IReadonlyQueryExecutor executor;

    public DbQueryReadonlyTool(IReadonlyQueryExecutor executor)
        : this(executor, Path.Combine(AppContext.BaseDirectory, "Security", "allowlist.json"))
    {
    }

    public DbQueryReadonlyTool(IReadonlyQueryExecutor executor, string allowlistPath)
    {
        this.executor = executor;
        this.allowlistPath = allowlistPath;
    }

    public async Task<DbQueryReadonlyToolResult> QueryAsync(
        StructuredQuery query,
        CancellationToken cancellationToken = default)
    {
        var allowlist = await QueryAllowlist.LoadAsync(allowlistPath, cancellationToken);
        var validation = new StructuredQueryValidator(allowlist).Validate(query);

        if (!validation.IsValid)
        {
            return DbQueryReadonlyToolResult.ValidationFailed(validation.Errors);
        }

        var compiled = new StructuredQueryCompiler(allowlist).Compile(query);
        var response = await executor.ExecuteAsync(compiled, query.Select, cancellationToken);

        return DbQueryReadonlyToolResult.Success(response);
    }
}

public sealed record DbQueryReadonlyToolResult(
    bool Succeeded,
    int StatusCode,
    DbQueryReadonlyResponse? Response,
    DbQueryReadonlyErrorResponse? Error)
{
    public static DbQueryReadonlyToolResult Success(DbQueryReadonlyResponse response) =>
        new(true, StatusCodes.Status200OK, response, null);

    public static DbQueryReadonlyToolResult ValidationFailed(IReadOnlyList<string> errors) =>
        new(
            false,
            StatusCodes.Status400BadRequest,
            null,
            new DbQueryReadonlyErrorResponse
            {
                Code = "QUERY_VALIDATION_FAILED",
                Message = "StructuredQuery failed validation.",
                Errors = errors
            });

    public static DbQueryReadonlyToolResult ConnectionNotConfigured() =>
        new(
            false,
            StatusCodes.Status500InternalServerError,
            null,
            new DbQueryReadonlyErrorResponse
            {
                Code = "SQL_CONNECTION_NOT_CONFIGURED",
                Message = "SQL connection string is not configured."
            });

    public static DbQueryReadonlyToolResult ExecutionFailed() =>
        new(
            false,
            StatusCodes.Status500InternalServerError,
            null,
            new DbQueryReadonlyErrorResponse
            {
                Code = "SQL_QUERY_FAILED",
                Message = "SQL read query failed."
            });
}
