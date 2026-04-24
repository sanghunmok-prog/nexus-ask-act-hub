using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapGet("/api/health", () => Results.Ok());

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