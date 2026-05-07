using Nexus.Contracts;
using Nexus.Mcp.Toolbelt.Tools;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddSingleton<DbSchemaSummaryTool>();
builder.Services.AddSingleton<DbQueryReadonlyTool>();
builder.Services.AddSingleton<IReadonlyQueryExecutor, SqlServerReadonlyQueryExecutor>();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapGet("/api/health", () => Results.Ok());
app.MapGet("/api/tools/db/schema-summary", async (
    DbSchemaSummaryTool tool,
    CancellationToken cancellationToken) =>
{
    var schemaSummary = await tool.GetSchemaSummaryAsync(cancellationToken);
    return Results.Ok(schemaSummary);
});
app.MapPost("/api/tools/db/query-readonly", async (
    StructuredQuery query,
    DbQueryReadonlyTool tool,
    CancellationToken cancellationToken) =>
{
    try
    {
        var result = await tool.QueryAsync(query, cancellationToken);
        return result.Succeeded
            ? Results.Ok(result.Response)
            : Results.Json(result.Error, statusCode: result.StatusCode);
    }
    catch (SqlConnectionNotConfiguredException)
    {
        var result = DbQueryReadonlyToolResult.ConnectionNotConfigured();
        return Results.Json(result.Error, statusCode: result.StatusCode);
    }
    catch (Exception)
    {
        var result = DbQueryReadonlyToolResult.ExecutionFailed();
        return Results.Json(result.Error, statusCode: result.StatusCode);
    }
});

app.Run();
