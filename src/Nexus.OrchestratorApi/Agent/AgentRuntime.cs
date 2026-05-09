using System.Text.Json;

namespace Nexus.OrchestratorApi.Agent;

public sealed class AgentRuntime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IChatPlanner planner;
    private readonly IToolbeltClient toolbeltClient;

    public AgentRuntime(IChatPlanner planner, IToolbeltClient toolbeltClient)
    {
        this.planner = planner;
        this.toolbeltClient = toolbeltClient;
    }

    public async Task<IReadOnlyList<SseEnvelope>> RunAsync(
        string prompt,
        Guid correlationId,
        Func<SseEnvelope, CancellationToken, Task> emitAsync,
        CancellationToken cancellationToken = default)
    {
        var emitted = new List<SseEnvelope>();

        async Task Emit(string eventType, object payload)
        {
            var envelope = SseEnvelope.Create(eventType, correlationId, payload);
            emitted.Add(envelope);
            await emitAsync(envelope, cancellationToken);
        }

        await Emit(
            "workflow.started",
            new
            {
                prompt
            });

        var plan = await planner.PlanAsync(prompt, cancellationToken);
        if (!plan.Succeeded)
        {
            await EmitSanitizedError(plan.ErrorCode ?? "PLANNER_FAILED", plan.ErrorMessage ?? "Planner failed.");
            await EmitDone(success: false);
            return emitted;
        }

        var summary = new AgentExecutionSummary();

        foreach (var step in plan.Steps)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await Emit(
                "tool.call",
                new
                {
                    toolName = step.ToolName,
                    sanitizedArgs = step.Args,
                    requiresApproval = false
                });

            ToolbeltToolResult result;
            try
            {
                result = await toolbeltClient.CallAsync(step, cancellationToken);
            }
            catch (ToolbeltConfigurationException)
            {
                await EmitSanitizedError(
                    "TOOLBELT_NOT_CONFIGURED",
                    "Toolbelt base URL is not configured.",
                    step.ToolName);
                await EmitDone(success: false);
                return emitted;
            }
            catch (ToolbeltClientException exception)
            {
                await EmitSanitizedError(
                    "TOOLBELT_CALL_FAILED",
                    "Toolbelt call failed.",
                    exception.ToolName,
                    exception.StatusCode is null ? null : (int)exception.StatusCode.Value);
                await EmitDone(success: false);
                return emitted;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                await EmitSanitizedError(
                    "TOOLBELT_CALL_FAILED",
                    "Toolbelt call failed.",
                    step.ToolName);
                await EmitDone(success: false);
                return emitted;
            }

            var toolResultPayload = ToolResultSummarizer.Summarize(result);
            summary.Apply(result);
            await Emit("tool.result", toolResultPayload);
        }

        await Emit(
            "assistant.message",
            new
            {
                message = $"Found {summary.SqlRowCount} delayed order rows and {summary.DocumentResultCount} relevant policy search results. Hybrid answer composition will be added in PR-12.",
                summary = new
                {
                    sqlRowCount = summary.SqlRowCount,
                    documentResultCount = summary.DocumentResultCount
                }
            });

        await EmitDone(success: true);
        return emitted;

        Task EmitSanitizedError(string code, string message, string? toolName = null, int? statusCode = null) =>
            Emit(
                "error",
                new
                {
                    code,
                    message,
                    retryable = false,
                    toolName,
                    statusCode
                });

        Task EmitDone(bool success) =>
            Emit(
                "done",
                new
                {
                    success
                });
    }

    private sealed class AgentExecutionSummary
    {
        public int SqlRowCount { get; private set; }

        public int DocumentResultCount { get; private set; }

        public void Apply(ToolbeltToolResult result)
        {
            if (result.ToolName == ToolNames.DocsSearch &&
                result.RawJson.TryGetProperty("resultCount", out var resultCount) &&
                resultCount.TryGetInt32(out var documentResultCount))
            {
                DocumentResultCount = documentResultCount;
            }

            if (result.ToolName == ToolNames.DbQueryReadonly &&
                result.RawJson.TryGetProperty("rowCount", out var rowCount) &&
                rowCount.TryGetInt32(out var sqlRowCount))
            {
                SqlRowCount = sqlRowCount;
            }
        }
    }

    private static class ToolResultSummarizer
    {
        public static object Summarize(ToolbeltToolResult result) =>
            result.ToolName switch
            {
                ToolNames.DocsSearch => SummarizeDocsSearch(result.RawJson),
                ToolNames.DbGetSchemaSummary => SummarizeSchemaSummary(result.RawJson),
                ToolNames.DbQueryReadonly => SummarizeDbQueryReadonly(result.RawJson),
                _ => new
                {
                    toolName = result.ToolName,
                    summary = "Tool returned a result."
                }
            };

        private static object SummarizeDocsSearch(JsonElement rawJson)
        {
            var resultCount = TryGetInt(rawJson, "resultCount");
            object? topResult = null;

            if (rawJson.TryGetProperty("results", out var results) &&
                results.ValueKind == JsonValueKind.Array &&
                results.GetArrayLength() > 0)
            {
                var first = results[0];
                topResult = new
                {
                    citationId = TryGetString(first, "citationId"),
                    sourceName = TryGetString(first, "sourceName"),
                    title = TryGetString(first, "title")
                };
            }

            return new
            {
                toolName = ToolNames.DocsSearch,
                resultCount,
                topResult,
                result = IncludeRawIfSmall(rawJson)
            };
        }

        private static object SummarizeSchemaSummary(JsonElement rawJson)
        {
            var tableNames = new List<string>();
            if (rawJson.TryGetProperty("tables", out var tables) &&
                tables.ValueKind == JsonValueKind.Array)
            {
                foreach (var table in tables.EnumerateArray())
                {
                    var name = TryGetString(table, "name");
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        tableNames.Add(name);
                    }
                }
            }

            return new
            {
                toolName = ToolNames.DbGetSchemaSummary,
                tableCount = tableNames.Count,
                tableNames,
                result = IncludeRawIfSmall(rawJson)
            };
        }

        private static object SummarizeDbQueryReadonly(JsonElement rawJson)
        {
            var rowCount = TryGetInt(rawJson, "rowCount");

            return new
            {
                toolName = ToolNames.DbQueryReadonly,
                rowCount,
                rows = TryGetRows(rawJson),
                result = IncludeRawIfSmall(rawJson)
            };
        }

        private static int TryGetInt(JsonElement element, string propertyName) =>
            element.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var value)
                ? value
                : 0;

        private static string? TryGetString(JsonElement element, string propertyName) =>
            element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;

        private static JsonElement? IncludeRawIfSmall(JsonElement rawJson)
        {
            var json = rawJson.GetRawText();
            return json.Length <= 10_000 ? rawJson.Clone() : null;
        }

        private static IReadOnlyList<JsonElement> TryGetRows(JsonElement rawJson)
        {
            if (!rawJson.TryGetProperty("rows", out var rows) || rows.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return rows.EnumerateArray().Take(5).Select(row => row.Clone()).ToArray();
        }
    }
}

public sealed record SseEnvelope
{
    public required string EventType { get; init; }

    public required string CorrelationId { get; init; }

    public required DateTime TimestampUtc { get; init; }

    public required object Payload { get; init; }

    public static SseEnvelope Create(string eventType, Guid correlationId, object payload) =>
        new()
        {
            EventType = eventType,
            CorrelationId = correlationId.ToString(),
            TimestampUtc = DateTime.UtcNow,
            Payload = payload
        };
}

public sealed class SseEventWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task WriteAsync(HttpResponse response, SseEnvelope envelope, CancellationToken cancellationToken = default)
    {
        await response.WriteAsync($"data: {JsonSerializer.Serialize(envelope, JsonOptions)}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }
}
