using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Nexus.OrchestratorApi.Approvals;

public static class ApprovalStatuses
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}

public static class CheckpointStatuses
{
    public const string WaitingApproval = "WaitingApproval";
    public const string ReadyToResume = "ReadyToResume";
    public const string Failed = "Failed";
}

public sealed record PendingGithubIssueArgs
{
    public required string Repo { get; init; }

    public required string Title { get; init; }

    public required string Body { get; init; }

    public IReadOnlyList<string> Labels { get; init; } = [];
}

public sealed record PendingActionEnvelope
{
    public required string ToolName { get; init; }

    public required PendingGithubIssueArgs Args { get; init; }

    public required Guid ApprovalId { get; init; }
}

public sealed record ApprovalRequestRecord
{
    public required Guid ApprovalId { get; init; }

    public required Guid CorrelationId { get; init; }

    public required DateTime RequestedAtUtc { get; init; }

    public required string RequestedByUserId { get; init; }

    public required string Status { get; init; }

    public required string ToolName { get; init; }

    public required string ParamsHash { get; init; }

    public required string ParamsJson { get; init; }

    public required string RiskSummary { get; init; }

    public DateTime? ApprovedAtUtc { get; init; }

    public string? ApprovedByUserId { get; init; }
}

public sealed record AgentCheckpointRecord
{
    public required Guid CheckpointId { get; init; }

    public required Guid CorrelationId { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    public required string Status { get; init; }

    public required string ConversationSummary { get; init; }

    public string? PendingActionJson { get; init; }

    public Guid? LastToolCallId { get; init; }
}

public sealed record ApprovalCreateRequest
{
    public required ApprovalRequestRecord Approval { get; init; }

    public required AgentCheckpointRecord Checkpoint { get; init; }
}

public sealed record ApprovalCreateResult
{
    public required ApprovalRequestRecord Approval { get; init; }

    public required AgentCheckpointRecord Checkpoint { get; init; }

    public required PendingGithubIssueArgs Args { get; init; }
}

public sealed record PendingApprovalDto
{
    public required Guid ApprovalId { get; init; }

    public required Guid CorrelationId { get; init; }

    public required DateTime RequestedAtUtc { get; init; }

    public required string RequestedByUserId { get; init; }

    public required string Status { get; init; }

    public required string ToolName { get; init; }

    public required string ParamsHash { get; init; }

    public required ApprovalPublicParams Params { get; init; }

    public required string RiskSummary { get; init; }
}

public sealed record ApprovalPublicParams
{
    public required string Repo { get; init; }

    public required string Title { get; init; }

    public IReadOnlyList<string> Labels { get; init; } = [];
}

public sealed record PendingApprovalsResponse
{
    public IReadOnlyList<PendingApprovalDto> Approvals { get; init; } = [];
}

public sealed record ApprovalDecisionResponse
{
    public required Guid ApprovalId { get; init; }

    public required string Status { get; init; }

    public required string CheckpointStatus { get; init; }

    public required bool ResumeAvailable { get; init; }

    public required string Message { get; init; }
}

public sealed record ApprovalErrorResponse
{
    public required string Code { get; init; }

    public required string Message { get; init; }

    public IReadOnlyList<string> Errors { get; init; } = [];
}

public static class ApprovalJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string SerializeDeterministic<T>(T value) =>
        JsonSerializer.Serialize(value, Options);

    public static PendingGithubIssueArgs DeserializeArgs(string json) =>
        JsonSerializer.Deserialize<PendingGithubIssueArgs>(json, Options) ??
        new PendingGithubIssueArgs
        {
            Repo = string.Empty,
            Title = string.Empty,
            Body = string.Empty,
            Labels = []
        };

    public static string ComputeParamsHash(string paramsJson)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(paramsJson));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
