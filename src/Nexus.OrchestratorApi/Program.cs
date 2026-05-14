using Nexus.Embeddings;
using Nexus.OrchestratorApi.Agent;
using Nexus.OrchestratorApi.Approvals;
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
builder.Services.AddSingleton<ApprovalIntentFactory>();
builder.Services.AddSingleton<ApprovalService>();
builder.Services.AddSingleton<IApprovalRepository, SqlApprovalRepository>();
builder.Services.AddSingleton<IChatPlanner>(services => ChatPlannerFactory.Create(services.GetRequiredService<IConfiguration>()));
builder.Services.AddHttpClient<IToolbeltClient, HttpToolbeltClient>();
#pragma warning disable EXTEXP0001
builder.Services.AddHttpClient<IToolbeltWriteClient, HttpToolbeltWriteClient>()
    .RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
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

app.MapGet("/api/approvals/pending", async (
    ApprovalService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await service.GetPendingApprovalsAsync(cancellationToken));
    }
    catch
    {
        return Results.Json(
            new ApprovalErrorResponse
            {
                Code = "APPROVAL_PERSISTENCE_FAILED",
                Message = "Approval requests could not be loaded."
            },
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapGet("/api/approvals/ready", async (
    ApprovalService service,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await service.GetReadyApprovalsAsync(cancellationToken));
    }
    catch (Exception exception)
    {
        loggerFactory
            .CreateLogger("Nexus.OrchestratorApi.Approvals.ReadyEndpoint")
            .LogError(exception, "Ready approvals could not be loaded.");

        return Results.Json(
            new ApprovalErrorResponse
            {
                Code = "APPROVAL_PERSISTENCE_FAILED",
                Message = "Ready approvals could not be loaded."
            },
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapPost("/api/approvals/{approvalId:guid}/approve", async (
    Guid approvalId,
    HttpContext context,
    ApprovalService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        var result = await service.ApproveAsync(
            approvalId,
            ResolveUserId(context.Request),
            cancellationToken);

        return result.Succeeded
            ? Results.Ok(result.Response)
            : Results.Json(result.Error, statusCode: result.StatusCode);
    }
    catch
    {
        return Results.Json(
            new ApprovalErrorResponse
            {
                Code = "APPROVAL_PERSISTENCE_FAILED",
                Message = "Approval request could not be updated."
            },
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapPost("/api/approvals/{approvalId:guid}/reject", async (
    Guid approvalId,
    ApprovalService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        var result = await service.RejectAsync(approvalId, cancellationToken);
        return result.Succeeded
            ? Results.Ok(result.Response)
            : Results.Json(result.Error, statusCode: result.StatusCode);
    }
    catch
    {
        return Results.Json(
            new ApprovalErrorResponse
            {
                Code = "APPROVAL_PERSISTENCE_FAILED",
                Message = "Approval request could not be updated."
            },
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapPost("/api/approvals/{approvalId:guid}/execute", async (
    Guid approvalId,
    ApprovalService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        var result = await service.ExecuteAsync(approvalId, cancellationToken);
        if (result.Response is not null)
        {
            return Results.Json(result.Response, statusCode: result.StatusCode);
        }

        return Results.Json(result.Error, statusCode: result.StatusCode);
    }
    catch
    {
        return Results.Json(
            new ApprovalErrorResponse
            {
                Code = "APPROVAL_EXECUTION_FAILED",
                Message = "Approved action could not be executed."
            },
            statusCode: StatusCodes.Status500InternalServerError);
    }
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
        ResolveUserId(context.Request),
        context.RequestAborted);
});

static string ResolveUserId(HttpRequest request)
{
    var userId = request.Headers["X-Nexus-UserId"].FirstOrDefault();
    return string.IsNullOrWhiteSpace(userId) ? "demo-user" : userId.Trim();
}

app.Run();

internal sealed record ChatStreamRequest(string Prompt);
