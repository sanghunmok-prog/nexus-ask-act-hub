using Nexus.Contracts;

namespace Nexus.OrchestratorApi.Agent;

public interface IChatPlanner
{
    Task<PlannerResult> PlanAsync(string prompt, CancellationToken cancellationToken = default);
}

public sealed record PlannerResult
{
    public bool Succeeded { get; init; }

    public IReadOnlyList<ToolPlanStep> Steps { get; init; } = [];

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public static PlannerResult Success(IReadOnlyList<ToolPlanStep> steps) =>
        new()
        {
            Succeeded = true,
            Steps = steps
        };

    public static PlannerResult Failure(string errorCode, string errorMessage) =>
        new()
        {
            Succeeded = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };
}

public sealed record ToolPlanStep
{
    public required string ToolName { get; init; }

    public required string Method { get; init; }

    public required string Endpoint { get; init; }

    public object Args { get; init; } = new();
}

public sealed class MockChatPlanner : IChatPlanner
{
    public Task<PlannerResult> PlanAsync(string prompt, CancellationToken cancellationToken = default) =>
        Task.FromResult(PlannerResult.Success(DemoDelayedShipmentsPolicyPlan()));

    private static IReadOnlyList<ToolPlanStep> DemoDelayedShipmentsPolicyPlan() =>
    [
        new ToolPlanStep
        {
            ToolName = ToolNames.DocsSearch,
            Method = HttpMethods.Post,
            Endpoint = "/api/tools/docs/search",
            Args = new DocsSearchRequest
            {
                Query = "delayed shipping policy escalation carrier",
                TopK = 5
            }
        },
        new ToolPlanStep
        {
            ToolName = ToolNames.DbGetSchemaSummary,
            Method = HttpMethods.Get,
            Endpoint = "/api/tools/db/schema-summary",
            Args = new { }
        },
        new ToolPlanStep
        {
            ToolName = ToolNames.DbQueryReadonly,
            Method = HttpMethods.Post,
            Endpoint = "/api/tools/db/query-readonly",
            Args = new StructuredQuery
            {
                Table = "Orders",
                Select =
                [
                    "OrderId",
                    "Status",
                    "ExpectedShipDateUtc",
                    "ActualShipDateUtc",
                    "Carrier",
                    "DelayReason"
                ],
                Filters =
                [
                    new StructuredQueryFilter
                    {
                        Column = "Status",
                        Op = "eq",
                        Value = "Delayed"
                    }
                ],
                OrderBy =
                [
                    new StructuredQueryOrderBy
                    {
                        Column = "ExpectedShipDateUtc",
                        Dir = "desc"
                    }
                ],
                Limit = 5
            }
        }
    ];
}

public sealed class LivePlannerNotConfigured : IChatPlanner
{
    public Task<PlannerResult> PlanAsync(string prompt, CancellationToken cancellationToken = default) =>
        Task.FromResult(PlannerResult.Failure(
            "LIVE_PLANNER_NOT_CONFIGURED",
            "Live planner mode is not configured for this environment."));
}

public sealed class UnsupportedLlmModePlanner : IChatPlanner
{
    public Task<PlannerResult> PlanAsync(string prompt, CancellationToken cancellationToken = default) =>
        Task.FromResult(PlannerResult.Failure(
            "UNSUPPORTED_LLM_MODE",
            "Unsupported LLM_MODE. Supported modes are 'mock' and 'live'."));
}

public static class ChatPlannerFactory
{
    public static IChatPlanner Create(IConfiguration configuration)
    {
        var mode = ResolveMode(configuration);

        return mode switch
        {
            "mock" => new MockChatPlanner(),
            "live" => new LivePlannerNotConfigured(),
            _ => new UnsupportedLlmModePlanner()
        };
    }

    public static string ResolveMode(IConfiguration configuration) =>
        (configuration["LLM_MODE"] ?? configuration["LlmMode"] ?? "mock").Trim().ToLowerInvariant();
}

public static class ToolNames
{
    public const string DocsSearch = "docs.search";
    public const string DocsGetChunk = "docs.get_chunk";
    public const string DbGetSchemaSummary = "db.get_schema_summary";
    public const string DbQueryReadonly = "db.query_readonly";
}
