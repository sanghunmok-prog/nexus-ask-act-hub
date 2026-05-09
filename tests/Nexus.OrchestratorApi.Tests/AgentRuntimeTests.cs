using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Nexus.Contracts;
using Nexus.OrchestratorApi.Agent;

namespace Nexus.OrchestratorApi.Tests;

public sealed class AgentRuntimeTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Missing_llm_mode_defaults_to_mock()
    {
        var configuration = new ConfigurationBuilder().Build();

        var planner = ChatPlannerFactory.Create(configuration);

        Assert.IsType<MockChatPlanner>(planner);
        Assert.Equal("mock", ChatPlannerFactory.ResolveMode(configuration));
    }

    [Fact]
    public async Task Mock_planner_returns_deterministic_delayed_shipments_policy_plan()
    {
        var planner = new MockChatPlanner();

        var result = await planner.PlanAsync("Show delayed shipments and cite the relevant policy.");

        Assert.True(result.Succeeded);
        Assert.Collection(
            result.Steps,
            step =>
            {
                Assert.Equal(ToolNames.DocsSearch, step.ToolName);
                Assert.Equal(HttpMethods.Post, step.Method);
                Assert.Equal("/api/tools/docs/search", step.Endpoint);
                var args = Assert.IsType<DocsSearchRequest>(step.Args);
                Assert.Equal("delayed shipping policy escalation carrier", args.Query);
                Assert.Equal(5, args.TopK);
            },
            step =>
            {
                Assert.Equal(ToolNames.DbGetSchemaSummary, step.ToolName);
                Assert.Equal(HttpMethods.Get, step.Method);
                Assert.Equal("/api/tools/db/schema-summary", step.Endpoint);
            },
            step =>
            {
                Assert.Equal(ToolNames.DbQueryReadonly, step.ToolName);
                Assert.Equal(HttpMethods.Post, step.Method);
                Assert.Equal("/api/tools/db/query-readonly", step.Endpoint);
                var args = Assert.IsType<StructuredQuery>(step.Args);
                Assert.Equal("Orders", args.Table);
                Assert.Equal(5, args.Limit);
                Assert.Contains("DelayReason", args.Select);
                var filter = Assert.Single(args.Filters);
                Assert.Equal("Status", filter.Column);
                Assert.Equal("eq", filter.Op);
                Assert.Equal("Delayed", filter.Value);
            });
    }

    [Fact]
    public async Task Mock_planner_does_not_add_dynamic_last_30_days_filter()
    {
        var planner = new MockChatPlanner();

        var result = await planner.PlanAsync("Show delayed orders and the relevant shipping delay policy.");
        var query = Assert.IsType<StructuredQuery>(result.Steps.Single(step => step.ToolName == ToolNames.DbQueryReadonly).Args);

        Assert.DoesNotContain(query.Filters, filter => filter.Column == "ExpectedShipDateUtc");
        Assert.DoesNotContain(query.Filters, filter => filter.Op == "between");
        Assert.DoesNotContain("30", JsonSerializer.Serialize(query, JsonOptions));
    }

    [Fact]
    public async Task Agent_runtime_emits_expected_success_sequence_and_tool_results()
    {
        var emitted = await RunAsync(new FakeToolbeltClient());

        Assert.Equal(
            [
                "workflow.started",
                "tool.call",
                "tool.result",
                "tool.call",
                "tool.result",
                "tool.call",
                "tool.result",
                "tool.call",
                "tool.result",
                "assistant.message",
                "done"
            ],
            emitted.Select(envelope => envelope.EventType).ToArray());

        Assert.Contains(emitted, envelope => envelope.EventType == "tool.call" && PayloadString(envelope).Contains("\"toolName\":\"docs.search\""));
        Assert.Contains(emitted, envelope => envelope.EventType == "tool.result" && PayloadString(envelope).Contains("\"toolName\":\"docs.search\"") && PayloadString(envelope).Contains("\"resultCount\":1"));
        Assert.Contains(emitted, envelope => envelope.EventType == "tool.call" && PayloadString(envelope).Contains("\"toolName\":\"docs.get_chunk\""));
        Assert.Contains(emitted, envelope => envelope.EventType == "tool.result" && PayloadString(envelope).Contains("\"toolName\":\"docs.get_chunk\"") && PayloadString(envelope).Contains("\"chunkTextLength\""));
        Assert.Contains(emitted, envelope => envelope.EventType == "tool.call" && PayloadString(envelope).Contains("\"toolName\":\"db.get_schema_summary\""));
        Assert.Contains(emitted, envelope => envelope.EventType == "tool.result" && PayloadString(envelope).Contains("\"toolName\":\"db.get_schema_summary\"") && PayloadString(envelope).Contains("\"tableCount\":1"));
        Assert.Contains(emitted, envelope => envelope.EventType == "tool.call" && PayloadString(envelope).Contains("\"toolName\":\"db.query_readonly\""));
        Assert.Contains(emitted, envelope => envelope.EventType == "tool.result" && PayloadString(envelope).Contains("\"toolName\":\"db.query_readonly\"") && PayloadString(envelope).Contains("\"rowCount\":5"));
        Assert.Contains(emitted, envelope => envelope.EventType == "assistant.message" && PayloadString(envelope).Contains("## Delayed orders"));
        Assert.Contains(emitted, envelope => envelope.EventType == "assistant.message" && PayloadString(envelope).Contains("## Relevant policy"));
        Assert.Contains(emitted, envelope => envelope.EventType == "assistant.message" && PayloadString(envelope).Contains("\"citationCount\":1"));
        Assert.DoesNotContain(emitted, envelope => envelope.EventType == "assistant.message" && PayloadString(envelope).Contains("Hybrid answer composition will be added in PR-12"));
        Assert.DoesNotContain(emitted, envelope => envelope.EventType == "assistant.message" && PayloadString(envelope).Contains("last 30 days", StringComparison.OrdinalIgnoreCase));

        var done = emitted.Last();
        Assert.Equal("done", done.EventType);
        Assert.True(Payload(done).GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Agent_runtime_does_not_call_docs_get_chunk_when_docs_search_has_zero_results()
    {
        var emitted = await RunAsync(new FakeToolbeltClient(documentResultCount: 0));

        Assert.DoesNotContain(emitted, envelope => envelope.EventType == "tool.call" && PayloadString(envelope).Contains("\"toolName\":\"docs.get_chunk\""));
        Assert.Contains(emitted, envelope => envelope.EventType == "assistant.message" && PayloadString(envelope).Contains("No relevant policy document was found."));
        Assert.Equal("done", emitted.Last().EventType);
        Assert.True(Payload(emitted.Last()).GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Agent_runtime_uses_search_snippet_when_docs_get_chunk_fails()
    {
        var emitted = await RunAsync(new FakeToolbeltClient(failDocsGetChunk: true));

        Assert.Contains(emitted, envelope => envelope.EventType == "tool.call" && PayloadString(envelope).Contains("\"toolName\":\"docs.get_chunk\""));
        Assert.Contains(emitted, envelope => envelope.EventType == "tool.result" && PayloadString(envelope).Contains("\"toolName\":\"docs.get_chunk\"") && PayloadString(envelope).Contains("\"success\":false"));
        Assert.Contains(emitted, envelope => envelope.EventType == "assistant.message" && PayloadString(envelope).Contains("Escalate delayed carrier shipments."));
        Assert.Contains(emitted, envelope => envelope.EventType == "assistant.message" && PayloadString(envelope).Contains("full citation text was unavailable"));
        Assert.Equal("done", emitted.Last().EventType);
        Assert.True(Payload(emitted.Last()).GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Toolbelt_failure_emits_sanitized_error_and_done()
    {
        var emitted = await RunAsync(new FailingToolbeltClient());

        Assert.Contains(emitted, envelope => envelope.EventType == "workflow.started");
        Assert.Contains(emitted, envelope => envelope.EventType == "tool.call");

        var error = Assert.Single(emitted, envelope => envelope.EventType == "error");
        var errorPayload = Payload(error);
        Assert.Equal("TOOLBELT_CALL_FAILED", errorPayload.GetProperty("code").GetString());
        Assert.Equal("Toolbelt call failed.", errorPayload.GetProperty("message").GetString());
        Assert.False(errorPayload.GetProperty("retryable").GetBoolean());
        Assert.DoesNotContain("database password", PayloadString(error), StringComparison.OrdinalIgnoreCase);

        var done = emitted.Last();
        Assert.Equal("done", done.EventType);
        Assert.False(Payload(done).GetProperty("success").GetBoolean());
    }

    private static async Task<IReadOnlyList<SseEnvelope>> RunAsync(IToolbeltClient toolbeltClient)
    {
        var runtime = new AgentRuntime(new MockChatPlanner(), toolbeltClient);
        var emitted = new List<SseEnvelope>();

        await runtime.RunAsync(
            "Show delayed shipments and cite the relevant policy.",
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            (envelope, _) =>
            {
                emitted.Add(envelope);
                return Task.CompletedTask;
            });

        return emitted;
    }

    private static JsonElement Payload(SseEnvelope envelope) =>
        JsonSerializer.SerializeToElement(envelope.Payload, JsonOptions);

    private static string PayloadString(SseEnvelope envelope) =>
        Payload(envelope).GetRawText();

    private sealed class FakeToolbeltClient : IToolbeltClient
    {
        private readonly int documentResultCount;
        private readonly bool failDocsGetChunk;

        public FakeToolbeltClient(int documentResultCount = 1, bool failDocsGetChunk = false)
        {
            this.documentResultCount = documentResultCount;
            this.failDocsGetChunk = failDocsGetChunk;
        }

        public Task<ToolbeltToolResult> CallAsync(ToolPlanStep step, CancellationToken cancellationToken = default)
        {
            if (step.ToolName == ToolNames.DocsGetChunk && failDocsGetChunk)
            {
                throw new ToolbeltClientException(step.ToolName, System.Net.HttpStatusCode.InternalServerError, "chunk unavailable");
            }

            object result = step.ToolName switch
            {
                ToolNames.DocsSearch => documentResultCount == 0
                    ? new
                    {
                        query = "delayed shipping policy escalation carrier",
                        topK = 5,
                        resultCount = 0,
                        results = Array.Empty<object>()
                    }
                    : new
                {
                    query = "delayed shipping policy escalation carrier",
                    topK = 5,
                    resultCount = 1,
                    results = new[]
                    {
                        new
                        {
                            citationId = "doc-1:0",
                            docId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                            chunkId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                            chunkIndex = 0,
                            title = "Shipping Delay Policy",
                            sourceName = "shipping-policy.md",
                            snippet = "Escalate delayed carrier shipments.",
                            distance = 0.1
                        }
                    }
                },
                ToolNames.DocsGetChunk => new
                {
                    citationId = "doc-1:0",
                    docId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    chunkId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    chunkIndex = 0,
                    title = "Shipping Delay Policy",
                    sourceName = "shipping-policy.md",
                    chunkText = "Escalate delayed carrier shipments when the policy threshold is met.",
                    metadata = new
                    {
                        charStart = 0,
                        charEnd = 65
                    }
                },
                ToolNames.DbGetSchemaSummary => new
                {
                    tables = new[]
                    {
                        new
                        {
                            name = "Orders",
                            columns = new[] { "OrderId", "Status", "ExpectedShipDateUtc" }
                        }
                    }
                },
                ToolNames.DbQueryReadonly => new
                {
                    rowCount = 5,
                    rows = Enumerable.Range(1, 5)
                        .Select(index => new Dictionary<string, object?>
                        {
                            ["OrderId"] = index,
                            ["Status"] = "Delayed",
                            ["Carrier"] = "CarrierCo"
                        })
                        .ToArray()
                },
                _ => new { }
            };

            return Task.FromResult(new ToolbeltToolResult(step.ToolName, JsonSerializer.SerializeToElement(result, JsonOptions)));
        }
    }

    private sealed class FailingToolbeltClient : IToolbeltClient
    {
        public Task<ToolbeltToolResult> CallAsync(ToolPlanStep step, CancellationToken cancellationToken = default) =>
            throw new ToolbeltClientException(step.ToolName, System.Net.HttpStatusCode.InternalServerError, "database password leaked internally");
    }
}
