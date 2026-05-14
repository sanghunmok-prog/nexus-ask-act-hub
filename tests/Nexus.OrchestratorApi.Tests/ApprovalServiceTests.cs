using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Nexus.OrchestratorApi.Agent;
using Nexus.OrchestratorApi.Approvals;

namespace Nexus.OrchestratorApi.Tests;

public sealed class ApprovalServiceTests
{
    [Fact]
    public async Task Pending_approvals_are_returned()
    {
        var approval = CreateApproval(status: ApprovalStatuses.Pending);
        var repository = new FakeApprovalRepository();
        repository.Approvals[approval.ApprovalId] = approval;

        var response = await CreateService(repository).GetPendingApprovalsAsync();

        var pending = Assert.Single(response.Approvals);
        Assert.Equal(approval.ApprovalId, pending.ApprovalId);
        Assert.Equal(ApprovalStatuses.Pending, pending.Status);
        Assert.Equal("github.create_issue", pending.ToolName);
        Assert.Equal("sanghunmok-prog/nexus-ask-act-hub", pending.Params.Repo);
        Assert.Equal("nexus-demo", Assert.Single(pending.Params.Labels));
    }

    [Fact]
    public async Task Approve_transitions_pending_to_approved()
    {
        var approval = CreateApproval(status: ApprovalStatuses.Pending);
        var checkpoint = CreateCheckpoint(approval.CorrelationId, CheckpointStatuses.WaitingApproval);
        var repository = new FakeApprovalRepository();
        repository.Approvals[approval.ApprovalId] = approval;
        repository.Checkpoints[checkpoint.CheckpointId] = checkpoint;

        var toolbelt = new FakeToolbeltClient();
        var result = await CreateService(repository, toolbelt).ApproveAsync(approval.ApprovalId, "approver-1");

        Assert.True(result.Succeeded);
        Assert.Equal(ApprovalStatuses.Approved, result.Response?.Status);
        Assert.Equal(CheckpointStatuses.ReadyToResume, result.Response?.CheckpointStatus);
        Assert.True(result.Response?.ResumeAvailable);
        Assert.Contains("ready to execute", result.Response?.Message);
        Assert.Contains("No external action has been executed yet", result.Response?.Message);
        Assert.Equal(ApprovalStatuses.Approved, repository.Approvals[approval.ApprovalId].Status);
        Assert.Equal("approver-1", repository.Approvals[approval.ApprovalId].ApprovedByUserId);
        Assert.Equal(CheckpointStatuses.ReadyToResume, repository.Checkpoints[checkpoint.CheckpointId].Status);
        Assert.Equal(0, toolbelt.CallCount);
    }

    [Fact]
    public async Task Ready_approvals_are_returned()
    {
        var approval = CreateApproval(status: ApprovalStatuses.Approved);
        var checkpoint = CreateCheckpoint(
            approval.CorrelationId,
            CheckpointStatuses.ReadyToResume,
            CreatePendingActionJson(approval.ApprovalId));
        var repository = new FakeApprovalRepository();
        repository.Approvals[approval.ApprovalId] = approval;
        repository.Checkpoints[checkpoint.CheckpointId] = checkpoint;

        var response = await CreateService(repository).GetReadyApprovalsAsync();

        var ready = Assert.Single(response.Approvals);
        Assert.Equal(approval.ApprovalId, ready.ApprovalId);
        Assert.Equal(checkpoint.CheckpointId, ready.CheckpointId);
        Assert.Equal(CheckpointStatuses.ReadyToResume, ready.CheckpointStatus);
        Assert.True(ready.ExecutionAvailable);
        Assert.Equal("sanghunmok-prog/nexus-ask-act-hub", ready.Params.Repo);
        Assert.Equal("Delayed shipments review", ready.Params.Title);
        Assert.Equal("Review delayed shipment findings from NEXUS. Approval is required before this issue is created.", ready.Params.Body);
        Assert.Equal("nexus-demo", Assert.Single(ready.Params.Labels));
    }

