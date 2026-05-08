using System.Text.Json;
using Nexus.Embeddings;
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

app.MapPost("/api/chat/stream", async (HttpContext context) =>
{
    var request = await context.Request.ReadFromJsonAsync<ChatStreamRequest>(context.RequestAborted)
                  ?? new ChatStreamRequest(string.Empty);

    context.Response.StatusCode = StatusCodes.Status200OK;
    context.Response.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";

    var correlationId = Guid.NewGuid().ToString();
    var prompt = request.Prompt?.Trim() ?? string.Empty;

    var events = new object[]
    {
        CreateEnvelope(
            "workflow.started",
            correlationId,
            new
            {
                prompt
            }),
        CreateEnvelope(
            "tool.call",
            correlationId,
            new
            {
                toolName = "docs.search",
                sanitizedArgs = new
                {
                    query = prompt,
                    topK = 3
                },
                requiresApproval = false
            }),
        CreateEnvelope(
            "tool.result",
            correlationId,
            new
            {
                toolName = "docs.search",
                rowCount = 0,
                citationCount = 1,
                summary = "Returned 1 mock policy citation"
            }),
        CreateEnvelope(
            "assistant.message",
            correlationId,
            new
            {
                message = $"Mock answer for: {prompt}",
                citations = new object[]
                {
                    new
                    {
                        citationId = "mock-policy:1",
                        sourceName = "ShippingPolicy.pdf",
                        snippet = "Delayed shipments must include a customer update within one business day."
                    }
                }
            }),
        CreateEnvelope(
            "done",
            correlationId,
            new
            {
                success = true
            })
    };

    foreach (var envelope in events)
    {
        if (context.RequestAborted.IsCancellationRequested)
        {
            break;
        }

        await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(envelope)}\n\n", context.RequestAborted);
        await context.Response.Body.FlushAsync(context.RequestAborted);
    }
});

app.Run();

static object CreateEnvelope(string eventType, string correlationId, object payload) =>
    new
    {
        eventType,
        correlationId,
        timestampUtc = DateTime.UtcNow,
        payload
    };

internal sealed record ChatStreamRequest(string Prompt);
