using Nexus.Embeddings;
using Nexus.OrchestratorApi.Agent;
using Nexus.OrchestratorApi.Documents;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddSingleton<DocumentTextExtractor>();
builder.Services.AddSingleton<DocumentChunker>();
builder.Services.AddSingleton<DocumentIngestionService>();
builder.Services.AddSingleton<DocumentEmbeddingService>();
builder.Services.AddSingleton<IEmbeddingProvider, MockEmbeddingProvider>();
builder.Services.AddSingleton<IDocumentIngestionRepository, SqlDocumentIngestionRepository>();
builder.Services.AddSingleton<IDocumentEmbeddingRepository, SqlDocumentIngestionRepository>();
builder.Services.AddSingleton<IChatPlanner>(services => ChatPlannerFactory.Create(services.GetRequiredService<IConfiguration>()));
builder.Services.AddHttpClient<IToolbeltClient, HttpToolbeltClient>();
builder.Services.AddSingleton<HybridResponseComposer>();
builder.Services.AddSingleton<AgentRuntime>();
builder.Services.AddSingleton<SseEventWriter>();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapGet("/api/health", () => Results.Ok());
app.MapPost("/api/documents/upload", async (
    HttpRequest request,
    DocumentIngestionService service,
    CancellationToken cancellationToken) =>
{
    if (!request.HasFormContentType)
    {
        var missingFileResult = DocumentUploadResult.Failure(
            StatusCodes.Status400BadRequest,
            "DOCUMENT_FILE_REQUIRED",
            "Document file is required.");

        return Results.Json(missingFileResult.Error, statusCode: missingFileResult.StatusCode);
    }

    var form = await request.ReadFormAsync(cancellationToken);
    var file = form.Files.GetFile("file");

    var input = new DocumentUploadInput(
        file?.FileName,
        form["title"].FirstOrDefault(),
        form["sourceName"].FirstOrDefault(),
        file?.OpenReadStream(),
        file?.Length ?? 0);

    await using (input.Content)
    {
        var result = await service.UploadAsync(input, cancellationToken);
        return result.Succeeded
            ? Results.Ok(result.Response)
            : Results.Json(result.Error, statusCode: result.StatusCode);
    }
});
app.MapPost("/api/documents/{docId:guid}/ingest", async (
    Guid docId,
    DocumentEmbeddingService service,
    CancellationToken cancellationToken) =>
{
    var result = await service.IngestAsync(docId, cancellationToken);
    return result.Succeeded
        ? Results.Ok(result.Response)
        : Results.Json(result.Error, statusCode: result.StatusCode);
});

app.MapPost("/api/chat/stream", async (
    HttpContext context,
    AgentRuntime runtime,
    SseEventWriter eventWriter) =>
{
    var request = await context.Request.ReadFromJsonAsync<ChatStreamRequest>(context.RequestAborted)
                  ?? new ChatStreamRequest(string.Empty);

    context.Response.StatusCode = StatusCodes.Status200OK;
    context.Response.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";

    var correlationId = Guid.NewGuid();
    var prompt = request.Prompt?.Trim() ?? string.Empty;

    await runtime.RunAsync(
        prompt,
        correlationId,
        (envelope, cancellationToken) => eventWriter.WriteAsync(context.Response, envelope, cancellationToken),
        context.RequestAborted);
});

app.Run();

internal sealed record ChatStreamRequest(string Prompt);