    [Fact]
    public void Ready_approval_sql_uses_safe_aliases()
    {
        const System.Reflection.BindingFlags Flags =
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;

        var readyApprovalsSql = Assert.IsType<string>(
            typeof(SqlApprovalRepository).GetField("ReadyApprovalsSql", Flags)?.GetValue(null));
        var readyApprovalByIdSql = Assert.IsType<string>(
            typeof(SqlApprovalRepository).GetField("ReadyApprovalByIdSql", Flags)?.GetValue(null));

        Assert.Null(typeof(SqlApprovalRepository).GetField("ReadyApprovalSelectSql", Flags));
        Assert.Contains("SELECT TOP (50)", readyApprovalsSql);
        Assert.Contains("FROM dbo.ApprovalRequest AS ar", readyApprovalsSql);
        Assert.Contains("INNER JOIN dbo.AgentCheckpoint AS cp", readyApprovalsSql);
        Assert.Contains("WHERE ar.Status = @approvalStatus", readyApprovalsSql);
        Assert.Contains("AND ar.ToolName = @toolName", readyApprovalsSql);
        Assert.Contains("ORDER BY ar.ApprovedAtUtc DESC, cp.CreatedAtUtc DESC", readyApprovalsSql);
        Assert.Contains("SELECT TOP (1)", readyApprovalByIdSql);
        Assert.Contains("FROM dbo.ApprovalRequest AS ar", readyApprovalByIdSql);
        Assert.Contains("INNER JOIN dbo.AgentCheckpoint AS cp", readyApprovalByIdSql);
        Assert.Contains("WHERE ar.ApprovalId = @approvalId", readyApprovalByIdSql);
        Assert.DoesNotMatch("(?i)\\bAS\\s+approval\\b", readyApprovalsSql);
        Assert.DoesNotMatch("(?i)\\bAS\\s+checkpoint\\b", readyApprovalsSql);
        Assert.DoesNotContain("approval.", readyApprovalsSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("checkpoint.", readyApprovalsSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CorrelationIdWHERE", readyApprovalsSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CorrelationIdWHERE", readyApprovalByIdSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WHERE approval.ApprovalId", readyApprovalByIdSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Ready_approvals_filter_to_approved_ready_github_issue_only_without_toolbelt_call()
    {
        var repository = new FakeApprovalRepository();
        var toolbelt = new FakeToolbeltClient();
        var readyApproval = CreateApproval(status: ApprovalStatuses.Approved);
        AddApprovalWithCheckpoint(repository, readyApproval, CheckpointStatuses.ReadyToResume);
        AddApprovalWithCheckpoint(repository, CreateApproval(status: ApprovalStatuses.Pending), CheckpointStatuses.ReadyToResume);
        AddApprovalWithCheckpoint(repository, CreateApproval(status: ApprovalStatuses.Rejected), CheckpointStatuses.ReadyToResume);
        AddApprovalWithCheckpoint(repository, CreateApproval(status: ApprovalStatuses.Approved), CheckpointStatuses.Failed);
        AddApprovalWithCheckpoint(repository, CreateApproval(status: ApprovalStatuses.Approved), CheckpointStatuses.Completed);
        AddApprovalWithCheckpoint(repository, CreateApproval(status: ApprovalStatuses.Approved), CheckpointStatuses.Executing);
        AddApprovalWithCheckpoint(repository, CreateApproval(status: ApprovalStatuses.Approved) with
        {
            ToolName = "unknown.tool"
        }, CheckpointStatuses.ReadyToResume);

        var response = await CreateService(repository, toolbelt).GetReadyApprovalsAsync();

        var ready = Assert.Single(response.Approvals);
        Assert.Equal(readyApproval.ApprovalId, ready.ApprovalId);
        Assert.Equal(0, toolbelt.CallCount);
    }

    [Fact]
    public async Task Ready_approvals_parse_params_from_approval_params_json_not_pending_action_wrapper()
    {
        var approval = CreateApproval(status: ApprovalStatuses.Approved);
        var checkpoint = CreateCheckpoint(
            approval.CorrelationId,
            CheckpointStatuses.ReadyToResume,
            CreatePendingActionJson(approval.ApprovalId));
        var repository = new FakeApprovalRepository();
        repository.Approvals[approval.ApprovalId] = approval;
        repository.Checkpoints[checkpoint.CheckpointId] = checkpoint;

        var response = await CreateService(repository).GetReadyApprovalsAsync();

        var ready = Assert.Single(response.Approvals);
        Assert.Equal("sanghunmok-prog/nexus-ask-act-hub", ready.Params.Repo);
        Assert.NotEqual("owner/repo", ready.Params.Repo);
    }

    [Fact]
    public async Task Ready_approvals_handle_optional_body_and_labels()
    {
        var approval = CreateApproval(
            status: ApprovalStatuses.Approved,
            args: new PendingGithubIssueArgs
            {
                Repo = "owner/repo",
                Title = "Title only",
                Labels = []
            });
        var checkpoint = CreateCheckpoint(
            approval.CorrelationId,
            CheckpointStatuses.ReadyToResume,
            CreatePendingActionJson(approval.ApprovalId));
        var repository = new FakeApprovalRepository();
        repository.Approvals[approval.ApprovalId] = approval;
        repository.Checkpoints[checkpoint.CheckpointId] = checkpoint;

        var response = await CreateService(repository).GetReadyApprovalsAsync();

        var ready = Assert.Single(response.Approvals);
        Assert.Equal("owner/repo", ready.Params.Repo);
        Assert.Equal("Title only", ready.Params.Title);
        Assert.Null(ready.Params.Body);
        Assert.Empty(ready.Params.Labels);
    }

    [Fact]
    public async Task Ready_approvals_skip_malformed_historical_rows()
    {
        var valid = CreateApproval(status: ApprovalStatuses.Approved);
        var invalid = CreateApproval(status: ApprovalStatuses.Approved) with
        {
            ParamsJson = """{"toolName":"github.create_issue","args":{"repo":"owner/repo","title":"wrapped only"},"approvalId":"00000000-0000-0000-0000-000000000000"}"""
        };
        var repository = new FakeApprovalRepository();
        repository.Approvals[valid.ApprovalId] = valid;
        repository.Approvals[invalid.ApprovalId] = invalid;
        repository.Checkpoints[Guid.NewGuid()] = CreateCheckpoint(
            valid.CorrelationId,
            CheckpointStatuses.ReadyToResume,
            CreatePendingActionJson(valid.ApprovalId));
        repository.Checkpoints[Guid.NewGuid()] = CreateCheckpoint(
            invalid.CorrelationId,
            CheckpointStatuses.ReadyToResume,
            CreatePendingActionJson(invalid.ApprovalId));

        var response = await CreateService(repository).GetReadyApprovalsAsync();

        var ready = Assert.Single(response.Approvals);
        Assert.Equal(valid.ApprovalId, ready.ApprovalId);
    }

    [Fact]
    public async Task Execute_approved_ready_github_issue_succeeds_and_completes_checkpoint()
    {
        var approval = CreateApproval(status: ApprovalStatuses.Approved);
        var checkpoint = CreateCheckpoint(
            approval.CorrelationId,
            CheckpointStatuses.ReadyToResume,
            CreatePendingActionJson(approval.ApprovalId));
        var repository = new FakeApprovalRepository();
        var toolbelt = new FakeToolbeltClient();
        repository.Approvals[approval.ApprovalId] = approval;
        repository.Checkpoints[checkpoint.CheckpointId] = checkpoint;

        var result = await CreateService(repository, toolbelt).ExecuteAsync(approval.ApprovalId);

        Assert.True(result.Succeeded);
        Assert.Equal("Executed", result.Response?.Status);
        Assert.Equal(CheckpointStatuses.Completed, result.Response?.CheckpointStatus);
        Assert.Equal(123, result.Response?.IssueNumber);
        Assert.Equal("https://github.com/owner/repo/issues/123", result.Response?.IssueUrl);
        Assert.Equal(1, toolbelt.CallCount);
        Assert.Equal(CheckpointStatuses.Completed, repository.Checkpoints[checkpoint.CheckpointId].Status);
    }

    [Theory]
    [InlineData(ApprovalStatuses.Pending, CheckpointStatuses.ReadyToResume)]
    [InlineData(ApprovalStatuses.Rejected, CheckpointStatuses.ReadyToResume)]
    [InlineData(ApprovalStatuses.Approved, CheckpointStatuses.Failed)]
    [InlineData(ApprovalStatuses.Approved, CheckpointStatuses.Completed)]
    public async Task Execute_non_ready_states_return_conflict_without_toolbelt_call(
        string approvalStatus,
        string checkpointStatus)
    {
        var approval = CreateApproval(status: approvalStatus);
        var checkpoint = CreateCheckpoint(
            approval.CorrelationId,
            checkpointStatus,
            CreatePendingActionJson(approval.ApprovalId));
        var repository = new FakeApprovalRepository();
        var toolbelt = new FakeToolbeltClient();
        repository.Approvals[approval.ApprovalId] = approval;
        repository.Checkpoints[checkpoint.CheckpointId] = checkpoint;

        var result = await CreateService(repository, toolbelt).ExecuteAsync(approval.ApprovalId);

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Equal("APPROVAL_NOT_EXECUTABLE", result.Error?.Code);
        Assert.Equal(0, toolbelt.CallCount);
        Assert.Equal(checkpointStatus, repository.Checkpoints[checkpoint.CheckpointId].Status);
    }

    [Fact]
    public async Task Duplicate_execute_returns_conflict_without_toolbelt_call()
    {
        var approval = CreateApproval(status: ApprovalStatuses.Approved);
        var checkpoint = CreateCheckpoint(
            approval.CorrelationId,
            CheckpointStatuses.ReadyToResume,
            CreatePendingActionJson(approval.ApprovalId));
        var repository = new FakeApprovalRepository { StartExecutionSucceeds = false };
        var toolbelt = new FakeToolbeltClient();
        repository.Approvals[approval.ApprovalId] = approval;
        repository.Checkpoints[checkpoint.CheckpointId] = checkpoint;

        var result = await CreateService(repository, toolbelt).ExecuteAsync(approval.ApprovalId);

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Equal(0, toolbelt.CallCount);
    }

    [Fact]
    public async Task Execute_unknown_tool_returns_sanitized_conflict_without_toolbelt_call()
    {
        var approval = CreateApproval(status: ApprovalStatuses.Approved);
        var checkpoint = CreateCheckpoint(
            approval.CorrelationId,
            CheckpointStatuses.ReadyToResume,
            CreatePendingActionJson(approval.ApprovalId, "unknown.tool"));
        var repository = new FakeApprovalRepository();
        var toolbelt = new FakeToolbeltClient();
        repository.Approvals[approval.ApprovalId] = approval;
        repository.Checkpoints[checkpoint.CheckpointId] = checkpoint;

        var result = await CreateService(repository, toolbelt).ExecuteAsync(approval.ApprovalId);

        Assert.False(result.Succeeded);
        Assert.Equal("APPROVAL_TOOL_NOT_SUPPORTED", result.Error?.Code);
        Assert.Equal(0, toolbelt.CallCount);
        Assert.Equal(CheckpointStatuses.ReadyToResume, repository.Checkpoints[checkpoint.CheckpointId].Status);
    }

    [Fact]
    public async Task Execute_toolbelt_failure_marks_checkpoint_failed_and_returns_sanitized_failure()
    {
        var approval = CreateApproval(status: ApprovalStatuses.Approved);
        var checkpoint = CreateCheckpoint(
            approval.CorrelationId,
            CheckpointStatuses.ReadyToResume,
            CreatePendingActionJson(approval.ApprovalId));
        var repository = new FakeApprovalRepository();
        var toolbelt = new FakeToolbeltClient(fail: true);
        repository.Approvals[approval.ApprovalId] = approval;
        repository.Checkpoints[checkpoint.CheckpointId] = checkpoint;

        var result = await CreateService(repository, toolbelt).ExecuteAsync(approval.ApprovalId);

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status502BadGateway, result.StatusCode);
        Assert.Equal("Failed", result.Response?.Status);
        Assert.Equal(CheckpointStatuses.Failed, result.Response?.CheckpointStatus);
        Assert.Equal("GitHub issue execution failed. No sensitive details were exposed.", result.Response?.Message);
        Assert.Null(result.Response?.ErrorCode);
        Assert.Equal(CheckpointStatuses.Failed, repository.Checkpoints[checkpoint.CheckpointId].Status);
        Assert.Equal(1, toolbelt.CallCount);
    }

    [Fact]
    public async Task Execute_toolbelt_sanitized_github_failure_returns_error_code_and_calls_toolbelt_once()
    {
        var approval = CreateApproval(status: ApprovalStatuses.Approved);
        var checkpoint = CreateCheckpoint(
            approval.CorrelationId,
            CheckpointStatuses.ReadyToResume,
            CreatePendingActionJson(approval.ApprovalId));
        var repository = new FakeApprovalRepository();
        var toolbelt = new FakeToolbeltClient(
            fail: true,
            errorCode: "GITHUB_AUTH_FAILED",
            statusCode: System.Net.HttpStatusCode.Unauthorized);
        repository.Approvals[approval.ApprovalId] = approval;
        repository.Checkpoints[checkpoint.CheckpointId] = checkpoint;

        var result = await CreateService(repository, toolbelt).ExecuteAsync(approval.ApprovalId);
        var responseJson = JsonSerializer.Serialize(result.Response, ApprovalJson.Options);

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status401Unauthorized, result.StatusCode);
        Assert.Equal("Failed", result.Response?.Status);
        Assert.Equal("GITHUB_AUTH_FAILED", result.Response?.ErrorCode);
        Assert.Equal(CheckpointStatuses.Failed, repository.Checkpoints[checkpoint.CheckpointId].Status);
        Assert.Equal(1, toolbelt.CallCount);
        Assert.DoesNotContain("test-token", responseJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("raw github body", responseJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack", responseJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reject_transitions_pending_to_rejected_and_marks_waiting_checkpoint_failed()
    {
        var approval = CreateApproval(status: ApprovalStatuses.Pending);
        var checkpoint = CreateCheckpoint(approval.CorrelationId, CheckpointStatuses.WaitingApproval);
        var repository = new FakeApprovalRepository();
        repository.Approvals[approval.ApprovalId] = approval;
        repository.Checkpoints[checkpoint.CheckpointId] = checkpoint;

        var result = await CreateService(repository).RejectAsync(approval.ApprovalId);

        Assert.True(result.Succeeded);
        Assert.Equal(ApprovalStatuses.Rejected, result.Response?.Status);
        Assert.Equal(CheckpointStatuses.Failed, result.Response?.CheckpointStatus);
        Assert.False(result.Response?.ResumeAvailable);
        Assert.Equal("Approval rejected. No external action was executed.", result.Response?.Message);
        Assert.Equal(ApprovalStatuses.Rejected, repository.Approvals[approval.ApprovalId].Status);
        Assert.Equal(CheckpointStatuses.Failed, repository.Checkpoints[checkpoint.CheckpointId].Status);
    }

    [Fact]
    public async Task Approve_non_pending_returns_not_pending()
    {
        var approval = CreateApproval(status: ApprovalStatuses.Approved);
        var repository = new FakeApprovalRepository();
        repository.Approvals[approval.ApprovalId] = approval;

        var result = await CreateService(repository).ApproveAsync(approval.ApprovalId, "approver-1");

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Equal("APPROVAL_NOT_PENDING", result.Error?.Code);
    }

    [Fact]
    public async Task Reject_non_pending_returns_not_pending()
    {
        var approval = CreateApproval(status: ApprovalStatuses.Rejected);
        var repository = new FakeApprovalRepository();
        repository.Approvals[approval.ApprovalId] = approval;

        var result = await CreateService(repository).RejectAsync(approval.ApprovalId);

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Equal("APPROVAL_NOT_PENDING", result.Error?.Code);
    }

    [Fact]
    public async Task Missing_approval_returns_not_found()
    {
        var result = await CreateService(new FakeApprovalRepository()).ApproveAsync(Guid.NewGuid(), "approver-1");

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status404NotFound, result.StatusCode);
        Assert.Equal("APPROVAL_NOT_FOUND", result.Error?.Code);
    }

    [Fact]
    public void Params_hash_is_deterministic_for_same_params()
    {
        var args = new PendingGithubIssueArgs
        {
            Repo = "sanghunmok-prog/nexus-ask-act-hub",
            Title = "Delayed shipments review",
            Body = "Review delayed shipment findings from NEXUS. Approval is required before this issue is created.",
            Labels = ["nexus-demo"]
        };

        var firstJson = ApprovalJson.SerializeDeterministic(args);
        var secondJson = ApprovalJson.SerializeDeterministic(args);

        Assert.Equal(firstJson, secondJson);
        Assert.Equal(ApprovalJson.ComputeParamsHash(firstJson), ApprovalJson.ComputeParamsHash(secondJson));
    }

    private static ApprovalService CreateService(
        FakeApprovalRepository repository,
        IToolbeltWriteClient? toolbeltClient = null)
    {
        var configuration = new ConfigurationBuilder().Build();
        return new ApprovalService(
            repository,
            new ApprovalIntentFactory(configuration, new TestHostEnvironment()),
            toolbeltClient ?? new FakeToolbeltClient(),
            NullLogger<ApprovalService>.Instance);
    }

    private static ApprovalRequestRecord CreateApproval(string status, PendingGithubIssueArgs? args = null)
    {
        args ??= new PendingGithubIssueArgs
        {
            Repo = "sanghunmok-prog/nexus-ask-act-hub",
            Title = "Delayed shipments review",
            Body = "Review delayed shipment findings from NEXUS. Approval is required before this issue is created.",
            Labels = ["nexus-demo"]
        };
        var paramsJson = ApprovalJson.SerializeDeterministic(args);

        return new ApprovalRequestRecord
        {
            ApprovalId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            RequestedAtUtc = DateTime.UtcNow,
            RequestedByUserId = "demo-user",
            Status = status,
            ToolName = ApprovalIntentFactory.GitHubCreateIssueToolName,
            ParamsHash = ApprovalJson.ComputeParamsHash(paramsJson),
            ParamsJson = paramsJson,
            RiskSummary = ApprovalIntentFactory.RiskSummary
        };
    }

    private static AgentCheckpointRecord CreateCheckpoint(
        Guid correlationId,
        string status,
        string pendingActionJson = "{}") =>
        new()
        {
            CheckpointId = Guid.NewGuid(),
            CorrelationId = correlationId,
            CreatedAtUtc = DateTime.UtcNow,
            Status = status,
            ConversationSummary = "summary",
            PendingActionJson = pendingActionJson
        };

    private static string CreatePendingActionJson(
        Guid approvalId,
        string toolName = ApprovalIntentFactory.GitHubCreateIssueToolName) =>
        ApprovalJson.SerializeDeterministic(new PendingActionEnvelope
        {
            ToolName = toolName,
            ApprovalId = approvalId,
            Args = new PendingGithubIssueArgs
            {
                Repo = "owner/repo",
                Title = "Delayed shipments review",
                Body = "Review delayed shipment findings from NEXUS. Approval is required before this issue is created.",
                Labels = ["nexus-demo"]
            }
        });

    private static void AddApprovalWithCheckpoint(
        FakeApprovalRepository repository,
        ApprovalRequestRecord approval,
        string checkpointStatus)
    {
        repository.Approvals[approval.ApprovalId] = approval;
        var checkpoint = CreateCheckpoint(
            approval.CorrelationId,
            checkpointStatus,
            CreatePendingActionJson(approval.ApprovalId));
        repository.Checkpoints[checkpoint.CheckpointId] = checkpoint;
    }

    private sealed class FakeApprovalRepository : IApprovalRepository
    {
        public Dictionary<Guid, ApprovalRequestRecord> Approvals { get; } = [];

        public Dictionary<Guid, AgentCheckpointRecord> Checkpoints { get; } = [];

        public bool StartExecutionSucceeds { get; init; } = true;

        public Task<ApprovalCreateResult> CreateApprovalWithCheckpointAsync(
            ApprovalCreateRequest request,
            PendingGithubIssueArgs args,
            CancellationToken cancellationToken = default)
        {
            Approvals[request.Approval.ApprovalId] = request.Approval;
            Checkpoints[request.Checkpoint.CheckpointId] = request.Checkpoint;
            return Task.FromResult(new ApprovalCreateResult
            {
                Approval = request.Approval,
                Checkpoint = request.Checkpoint,
                Args = args
            });
        }

        public Task<IReadOnlyList<ApprovalRequestRecord>> GetPendingApprovalsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ApprovalRequestRecord>>(
                Approvals.Values
                    .Where(approval => approval.Status == ApprovalStatuses.Pending)
                    .OrderByDescending(approval => approval.RequestedAtUtc)
                    .ToArray());

        public Task<IReadOnlyList<ReadyApprovalRecord>> GetReadyApprovalsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ReadyApprovalRecord>>(
                Approvals.Values
                    .Where(approval => approval.Status == ApprovalStatuses.Approved)
                    .Where(approval => approval.ToolName == ApprovalIntentFactory.GitHubCreateIssueToolName)
                    .Join(
                        Checkpoints.Values.Where(checkpoint => checkpoint.Status == CheckpointStatuses.ReadyToResume),
                        approval => approval.CorrelationId,
                        checkpoint => checkpoint.CorrelationId,
                        (approval, checkpoint) => new ReadyApprovalRecord
                        {
                            Approval = approval,
                            Checkpoint = checkpoint
                        })
                    .ToArray());

        public Task<ApprovalRequestRecord?> GetApprovalAsync(Guid approvalId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Approvals.GetValueOrDefault(approvalId));

        public Task<ReadyApprovalRecord?> GetApprovalWithCheckpointAsync(Guid approvalId, CancellationToken cancellationToken = default)
        {
            if (!Approvals.TryGetValue(approvalId, out var approval))
            {
                return Task.FromResult<ReadyApprovalRecord?>(null);
            }

            var checkpoint = Checkpoints.Values.FirstOrDefault(candidate => candidate.CorrelationId == approval.CorrelationId);
            return Task.FromResult(checkpoint is null
                ? null
                : new ReadyApprovalRecord
                {
                    Approval = approval,
                    Checkpoint = checkpoint
                });
        }

        public Task<bool> ApproveAsync(
            Guid approvalId,
            Guid correlationId,
            DateTime approvedAtUtc,
            string approvedByUserId,
            CancellationToken cancellationToken = default)
        {
            var approval = Approvals[approvalId];
            if (approval.Status != ApprovalStatuses.Pending)
            {
                return Task.FromResult(false);
            }

            Approvals[approvalId] = approval with
            {
                Status = ApprovalStatuses.Approved,
                ApprovedAtUtc = approvedAtUtc,
                ApprovedByUserId = approvedByUserId
            };

            foreach (var checkpoint in Checkpoints.Values.Where(checkpoint =>
                         checkpoint.CorrelationId == correlationId &&
                         checkpoint.Status == CheckpointStatuses.WaitingApproval))
            {
                Checkpoints[checkpoint.CheckpointId] = checkpoint with
                {
                    Status = CheckpointStatuses.ReadyToResume
                };
            }

            return Task.FromResult(true);
        }

        public Task<bool> RejectAsync(Guid approvalId, Guid correlationId, CancellationToken cancellationToken = default)
        {
            if (Approvals[approvalId].Status != ApprovalStatuses.Pending)
            {
                return Task.FromResult(false);
            }

            Approvals[approvalId] = Approvals[approvalId] with
            {
                Status = ApprovalStatuses.Rejected
            };

            foreach (var checkpoint in Checkpoints.Values.Where(checkpoint =>
                         checkpoint.CorrelationId == correlationId &&
                         checkpoint.Status == CheckpointStatuses.WaitingApproval))
            {
                Checkpoints[checkpoint.CheckpointId] = checkpoint with
                {
                    Status = CheckpointStatuses.Failed
                };
            }

            return Task.FromResult(true);
        }

        public Task<bool> TryStartExecutionAsync(Guid checkpointId, CancellationToken cancellationToken = default)
        {
            if (!StartExecutionSucceeds ||
                !Checkpoints.TryGetValue(checkpointId, out var checkpoint) ||
                checkpoint.Status != CheckpointStatuses.ReadyToResume)
            {
                return Task.FromResult(false);
            }

            Checkpoints[checkpointId] = checkpoint with { Status = CheckpointStatuses.Executing };
            return Task.FromResult(true);
        }

        public Task CompleteExecutionAsync(Guid checkpointId, CancellationToken cancellationToken = default)
        {
            Checkpoints[checkpointId] = Checkpoints[checkpointId] with { Status = CheckpointStatuses.Completed };
            return Task.CompletedTask;
        }

        public Task FailExecutionAsync(Guid checkpointId, CancellationToken cancellationToken = default)
        {
            Checkpoints[checkpointId] = Checkpoints[checkpointId] with { Status = CheckpointStatuses.Failed };
            return Task.CompletedTask;
        }
    }

    private sealed class FakeToolbeltClient : IToolbeltClient, IToolbeltWriteClient
    {
        private readonly bool fail;
        private readonly string? errorCode;
        private readonly System.Net.HttpStatusCode statusCode;

        public FakeToolbeltClient(
            bool fail = false,
            string? errorCode = null,
            System.Net.HttpStatusCode statusCode = System.Net.HttpStatusCode.BadGateway)
        {
            this.fail = fail;
            this.errorCode = errorCode;
            this.statusCode = statusCode;
        }

        public int CallCount { get; private set; }

        public Task<ToolbeltToolResult> CallAsync(ToolPlanStep step, CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (fail)
            {
                throw new ToolbeltClientException(
                    step.ToolName,
                    statusCode,
                    "github token raw failure should not leak",
                    errorCode,
                    "raw github body with test-token should not leak");
            }

            return Task.FromResult(new ToolbeltToolResult(
                step.ToolName,
                JsonSerializer.SerializeToElement(
                    new
                    {
                        number = 123,
                        htmlUrl = "https://github.com/owner/repo/issues/123",
                        title = "Delayed shipments review"
                    },
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        }
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
