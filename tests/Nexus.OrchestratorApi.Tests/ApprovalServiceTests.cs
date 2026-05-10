using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Http;
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
        var repository = new FakeApprovalRepository();
        repository.Approvals[approval.ApprovalId] = approval;

        var result = await CreateService(repository).ApproveAsync(approval.ApprovalId, "approver-1");

        Assert.True(result.Succeeded);
        Assert.Equal(ApprovalStatuses.Approved, result.Response?.Status);
        Assert.False(result.Response?.ResumeAvailable);
        Assert.Equal(ApprovalStatuses.Approved, repository.Approvals[approval.ApprovalId].Status);
        Assert.Equal("approver-1", repository.Approvals[approval.ApprovalId].ApprovedByUserId);
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

    private static ApprovalService CreateService(FakeApprovalRepository repository)
    {
        var configuration = new ConfigurationBuilder().Build();
        return new ApprovalService(repository, new ApprovalIntentFactory(configuration, new TestHostEnvironment()));
    }

    private static ApprovalRequestRecord CreateApproval(string status)
    {
        var args = new PendingGithubIssueArgs
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

    private static AgentCheckpointRecord CreateCheckpoint(Guid correlationId, string status) =>
        new()
        {
            CheckpointId = Guid.NewGuid(),
            CorrelationId = correlationId,
            CreatedAtUtc = DateTime.UtcNow,
            Status = status,
            ConversationSummary = "summary",
            PendingActionJson = "{}"
        };

    private sealed class FakeApprovalRepository : IApprovalRepository
    {
        public Dictionary<Guid, ApprovalRequestRecord> Approvals { get; } = [];

        public Dictionary<Guid, AgentCheckpointRecord> Checkpoints { get; } = [];

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

        public Task<ApprovalRequestRecord?> GetApprovalAsync(Guid approvalId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Approvals.GetValueOrDefault(approvalId));

        public Task ApproveAsync(
            Guid approvalId,
            DateTime approvedAtUtc,
            string approvedByUserId,
            CancellationToken cancellationToken = default)
        {
            var approval = Approvals[approvalId];
            Approvals[approvalId] = approval with
            {
                Status = ApprovalStatuses.Approved,
                ApprovedAtUtc = approvedAtUtc,
                ApprovedByUserId = approvedByUserId
            };
            return Task.CompletedTask;
        }

        public Task RejectAsync(Guid approvalId, Guid correlationId, CancellationToken cancellationToken = default)
        {
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

            return Task.CompletedTask;
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
