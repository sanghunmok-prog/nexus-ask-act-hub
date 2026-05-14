using System.Text.Json;
using Nexus.OrchestratorApi.Agent;

namespace Nexus.OrchestratorApi.Approvals;

public sealed class ApprovalService
{
    private readonly IApprovalRepository repository;
    private readonly ApprovalIntentFactory intentFactory;
    private readonly IToolbeltWriteClient toolbeltWriteClient;
    private readonly ILogger<ApprovalService> logger;

    public ApprovalService(
        IApprovalRepository repository,
        ApprovalIntentFactory intentFactory,
        IToolbeltWriteClient toolbeltWriteClient,
        ILogger<ApprovalService> logger)
    {
        this.repository = repository;
        this.intentFactory = intentFactory;
        this.toolbeltWriteClient = toolbeltWriteClient;
        this.logger = logger;
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

    public async Task<ReadyApprovalsResponse> GetReadyApprovalsAsync(CancellationToken cancellationToken = default)
    {
        var approvals = await repository.GetReadyApprovalsAsync(cancellationToken);
        var readyApprovals = new List<ReadyApprovalDto>();
        foreach (var approval in approvals)
        {
            if (TryToReadyApprovalDto(approval, out var dto))
            {
                readyApprovals.Add(dto);
            }
        }

        return new ReadyApprovalsResponse
        {
            Approvals = readyApprovals
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

        var approved = await repository.ApproveAsync(
            approvalId,
            approval.CorrelationId,
            DateTime.UtcNow,
            approvedByUserId,
            cancellationToken);

        if (!approved)
        {
            return ApprovalDecisionResult.NotPending();
        }

        return ApprovalDecisionResult.Success(new ApprovalDecisionResponse
        {
            ApprovalId = approvalId,
            Status = ApprovalStatuses.Approved,
            CheckpointStatus = CheckpointStatuses.ReadyToResume,
            ResumeAvailable = true,
            Message = "Approval recorded. The approved action is ready to execute. No external action has been executed yet."
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

        var rejected = await repository.RejectAsync(approvalId, approval.CorrelationId, cancellationToken);
        if (!rejected)
        {
            return ApprovalDecisionResult.NotPending();
        }

        return ApprovalDecisionResult.Success(new ApprovalDecisionResponse
        {
            ApprovalId = approvalId,
            Status = ApprovalStatuses.Rejected,
            CheckpointStatus = CheckpointStatuses.Failed,
            ResumeAvailable = false,
            Message = "Approval rejected. No external action was executed."
        });
    }

    public async Task<ApprovalExecutionResult> ExecuteAsync(
        Guid approvalId,
        CancellationToken cancellationToken = default)
    {
        var readyApproval = await repository.GetApprovalWithCheckpointAsync(approvalId, cancellationToken);
        if (readyApproval is null)
        {
            return ApprovalExecutionResult.NotFound();
        }

        if (readyApproval.Approval.Status != ApprovalStatuses.Approved ||
            readyApproval.Checkpoint.Status != CheckpointStatuses.ReadyToResume)
        {
            return ApprovalExecutionResult.Conflict("APPROVAL_NOT_EXECUTABLE", "Approved action is not ready to execute.");
        }

        if (!TryReadPendingAction(readyApproval.Checkpoint.PendingActionJson, out var pendingAction) ||
            pendingAction.ApprovalId != approvalId)
        {
            return ApprovalExecutionResult.Conflict("APPROVAL_ACTION_INVALID", "Approved action payload is invalid.");
        }

        if (pendingAction.ToolName != ApprovalIntentFactory.GitHubCreateIssueToolName)
        {
            return ApprovalExecutionResult.Conflict("APPROVAL_TOOL_NOT_SUPPORTED", "Approved action tool is not supported.");
        }

        var claimed = await repository.TryStartExecutionAsync(readyApproval.Checkpoint.CheckpointId, cancellationToken);
        if (!claimed)
        {
            return ApprovalExecutionResult.Conflict("APPROVAL_NOT_EXECUTABLE", "Approved action is not ready to execute.");
        }

        try
        {
            var result = await toolbeltWriteClient.CallAsync(
                new ToolPlanStep
                {
                    ToolName = ApprovalIntentFactory.GitHubCreateIssueToolName,
                    Method = HttpMethods.Post,
                    Endpoint = "/api/tools/github/create-issue",
                    Args = pendingAction.Args
                },
                cancellationToken);

            await repository.CompleteExecutionAsync(readyApproval.Checkpoint.CheckpointId, cancellationToken);
            return ApprovalExecutionResult.Success(ToExecutionSuccessResponse(
                approvalId,
                readyApproval.Checkpoint.CheckpointId,
                result.RawJson));
        }
        catch (ToolbeltClientException exception) when (!cancellationToken.IsCancellationRequested)
        {
            await repository.FailExecutionAsync(readyApproval.Checkpoint.CheckpointId, cancellationToken);
            return ApprovalExecutionResult.ExecutionFailed(
                CreateExecutionFailureResponse(
                    approvalId,
                    readyApproval.Checkpoint.CheckpointId,
                    SanitizeGitHubErrorCode(exception.ErrorCode)),
                ToExecutionFailureStatusCode(exception.StatusCode));
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            await repository.FailExecutionAsync(readyApproval.Checkpoint.CheckpointId, cancellationToken);
            return ApprovalExecutionResult.ExecutionFailed(CreateExecutionFailureResponse(
                approvalId,
                readyApproval.Checkpoint.CheckpointId,
                errorCode: null));
        }
    }

    private static ApprovalExecutionResponse CreateExecutionFailureResponse(
        Guid approvalId,
        Guid checkpointId,
        string? errorCode) =>
        new()
        {
            ApprovalId = approvalId,
            CheckpointId = checkpointId,
            ToolName = ApprovalIntentFactory.GitHubCreateIssueToolName,
            Status = "Failed",
            CheckpointStatus = CheckpointStatuses.Failed,
            ErrorCode = errorCode,
            Message = "GitHub issue execution failed. No sensitive details were exposed."
        };

    private static int ToExecutionFailureStatusCode(System.Net.HttpStatusCode? statusCode)
    {
        if (statusCode is null)
        {
            return StatusCodes.Status502BadGateway;
        }

        var numericStatusCode = (int)statusCode.Value;
        return numericStatusCode is >= 400 and <= 599
            ? numericStatusCode
            : StatusCodes.Status502BadGateway;
    }

    private static string? SanitizeGitHubErrorCode(string? errorCode)
    {
        var allowedCodes = new HashSet<string>(StringComparer.Ordinal)
        {
            "GITHUB_NOT_CONFIGURED",
            "GITHUB_REPO_NOT_ALLOWED",
            "GITHUB_AUTH_FAILED",
            "GITHUB_PERMISSION_FAILED",
            "GITHUB_VALIDATION_FAILED",
            "GITHUB_TEMPORARY_FAILURE",
            "GITHUB_REPO_NOT_ACCESSIBLE",
            "GITHUB_ISSUES_DISABLED",
            "GITHUB_REPO_INVALID",
            "GITHUB_TITLE_REQUIRED",
            "GITHUB_CREATE_ISSUE_FAILED"
        };

        return !string.IsNullOrWhiteSpace(errorCode) && allowedCodes.Contains(errorCode)
            ? errorCode
            : null;
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
                Body = null,
                Labels = args.Labels
            },
            RiskSummary = approval.RiskSummary
        };
    }

    private bool TryToReadyApprovalDto(ReadyApprovalRecord readyApproval, out ReadyApprovalDto dto)
    {
        dto = new ReadyApprovalDto
        {
            ApprovalId = readyApproval.Approval.ApprovalId,
            CorrelationId = readyApproval.Approval.CorrelationId,
            CheckpointId = readyApproval.Checkpoint.CheckpointId,
            CheckpointStatus = readyApproval.Checkpoint.Status,
            ApprovedAtUtc = readyApproval.Approval.ApprovedAtUtc,
            ApprovedByUserId = readyApproval.Approval.ApprovedByUserId,
            ToolName = readyApproval.Approval.ToolName,
            ParamsHash = readyApproval.Approval.ParamsHash,
            Params = new ApprovalPublicParams
            {
                Repo = string.Empty,
                Title = string.Empty
            },
            RiskSummary = readyApproval.Approval.RiskSummary,
            ExecutionAvailable = false
        };

        if (readyApproval.Approval.ToolName != ApprovalIntentFactory.GitHubCreateIssueToolName)
        {
            logger.LogWarning(
                "Skipping ready approval {ApprovalId} because tool {ToolName} is not supported.",
                readyApproval.Approval.ApprovalId,
                readyApproval.Approval.ToolName);
            return false;
        }

        if (!TryDeserializeApprovalParams(readyApproval.Approval.ParamsJson, out var args))
        {
            logger.LogWarning(
                "Skipping ready approval {ApprovalId} because ParamsJson could not be parsed as github.create_issue params.",
                readyApproval.Approval.ApprovalId);
            return false;
        }

        dto = new ReadyApprovalDto
        {
            ApprovalId = readyApproval.Approval.ApprovalId,
            CorrelationId = readyApproval.Approval.CorrelationId,
            CheckpointId = readyApproval.Checkpoint.CheckpointId,
            CheckpointStatus = readyApproval.Checkpoint.Status,
            ApprovedAtUtc = readyApproval.Approval.ApprovedAtUtc,
            ApprovedByUserId = readyApproval.Approval.ApprovedByUserId,
            ToolName = readyApproval.Approval.ToolName,
            ParamsHash = readyApproval.Approval.ParamsHash,
            Params = new ApprovalPublicParams
            {
                Repo = args.Repo,
                Title = args.Title,
                Body = args.Body,
                Labels = args.Labels ?? []
            },
            RiskSummary = readyApproval.Approval.RiskSummary,
            ExecutionAvailable = readyApproval.Checkpoint.Status == CheckpointStatuses.ReadyToResume &&
                                 readyApproval.Approval.ToolName == ApprovalIntentFactory.GitHubCreateIssueToolName
        };
        return true;
    }

    private static bool TryDeserializeApprovalParams(string paramsJson, out PendingGithubIssueArgs args)
    {
        args = new PendingGithubIssueArgs
        {
            Repo = string.Empty,
            Title = string.Empty,
            Labels = []
        };

        try
        {
            var parsed = JsonSerializer.Deserialize<PendingGithubIssueArgs>(paramsJson, ApprovalJson.Options);
            if (parsed is null ||
                string.IsNullOrWhiteSpace(parsed.Repo) ||
                string.IsNullOrWhiteSpace(parsed.Title))
            {
                return false;
            }

            args = parsed with
            {
                Repo = parsed.Repo,
                Title = parsed.Title,
                Body = string.IsNullOrWhiteSpace(parsed.Body) ? null : parsed.Body,
                Labels = parsed.Labels ?? []
            };
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryReadPendingAction(string? pendingActionJson, out PendingActionEnvelope pendingAction)
    {
        pendingAction = new PendingActionEnvelope
        {
            ToolName = string.Empty,
            Args = new PendingGithubIssueArgs
            {
                Repo = string.Empty,
                Title = string.Empty,
                Body = string.Empty,
                Labels = []
            },
            ApprovalId = Guid.Empty
        };

        if (string.IsNullOrWhiteSpace(pendingActionJson))
        {
            return false;
        }

        try
        {
            pendingAction = JsonSerializer.Deserialize<PendingActionEnvelope>(pendingActionJson, ApprovalJson.Options) ??
                            pendingAction;
            return !string.IsNullOrWhiteSpace(pendingAction.ToolName);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static ApprovalExecutionResponse ToExecutionSuccessResponse(
        Guid approvalId,
        Guid checkpointId,
        JsonElement rawJson)
    {
        var issueNumber = TryReadInt(rawJson, "number");
        var issueUrl = TryReadString(rawJson, "htmlUrl");
        return new ApprovalExecutionResponse
        {
            ApprovalId = approvalId,
            CheckpointId = checkpointId,
            ToolName = ApprovalIntentFactory.GitHubCreateIssueToolName,
            Status = "Executed",
            CheckpointStatus = CheckpointStatuses.Completed,
            IssueNumber = issueNumber,
            IssueUrl = issueUrl,
            Message = "GitHub issue created after explicit approval."
        };
    }

    private static int? TryReadInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var value)
            ? value
            : null;

    private static string? TryReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
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

public sealed record ApprovalExecutionResult
{
    public bool Succeeded { get; init; }

    public int StatusCode { get; init; }

    public ApprovalExecutionResponse? Response { get; init; }

    public ApprovalErrorResponse? Error { get; init; }

    public static ApprovalExecutionResult Success(ApprovalExecutionResponse response) =>
        new()
        {
            Succeeded = true,
            StatusCode = StatusCodes.Status200OK,
            Response = response
        };

    public static ApprovalExecutionResult ExecutionFailed(
        ApprovalExecutionResponse response,
        int statusCode = StatusCodes.Status502BadGateway) =>
        new()
        {
            Succeeded = false,
            StatusCode = statusCode,
            Response = response
        };

    public static ApprovalExecutionResult NotFound() =>
        Failure(
            StatusCodes.Status404NotFound,
            "APPROVAL_NOT_FOUND",
            "Approval request was not found.");

    public static ApprovalExecutionResult Conflict(string code, string message) =>
        Failure(StatusCodes.Status409Conflict, code, message);

    private static ApprovalExecutionResult Failure(int statusCode, string code, string message) =>
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
