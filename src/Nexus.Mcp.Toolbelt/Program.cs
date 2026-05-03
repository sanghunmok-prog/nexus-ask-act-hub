using Nexus.Mcp.Toolbelt.Tools;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddSingleton<DbSchemaSummaryTool>();

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

app.Run();
