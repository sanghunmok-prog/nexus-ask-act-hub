using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Nexus.Contracts;
using Nexus.OrchestratorApi.Agent;
using Nexus.OrchestratorApi.Approvals;

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

    [Fact]
    public async Task Recoverable_db_query_validation_error_retries_once_with_corrected_query()
    {
        var toolbeltClient = new RecoverableDbQueryFailureToolbeltClient(failRetry: false);
        var emitted = await RunAsync(
            toolbeltClient,
            prompt: "Show delayed shipments and cite the relevant policy with correction retry.");

        Assert.Equal(2, toolbeltClient.DbQueryAttempts);
        Assert.Equal("ExpectedShipDate", toolbeltClient.DbQueries[0].Select[2]);
        Assert.Equal("ExpectedShipDateUtc", toolbeltClient.DbQueries[1].Select[2]);
        Assert.Equal("ExpectedShipDateUtc", Assert.Single(toolbeltClient.DbQueries[1].OrderBy).Column);

        var retry = Assert.Single(emitted, envelope => envelope.EventType == "tool.retry");
        var retryPayload = Payload(retry);
        Assert.Equal(ToolNames.DbQueryReadonly, retryPayload.GetProperty("toolName").GetString());
        Assert.Equal(2, retryPayload.GetProperty("attempt").GetInt32());
        Assert.Equal(2, retryPayload.GetProperty("maxAttempts").GetInt32());
        Assert.Equal("schema_correction", retryPayload.GetProperty("reason").GetString());

        Assert.Contains(emitted, envelope => envelope.EventType == "assistant.message" && PayloadString(envelope).Contains("## Delayed orders"));
        Assert.Equal("done", emitted.Last().EventType);
        Assert.True(Payload(emitted.Last()).GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Retry_budget_prevents_third_db_query_attempt_when_correction_fails()
    {
        var toolbeltClient = new RecoverableDbQueryFailureToolbeltClient(failRetry: true);
        var emitted = await RunAsync(
            toolbeltClient,
            prompt: "Show delayed shipments and cite the relevant policy with correction retry.");

        Assert.Equal(2, toolbeltClient.DbQueryAttempts);
        Assert.Single(emitted, envelope => envelope.EventType == "tool.retry");
        Assert.DoesNotContain(emitted, envelope => envelope.EventType == "assistant.message");

        var error = Assert.Single(emitted, envelope => envelope.EventType == "error");
        Assert.Equal("DB_QUERY_CORRECTION_FAILED", Payload(error).GetProperty("code").GetString());
        Assert.DoesNotContain("SELECT *", PayloadString(error), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("database password", PayloadString(error), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connection string", PayloadString(error), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack", PayloadString(error), StringComparison.OrdinalIgnoreCase);

        Assert.Equal("done", emitted.Last().EventType);
        Assert.False(Payload(emitted.Last()).GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Non_recoverable_db_query_failure_does_not_retry()
    {
        var toolbeltClient = new NonRecoverableDbQueryFailureToolbeltClient();
        var emitted = await RunAsync(toolbeltClient);

        Assert.Equal(1, toolbeltClient.DbQueryAttempts);
        Assert.DoesNotContain(emitted, envelope => envelope.EventType == "tool.retry");

        var error = Assert.Single(emitted, envelope => envelope.EventType == "error");
        Assert.Equal("TOOLBELT_CALL_FAILED", Payload(error).GetProperty("code").GetString());
        Assert.Equal("done", emitted.Last().EventType);
        Assert.False(Payload(emitted.Last()).GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Retry_trace_does_not_expose_raw_sql_secrets_or_stack_traces()
    {
        var toolbeltClient = new RecoverableDbQueryFailureToolbeltClient(failRetry: true);
        var emitted = await RunAsync(
            toolbeltClient,
            prompt: "Show delayed shipments and cite the relevant policy with correction retry.");

        var trace = string.Join(Environment.NewLine, emitted.Select(PayloadString));
        Assert.DoesNotContain("SELECT *", trace, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("database password", trace, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connection string", trace, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack trace", trace, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("at Nexus.", trace, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Action_prompt_creates_approval_checkpoint_and_emits_approval_events()
    {
        var repository = new FakeApprovalRepository();
        var toolbeltClient = new FakeToolbeltClient();
        var emitted = await RunAsync(
            toolbeltClient,
            CreateApprovalService(repository),
            "Create a GitHub issue for the delayed shipment findings.",
            "reviewer-1");

        Assert.Equal(
            [
                "workflow.started",
                "tool.call",
                "checkpoint.saved",
                "approval.required",
                "assistant.message",
                "done"
            ],
            emitted.Select(envelope => envelope.EventType).ToArray());

        Assert.NotNull(repository.CreatedApproval);
        Assert.NotNull(repository.CreatedCheckpoint);
        Assert.Equal(ApprovalStatuses.Pending, repository.CreatedApproval.Status);
        Assert.Equal(CheckpointStatuses.WaitingApproval, repository.CreatedCheckpoint.Status);
        Assert.Equal("reviewer-1", repository.CreatedApproval.RequestedByUserId);
        Assert.Equal(ApprovalIntentFactory.GitHubCreateIssueToolName, repository.CreatedApproval.ToolName);
        Assert.Equal(repository.CreatedApproval.CorrelationId, repository.CreatedCheckpoint.CorrelationId);
        Assert.Equal(repository.CreatedApproval.ParamsHash, ApprovalJson.ComputeParamsHash(repository.CreatedApproval.ParamsJson));

        Assert.Contains(emitted, envelope => envelope.EventType == "tool.call" && PayloadString(envelope).Contains("\"toolName\":\"github.create_issue\"") && PayloadString(envelope).Contains("\"requiresApproval\":true"));
        Assert.Contains(emitted, envelope => envelope.EventType == "checkpoint.saved" && PayloadString(envelope).Contains("\"status\":\"WaitingApproval\""));
        Assert.Contains(emitted, envelope => envelope.EventType == "approval.required" && PayloadString(envelope).Contains("\"toolName\":\"github.create_issue\"") && PayloadString(envelope).Contains("\"repo\":\"sanghunmok-prog/nexus-ask-act-hub\""));
        Assert.Contains(emitted, envelope => envelope.EventType == "assistant.message" && PayloadString(envelope).Contains("No external action has been executed."));
        Assert.DoesNotContain(emitted, envelope => PayloadString(envelope).Contains("ReadyToResume"));
        Assert.Equal(0, toolbeltClient.CallCount);

        var done = emitted.Last();
        Assert.Equal("done", done.EventType);
        Assert.True(Payload(done).GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Non_action_prompt_still_uses_read_path_when_approval_service_is_configured()
    {
        var repository = new FakeApprovalRepository();
        var toolbeltClient = new FakeToolbeltClient();
        var emitted = await RunAsync(
            toolbeltClient,
            CreateApprovalService(repository),
            "Show delayed shipments and cite the relevant policy.");

        Assert.Contains(emitted, envelope => envelope.EventType == "tool.call" && PayloadString(envelope).Contains("\"toolName\":\"docs.search\""));
        Assert.Contains(emitted, envelope => envelope.EventType == "assistant.message" && PayloadString(envelope).Contains("## Delayed orders"));
        Assert.Null(repository.CreatedApproval);
        Assert.True(toolbeltClient.CallCount > 0);
    }

    private static async Task<IReadOnlyList<SseEnvelope>> RunAsync(
        IToolbeltClient toolbeltClient,
        ApprovalService? approvalService = null,
        string prompt = "Show delayed shipments and cite the relevant policy.",
        string requestedByUserId = "demo-user")
    {
        var runtime = new AgentRuntime(new MockChatPlanner(), toolbeltClient, approvalService: approvalService);
        var emitted = new List<SseEnvelope>();

        await runtime.RunAsync(
            prompt,
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            (envelope, _) =>
            {
                emitted.Add(envelope);
                return Task.CompletedTask;
            },
            requestedByUserId);

        return emitted;
    }

    private static ApprovalService CreateApprovalService(FakeApprovalRepository repository)
    {
        var configuration = new ConfigurationBuilder().Build();
        return new ApprovalService(
            repository,
            new ApprovalIntentFactory(configuration, new TestHostEnvironment()),
            new FakeToolbeltClient(),
            NullLogger<ApprovalService>.Instance);
    }

    private static JsonElement Payload(SseEnvelope envelope) =>
        JsonSerializer.SerializeToElement(envelope.Payload, JsonOptions);

    private static string PayloadString(SseEnvelope envelope) =>
        Payload(envelope).GetRawText();

    private sealed class FakeToolbeltClient : IToolbeltClient, IToolbeltWriteClient
    {
        private readonly int documentResultCount;
        private readonly bool failDocsGetChunk;

        public FakeToolbeltClient(int documentResultCount = 1, bool failDocsGetChunk = false)
        {
            this.documentResultCount = documentResultCount;
            this.failDocsGetChunk = failDocsGetChunk;
        }

        public int CallCount { get; private set; }

        public Task<ToolbeltToolResult> CallAsync(ToolPlanStep step, CancellationToken cancellationToken = default)
        {
            CallCount++;

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

    private sealed class RecoverableDbQueryFailureToolbeltClient : IToolbeltClient
    {
        private readonly bool failRetry;
        private readonly FakeToolbeltClient inner = new();

        public RecoverableDbQueryFailureToolbeltClient(bool failRetry)
        {
            this.failRetry = failRetry;
        }

        public int DbQueryAttempts { get; private set; }

        public List<StructuredQuery> DbQueries { get; } = [];

        public Task<ToolbeltToolResult> CallAsync(ToolPlanStep step, CancellationToken cancellationToken = default)
        {
            if (step.ToolName != ToolNames.DbQueryReadonly)
            {
                return inner.CallAsync(step, cancellationToken);
            }

            DbQueryAttempts++;
            var query = Assert.IsType<StructuredQuery>(step.Args);
            DbQueries.Add(query);

            if (DbQueryAttempts == 1 || failRetry)
            {
                throw new ToolbeltClientException(
                    step.ToolName,
                    System.Net.HttpStatusCode.BadRequest,
                    "Toolbelt returned an unsuccessful status code.",
                    "QUERY_VALIDATION_FAILED",
                    "StructuredQuery failed validation.",
                    ["Select column 'ExpectedShipDate' is not allowlisted. SELECT * with database password should not leak."]);
            }

            return inner.CallAsync(step, cancellationToken);
        }
    }

    private sealed class NonRecoverableDbQueryFailureToolbeltClient : IToolbeltClient
    {
        private readonly FakeToolbeltClient inner = new();

        public int DbQueryAttempts { get; private set; }

        public Task<ToolbeltToolResult> CallAsync(ToolPlanStep step, CancellationToken cancellationToken = default)
        {
            if (step.ToolName != ToolNames.DbQueryReadonly)
            {
                return inner.CallAsync(step, cancellationToken);
            }

            DbQueryAttempts++;
            throw new ToolbeltClientException(
                step.ToolName,
                System.Net.HttpStatusCode.InternalServerError,
                "Toolbelt returned an unsuccessful status code.",
                "SQL_CONNECTION_NOT_CONFIGURED",
                "SQL connection string is not configured.");
        }
    }

    private sealed class FakeApprovalRepository : IApprovalRepository
    {
        public ApprovalRequestRecord? CreatedApproval { get; private set; }

        public AgentCheckpointRecord? CreatedCheckpoint { get; private set; }

        public Task<ApprovalCreateResult> CreateApprovalWithCheckpointAsync(
            ApprovalCreateRequest request,
            PendingGithubIssueArgs args,
            CancellationToken cancellationToken = default)
        {
            CreatedApproval = request.Approval;
            CreatedCheckpoint = request.Checkpoint;
            return Task.FromResult(new ApprovalCreateResult
            {
                Approval = request.Approval,
                Checkpoint = request.Checkpoint,
                Args = args
            });
        }

        public Task<IReadOnlyList<ApprovalRequestRecord>> GetPendingApprovalsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ApprovalRequestRecord>>([]);

        public Task<IReadOnlyList<ReadyApprovalRecord>> GetReadyApprovalsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ReadyApprovalRecord>>([]);

        public Task<ApprovalRequestRecord?> GetApprovalAsync(Guid approvalId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ApprovalRequestRecord?>(null);

        public Task<ReadyApprovalRecord?> GetApprovalWithCheckpointAsync(Guid approvalId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ReadyApprovalRecord?>(null);

        public Task<bool> ApproveAsync(
            Guid approvalId,
            Guid correlationId,
            DateTime approvedAtUtc,
            string approvedByUserId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> RejectAsync(Guid approvalId, Guid correlationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> TryStartExecutionAsync(Guid checkpointId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task CompleteExecutionAsync(Guid checkpointId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task FailExecutionAsync(Guid checkpointId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "Nexus.OrchestratorApi.Tests";

        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
