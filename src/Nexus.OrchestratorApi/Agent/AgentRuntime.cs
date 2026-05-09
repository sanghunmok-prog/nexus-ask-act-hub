using System.Text.Json;
using Nexus.Contracts;

namespace Nexus.OrchestratorApi.Agent;

public sealed class AgentRuntime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IChatPlanner planner;
    private readonly IToolbeltClient toolbeltClient;
    private readonly HybridResponseComposer responseComposer;

    public AgentRuntime(IChatPlanner planner, IToolbeltClient toolbeltClient, HybridResponseComposer? responseComposer = null)
    {
        this.planner = planner;
        this.toolbeltClient = toolbeltClient;
        this.responseComposer = responseComposer ?? new HybridResponseComposer();
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

        var collectedResults = new CollectedToolResults();

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
            collectedResults.Apply(result);
            await Emit("tool.result", toolResultPayload);

            if (step.ToolName == ToolNames.DocsSearch &&
                TryCreateDocsGetChunkStep(result.RawJson, out var docsGetChunkStep))
            {
                await Emit(
                    "tool.call",
                    new
                    {
                        toolName = docsGetChunkStep.ToolName,
                        sanitizedArgs = docsGetChunkStep.Args,
                        requiresApproval = false
                    });

                try
                {
                    var docsGetChunkResult = await toolbeltClient.CallAsync(docsGetChunkStep, cancellationToken);
                    collectedResults.Apply(docsGetChunkResult);
                    await Emit("tool.result", ToolResultSummarizer.Summarize(docsGetChunkResult));
                }
                catch (ToolbeltConfigurationException)
                {
                    collectedResults.DocsGetChunkUnavailable = true;
                    await EmitDocsGetChunkUnavailable();
                }
                catch (ToolbeltClientException)
                {
                    collectedResults.DocsGetChunkUnavailable = true;
                    await EmitDocsGetChunkUnavailable();
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    collectedResults.DocsGetChunkUnavailable = true;
                    await EmitDocsGetChunkUnavailable();
                }
            }
        }

        var response = responseComposer.Compose(collectedResults.ToComposerInput());
        await Emit(
            "assistant.message",
            new
            {
                message = response.Message,
                citations = response.Citations,
                summary = response.Summary
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

        Task EmitDocsGetChunkUnavailable() =>
            Emit(
                "tool.result",
                new
                {
                    toolName = ToolNames.DocsGetChunk,
                    success = false,
                    code = "TOOLBELT_CALL_FAILED",
                    message = "Full citation chunk could not be loaded."
                });
    }

    private static bool TryCreateDocsGetChunkStep(JsonElement docsSearchResult, out ToolPlanStep step)
    {
        step = new ToolPlanStep
        {
            ToolName = ToolNames.DocsGetChunk,
            Method = HttpMethods.Post,
            Endpoint = "/api/tools/docs/get-chunk",
            Args = new { }
        };

        if (!docsSearchResult.TryGetProperty("results", out var results) ||
            results.ValueKind != JsonValueKind.Array ||
            results.GetArrayLength() == 0)
        {
            return false;
        }

        var topResult = results[0];
        var chunkId = TryGetString(topResult, "chunkId");
        var citationId = TryGetString(topResult, "citationId");

        if (!string.IsNullOrWhiteSpace(chunkId))
        {
            step = step with
            {
                Args = new DocsGetChunkRequest
                {
                    ChunkId = chunkId
                }
            };
            return true;
        }

        if (!string.IsNullOrWhiteSpace(citationId))
        {
            step = step with
            {
                Args = new DocsGetChunkRequest
                {
                    CitationId = citationId
                }
            };
            return true;
        }

        return false;
    }

    private static string? TryGetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private sealed class CollectedToolResults
    {
        public JsonElement? DocsSearchResult { get; private set; }

        public JsonElement? DocsGetChunkResult { get; private set; }

        public JsonElement? DbSchemaSummaryResult { get; private set; }

        public JsonElement? DbQueryReadonlyResult { get; private set; }

        public bool DocsGetChunkUnavailable { get; set; }

        public void Apply(ToolbeltToolResult result)
        {
            var rawJson = result.RawJson.Clone();
            switch (result.ToolName)
            {
                case ToolNames.DocsSearch:
                    DocsSearchResult = rawJson;
                    break;
                case ToolNames.DocsGetChunk:
                    DocsGetChunkResult = rawJson;
                    break;
                case ToolNames.DbGetSchemaSummary:
                    DbSchemaSummaryResult = rawJson;
                    break;
                case ToolNames.DbQueryReadonly:
                    DbQueryReadonlyResult = rawJson;
                    break;
            }
        }

        public HybridResponseInput ToComposerInput() =>
            new()
            {
                DocsSearchResult = DocsSearchResult,
                DocsGetChunkResult = DocsGetChunkResult,
                DbSchemaSummaryResult = DbSchemaSummaryResult,
                DbQueryReadonlyResult = DbQueryReadonlyResult,
                DocsGetChunkUnavailable = DocsGetChunkUnavailable
            };
    }

    private static class ToolResultSummarizer
    {
        public static object Summarize(ToolbeltToolResult result) =>
            result.ToolName switch
            {
                ToolNames.DocsSearch => SummarizeDocsSearch(result.RawJson),
                ToolNames.DocsGetChunk => SummarizeDocsGetChunk(result.RawJson),
                ToolNames.DbGetSchemaSummary => SummarizeSchemaSummary(result.RawJson),
                ToolNames.DbQueryReadonly => SummarizeDbQueryReadonly(result.RawJson),
                _ => new
                {
                    toolName = result.ToolName,
                    summary = "Tool returned a result."
                }
            };

        private static object SummarizeDocsGetChunk(JsonElement rawJson) =>
            new
            {
                toolName = ToolNames.DocsGetChunk,
                citationId = TryGetString(rawJson, "citationId"),
                sourceName = TryGetString(rawJson, "sourceName"),
                title = TryGetString(rawJson, "title"),
                chunkIndex = TryGetInt(rawJson, "chunkIndex"),
                chunkTextLength = TryGetString(rawJson, "chunkText")?.Length ?? 0
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
