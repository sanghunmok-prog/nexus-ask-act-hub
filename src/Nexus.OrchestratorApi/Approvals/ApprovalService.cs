namespace Nexus.OrchestratorApi.Approvals;

public sealed class ApprovalService
{
    private readonly IApprovalRepository repository;
    private readonly ApprovalIntentFactory intentFactory;

    public ApprovalService(IApprovalRepository repository, ApprovalIntentFactory intentFactory)
    {
        this.repository = repository;
        this.intentFactory = intentFactory;
    }

    public bool IsActionIntent(string prompt) => intentFactory.IsActionIntent(prompt);

    public PendingGithubIssueArgs CreateGitHubIssueArgs() => intentFactory.CreateGitHubIssueArgs();

    public async Task<ApprovalCreateResult> CreateGitHubIssueApprovalAsync(
        string prompt,
        Guid correlationId,
        string requestedByUserId,
        CancellationToken cancellationToken = default)
    {
        var args = intentFactory.CreateGitHubIssueArgs();
        var paramsJson = ApprovalJson.SerializeDeterministic(args);
        var approvalId = Guid.NewGuid();
        var checkpointId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var pendingAction = new PendingActionEnvelope
        {
            ToolName = ApprovalIntentFactory.GitHubCreateIssueToolName,
            Args = args,
            ApprovalId = approvalId
        };

        var request = new ApprovalCreateRequest
        {
            Approval = new ApprovalRequestRecord
            {
                ApprovalId = approvalId,
                CorrelationId = correlationId,
                RequestedAtUtc = now,
                RequestedByUserId = requestedByUserId,
                Status = ApprovalStatuses.Pending,
                ToolName = ApprovalIntentFactory.GitHubCreateIssueToolName,
                ParamsHash = ApprovalJson.ComputeParamsHash(paramsJson),
                ParamsJson = paramsJson,
                RiskSummary = ApprovalIntentFactory.RiskSummary
            },
            Checkpoint = new AgentCheckpointRecord
            {
                CheckpointId = checkpointId,
                CorrelationId = correlationId,
                CreatedAtUtc = now,
                Status = CheckpointStatuses.WaitingApproval,
                ConversationSummary = CreateConversationSummary(prompt),
                PendingActionJson = ApprovalJson.SerializeDeterministic(pendingAction)
            }
        };

        try
        {
            return await repository.CreateApprovalWithCheckpointAsync(request, args, cancellationToken);
        }
        catch (Exception exception)
        {
            throw new ApprovalPersistenceException(exception);
        }
    }

    public async Task<PendingApprovalsResponse> GetPendingApprovalsAsync(CancellationToken cancellationToken = default)
    {
        var approvals = await repository.GetPendingApprovalsAsync(cancellationToken);
        return new PendingApprovalsResponse
        {
            Approvals = approvals
                .Select(ToPendingApprovalDto)
                .ToArray()
        };
    }

    public async Task<ApprovalDecisionResult> ApproveAsync(
        Guid approvalId,
        string approvedByUserId,
        CancellationToken cancellationToken = default)
    {
        var approval = await repository.GetApprovalAsync(approvalId, cancellationToken);
        if (approval is null)
        {
            return ApprovalDecisionResult.NotFound();
        }

        if (approval.Status != ApprovalStatuses.Pending)
        {
            return ApprovalDecisionResult.NotPending();
        }

        await repository.ApproveAsync(approvalId, DateTime.UtcNow, approvedByUserId, cancellationToken);
        return ApprovalDecisionResult.Success(new ApprovalDecisionResponse
        {
            ApprovalId = approvalId,
            Status = ApprovalStatuses.Approved,
            ResumeAvailable = false,
            Message = "Approval recorded. Workflow resume will be implemented in a later PR."
        });
    }

    public async Task<ApprovalDecisionResult> RejectAsync(Guid approvalId, CancellationToken cancellationToken = default)
    {
        var approval = await repository.GetApprovalAsync(approvalId, cancellationToken);
        if (approval is null)
        {
            return ApprovalDecisionResult.NotFound();
        }

        if (approval.Status != ApprovalStatuses.Pending)
        {
            return ApprovalDecisionResult.NotPending();
        }

        await repository.RejectAsync(approvalId, approval.CorrelationId, cancellationToken);
        return ApprovalDecisionResult.Success(new ApprovalDecisionResponse
        {
            ApprovalId = approvalId,
            Status = ApprovalStatuses.Rejected,
            ResumeAvailable = false,
            Message = "Approval rejected. No external action was executed."
        });
    }

    private static string CreateConversationSummary(string prompt)
    {
        var trimmed = prompt.Trim();
        if (trimmed.Length > 240)
        {
            trimmed = trimmed[..240].TrimEnd() + "...";
        }

        return $"User requested approval-gated GitHub issue creation. Prompt: {trimmed}";
    }

    private static PendingApprovalDto ToPendingApprovalDto(ApprovalRequestRecord approval)
    {
        var args = ApprovalJson.DeserializeArgs(approval.ParamsJson);
        return new PendingApprovalDto
        {
            ApprovalId = approval.ApprovalId,
            CorrelationId = approval.CorrelationId,
            RequestedAtUtc = approval.RequestedAtUtc,
            RequestedByUserId = approval.RequestedByUserId,
            Status = approval.Status,
            ToolName = approval.ToolName,
            ParamsHash = approval.ParamsHash,
            Params = new ApprovalPublicParams
            {
                Repo = args.Repo,
                Title = args.Title,
                Labels = args.Labels
            },
            RiskSummary = approval.RiskSummary
        };
    }
}

public sealed record ApprovalDecisionResult
{
    public bool Succeeded { get; init; }

    public int StatusCode { get; init; }

    public ApprovalDecisionResponse? Response { get; init; }

    public ApprovalErrorResponse? Error { get; init; }

    public static ApprovalDecisionResult Success(ApprovalDecisionResponse response) =>
        new()
        {
            Succeeded = true,
            StatusCode = StatusCodes.Status200OK,
            Response = response
        };

    public static ApprovalDecisionResult NotFound() =>
        Failure(
            StatusCodes.Status404NotFound,
            "APPROVAL_NOT_FOUND",
            "Approval request was not found.");

    public static ApprovalDecisionResult NotPending() =>
        Failure(
            StatusCodes.Status409Conflict,
            "APPROVAL_NOT_PENDING",
            "Approval request is not pending.");

    private static ApprovalDecisionResult Failure(int statusCode, string code, string message) =>
        new()
        {
            Succeeded = false,
            StatusCode = statusCode,
            Error = new ApprovalErrorResponse
            {
                Code = code,
                Message = message
            }
        };
}
