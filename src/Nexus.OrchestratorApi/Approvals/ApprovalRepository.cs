using Microsoft.Data.SqlClient;
using Nexus.OrchestratorApi.Documents;

namespace Nexus.OrchestratorApi.Approvals;

public interface IApprovalRepository
{
    Task<ApprovalCreateResult> CreateApprovalWithCheckpointAsync(
        ApprovalCreateRequest request,
        PendingGithubIssueArgs args,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApprovalRequestRecord>> GetPendingApprovalsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReadyApprovalRecord>> GetReadyApprovalsAsync(CancellationToken cancellationToken = default);

    Task<ApprovalRequestRecord?> GetApprovalAsync(Guid approvalId, CancellationToken cancellationToken = default);

    Task<ReadyApprovalRecord?> GetApprovalWithCheckpointAsync(Guid approvalId, CancellationToken cancellationToken = default);

    Task<bool> ApproveAsync(
        Guid approvalId,
        Guid correlationId,
        DateTime approvedAtUtc,
        string approvedByUserId,
        CancellationToken cancellationToken = default);

    Task<bool> RejectAsync(Guid approvalId, Guid correlationId, CancellationToken cancellationToken = default);

    Task<bool> TryStartExecutionAsync(Guid checkpointId, CancellationToken cancellationToken = default);

    Task CompleteExecutionAsync(Guid checkpointId, CancellationToken cancellationToken = default);

    Task FailExecutionAsync(Guid checkpointId, CancellationToken cancellationToken = default);
}

public sealed class SqlApprovalRepository : IApprovalRepository
{
    private readonly string? connectionString;

    public SqlApprovalRepository(IConfiguration configuration)
    {
        var environmentConnectionString = configuration["NEXUS_SQL_CONNECTION_STRING"];
        connectionString = string.IsNullOrWhiteSpace(environmentConnectionString)
            ? configuration.GetConnectionString("NexusSql")
            : environmentConnectionString;
    }

    public async Task<ApprovalCreateResult> CreateApprovalWithCheckpointAsync(
        ApprovalCreateRequest request,
        PendingGithubIssueArgs args,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (transaction)
        {
            try
            {
                await InsertApprovalAsync(connection, transaction, request.Approval, cancellationToken);
                await InsertCheckpointAsync(connection, transaction, request.Checkpoint, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        return new ApprovalCreateResult
        {
            Approval = request.Approval,
            Checkpoint = request.Checkpoint,
            Args = args
        };
    }

    public async Task<IReadOnlyList<ApprovalRequestRecord>> GetPendingApprovalsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = """
            SELECT
                ApprovalId,
                CorrelationId,
                RequestedAtUtc,
                RequestedByUserId,
                Status,
                ToolName,
                ParamsHash,
                ParamsJson,
                RiskSummary,
                ApprovedAtUtc,
                ApprovedByUserId
            FROM dbo.ApprovalRequest
            WHERE Status = @status
            ORDER BY RequestedAtUtc DESC;
            """;
        command.Parameters.Add(new SqlParameter("@status", ApprovalStatuses.Pending));

        var approvals = new List<ApprovalRequestRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            approvals.Add(ReadApproval(reader));
        }

        return approvals;
    }

    public async Task<IReadOnlyList<ReadyApprovalRecord>> GetReadyApprovalsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = ReadyApprovalsSql;
        command.Parameters.Add(new SqlParameter("@approvalStatus", ApprovalStatuses.Approved));
        command.Parameters.Add(new SqlParameter("@checkpointStatus", CheckpointStatuses.ReadyToResume));
        command.Parameters.Add(new SqlParameter("@toolName", ApprovalIntentFactory.GitHubCreateIssueToolName));

        var approvals = new List<ReadyApprovalRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            approvals.Add(ReadReadyApproval(reader));
        }

        return approvals;
    }

    public async Task<ApprovalRequestRecord?> GetApprovalAsync(Guid approvalId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = """
            SELECT
                ApprovalId,
                CorrelationId,
                RequestedAtUtc,
                RequestedByUserId,
                Status,
                ToolName,
                ParamsHash,
                ParamsJson,
                RiskSummary,
                ApprovedAtUtc,
                ApprovedByUserId
            FROM dbo.ApprovalRequest
            WHERE ApprovalId = @approvalId;
            """;
        command.Parameters.Add(new SqlParameter("@approvalId", approvalId));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadApproval(reader) : null;
    }

    public async Task<ReadyApprovalRecord?> GetApprovalWithCheckpointAsync(Guid approvalId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = ReadyApprovalByIdSql;
        command.Parameters.Add(new SqlParameter("@approvalId", approvalId));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadReadyApproval(reader) : null;
    }

    public async Task<bool> ApproveAsync(
        Guid approvalId,
        Guid correlationId,
        DateTime approvedAtUtc,
        string approvedByUserId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (transaction)
        {
            try
            {
                int affectedRows;
                await using (var approvalCommand = connection.CreateCommand())
                {
                    approvalCommand.Transaction = transaction;
                    approvalCommand.CommandTimeout = 2;
                    approvalCommand.CommandText = """
                        UPDATE dbo.ApprovalRequest
                        SET
                            Status = @status,
                            ApprovedAtUtc = @approvedAtUtc,
                            ApprovedByUserId = @approvedByUserId
                        WHERE ApprovalId = @approvalId
                          AND Status = @pendingStatus;
                        """;
                    approvalCommand.Parameters.Add(new SqlParameter("@status", ApprovalStatuses.Approved));
                    approvalCommand.Parameters.Add(new SqlParameter("@approvedAtUtc", approvedAtUtc));
                    approvalCommand.Parameters.Add(new SqlParameter("@approvedByUserId", approvedByUserId));
                    approvalCommand.Parameters.Add(new SqlParameter("@approvalId", approvalId));
                    approvalCommand.Parameters.Add(new SqlParameter("@pendingStatus", ApprovalStatuses.Pending));

                    affectedRows = await approvalCommand.ExecuteNonQueryAsync(cancellationToken);
                }

                if (affectedRows == 1)
                {
                    await using var checkpointCommand = connection.CreateCommand();
                    checkpointCommand.Transaction = transaction;
                    checkpointCommand.CommandTimeout = 2;
                    checkpointCommand.CommandText = """
                        UPDATE dbo.AgentCheckpoint
                        SET Status = @readyStatus
                        WHERE CorrelationId = @correlationId
                          AND Status = @waitingStatus;
                        """;
                    checkpointCommand.Parameters.Add(new SqlParameter("@readyStatus", CheckpointStatuses.ReadyToResume));
                    checkpointCommand.Parameters.Add(new SqlParameter("@correlationId", correlationId));
                    checkpointCommand.Parameters.Add(new SqlParameter("@waitingStatus", CheckpointStatuses.WaitingApproval));
                    await checkpointCommand.ExecuteNonQueryAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                return affectedRows == 1;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
    }

    public async Task<bool> RejectAsync(Guid approvalId, Guid correlationId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (transaction)
        {
            try
            {
                int affectedRows;
                await using (var approvalCommand = connection.CreateCommand())
                {
                    approvalCommand.Transaction = transaction;
                    approvalCommand.CommandTimeout = 2;
                    approvalCommand.CommandText = """
                        UPDATE dbo.ApprovalRequest
                        SET Status = @status
                        WHERE ApprovalId = @approvalId
                          AND Status = @pendingStatus;
                        """;
                    approvalCommand.Parameters.Add(new SqlParameter("@status", ApprovalStatuses.Rejected));
                    approvalCommand.Parameters.Add(new SqlParameter("@approvalId", approvalId));
                    approvalCommand.Parameters.Add(new SqlParameter("@pendingStatus", ApprovalStatuses.Pending));
                    affectedRows = await approvalCommand.ExecuteNonQueryAsync(cancellationToken);
                }

                if (affectedRows == 1)
                {
                    await using var checkpointCommand = connection.CreateCommand();
                    checkpointCommand.Transaction = transaction;
                    checkpointCommand.CommandTimeout = 2;
                    checkpointCommand.CommandText = """
                        UPDATE dbo.AgentCheckpoint
                        SET Status = @failedStatus
                        WHERE CorrelationId = @correlationId
                          AND Status = @waitingStatus;
                        """;
                    checkpointCommand.Parameters.Add(new SqlParameter("@failedStatus", CheckpointStatuses.Failed));
                    checkpointCommand.Parameters.Add(new SqlParameter("@correlationId", correlationId));
                    checkpointCommand.Parameters.Add(new SqlParameter("@waitingStatus", CheckpointStatuses.WaitingApproval));
                    await checkpointCommand.ExecuteNonQueryAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                return affectedRows == 1;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
    }

    public async Task<bool> TryStartExecutionAsync(Guid checkpointId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = """
            UPDATE dbo.AgentCheckpoint
            SET Status = @executingStatus
            WHERE CheckpointId = @checkpointId
              AND Status = @readyStatus;
            """;
        command.Parameters.Add(new SqlParameter("@executingStatus", CheckpointStatuses.Executing));
        command.Parameters.Add(new SqlParameter("@checkpointId", checkpointId));
        command.Parameters.Add(new SqlParameter("@readyStatus", CheckpointStatuses.ReadyToResume));

        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public Task CompleteExecutionAsync(Guid checkpointId, CancellationToken cancellationToken = default) =>
        UpdateCheckpointStatusAsync(checkpointId, CheckpointStatuses.Completed, cancellationToken);

    public Task FailExecutionAsync(Guid checkpointId, CancellationToken cancellationToken = default) =>
        UpdateCheckpointStatusAsync(checkpointId, CheckpointStatuses.Failed, cancellationToken);

    private async Task UpdateCheckpointStatusAsync(
        Guid checkpointId,
        string status,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = """
            UPDATE dbo.AgentCheckpoint
            SET Status = @status
            WHERE CheckpointId = @checkpointId;
            """;
        command.Parameters.Add(new SqlParameter("@status", status));
        command.Parameters.Add(new SqlParameter("@checkpointId", checkpointId));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new SqlConnectionNotConfiguredException();
        }

        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task InsertApprovalAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        ApprovalRequestRecord approval,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = 2;
        command.CommandText = """
            INSERT INTO dbo.ApprovalRequest (
                ApprovalId,
                CorrelationId,
                RequestedAtUtc,
                RequestedByUserId,
                Status,
                ToolName,
                ParamsHash,
                ParamsJson,
                RiskSummary,
                ApprovedAtUtc,
                ApprovedByUserId
            )
            VALUES (
                @approvalId,
                @correlationId,
                @requestedAtUtc,
                @requestedByUserId,
                @status,
                @toolName,
                @paramsHash,
                @paramsJson,
                @riskSummary,
                NULL,
                NULL
            );
            """;
        command.Parameters.Add(new SqlParameter("@approvalId", approval.ApprovalId));
        command.Parameters.Add(new SqlParameter("@correlationId", approval.CorrelationId));
        command.Parameters.Add(new SqlParameter("@requestedAtUtc", approval.RequestedAtUtc));
        command.Parameters.Add(new SqlParameter("@requestedByUserId", approval.RequestedByUserId));
        command.Parameters.Add(new SqlParameter("@status", approval.Status));
        command.Parameters.Add(new SqlParameter("@toolName", approval.ToolName));
        command.Parameters.Add(new SqlParameter("@paramsHash", approval.ParamsHash));
        command.Parameters.Add(new SqlParameter("@paramsJson", approval.ParamsJson));
        command.Parameters.Add(new SqlParameter("@riskSummary", approval.RiskSummary));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertCheckpointAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        AgentCheckpointRecord checkpoint,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = 2;
        command.CommandText = """
            INSERT INTO dbo.AgentCheckpoint (
                CheckpointId,
                CorrelationId,
                CreatedAtUtc,
                Status,
                ConversationSummary,
                PendingActionJson,
                LastToolCallId
            )
            VALUES (
                @checkpointId,
                @correlationId,
                @createdAtUtc,
                @status,
                @conversationSummary,
                @pendingActionJson,
                NULL
            );
            """;
        command.Parameters.Add(new SqlParameter("@checkpointId", checkpoint.CheckpointId));
        command.Parameters.Add(new SqlParameter("@correlationId", checkpoint.CorrelationId));
        command.Parameters.Add(new SqlParameter("@createdAtUtc", checkpoint.CreatedAtUtc));
        command.Parameters.Add(new SqlParameter("@status", checkpoint.Status));
        command.Parameters.Add(new SqlParameter("@conversationSummary", checkpoint.ConversationSummary));
        command.Parameters.Add(new SqlParameter("@pendingActionJson", checkpoint.PendingActionJson ?? string.Empty));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static ApprovalRequestRecord ReadApproval(SqlDataReader reader) =>
        new()
        {
            ApprovalId = reader.GetGuid(0),
            CorrelationId = reader.GetGuid(1),
            RequestedAtUtc = reader.GetDateTime(2),
            RequestedByUserId = reader.GetString(3),
            Status = reader.GetString(4),
            ToolName = reader.GetString(5),
            ParamsHash = reader.GetString(6),
            ParamsJson = reader.GetString(7),
            RiskSummary = reader.GetString(8),
            ApprovedAtUtc = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
            ApprovedByUserId = reader.IsDBNull(10) ? null : reader.GetString(10)
        };

    private const string ReadyApprovalsSql = """
        SELECT TOP (50)
            ar.ApprovalId AS ApprovalId,
            ar.CorrelationId AS ApprovalCorrelationId,
            ar.RequestedAtUtc AS RequestedAtUtc,
            ar.RequestedByUserId AS RequestedByUserId,
            ar.Status AS ApprovalStatus,
            ar.ToolName AS ToolName,
            ar.ParamsHash AS ParamsHash,
            ar.ParamsJson AS ParamsJson,
            ar.RiskSummary AS RiskSummary,
            ar.ApprovedAtUtc AS ApprovedAtUtc,
            ar.ApprovedByUserId AS ApprovedByUserId,
            cp.CheckpointId AS CheckpointId,
            cp.CorrelationId AS CheckpointCorrelationId,
            cp.CreatedAtUtc AS CheckpointCreatedAtUtc,
            cp.Status AS CheckpointStatus,
            cp.ConversationSummary AS ConversationSummary,
            cp.PendingActionJson AS PendingActionJson,
            cp.LastToolCallId AS LastToolCallId
        FROM dbo.ApprovalRequest AS ar
        INNER JOIN dbo.AgentCheckpoint AS cp
            ON cp.CorrelationId = ar.CorrelationId
        WHERE ar.Status = @approvalStatus
          AND cp.Status = @checkpointStatus
          AND ar.ToolName = @toolName
        ORDER BY ar.ApprovedAtUtc DESC, cp.CreatedAtUtc DESC;
        """;

    private const string ReadyApprovalByIdSql = """
        SELECT TOP (1)
            ar.ApprovalId AS ApprovalId,
            ar.CorrelationId AS ApprovalCorrelationId,
            ar.RequestedAtUtc AS RequestedAtUtc,
            ar.RequestedByUserId AS RequestedByUserId,
            ar.Status AS ApprovalStatus,
            ar.ToolName AS ToolName,
            ar.ParamsHash AS ParamsHash,
            ar.ParamsJson AS ParamsJson,
            ar.RiskSummary AS RiskSummary,
            ar.ApprovedAtUtc AS ApprovedAtUtc,
            ar.ApprovedByUserId AS ApprovedByUserId,
            cp.CheckpointId AS CheckpointId,
            cp.CorrelationId AS CheckpointCorrelationId,
            cp.CreatedAtUtc AS CheckpointCreatedAtUtc,
            cp.Status AS CheckpointStatus,
            cp.ConversationSummary AS ConversationSummary,
            cp.PendingActionJson AS PendingActionJson,
            cp.LastToolCallId AS LastToolCallId
        FROM dbo.ApprovalRequest AS ar
        INNER JOIN dbo.AgentCheckpoint AS cp
            ON cp.CorrelationId = ar.CorrelationId
        WHERE ar.ApprovalId = @approvalId;
        """;

    private static ReadyApprovalRecord ReadReadyApproval(SqlDataReader reader) =>
        new()
        {
            Approval = new ApprovalRequestRecord
            {
                ApprovalId = reader.GetGuid(0),
                CorrelationId = reader.GetGuid(1),
                RequestedAtUtc = reader.GetDateTime(2),
                RequestedByUserId = reader.GetString(3),
                Status = reader.GetString(4),
                ToolName = reader.GetString(5),
                ParamsHash = reader.GetString(6),
                ParamsJson = reader.GetString(7),
                RiskSummary = reader.GetString(8),
                ApprovedAtUtc = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                ApprovedByUserId = reader.IsDBNull(10) ? null : reader.GetString(10)
            },
            Checkpoint = new AgentCheckpointRecord
            {
                CheckpointId = reader.GetGuid(11),
                CorrelationId = reader.GetGuid(12),
                CreatedAtUtc = reader.GetDateTime(13),
                Status = reader.GetString(14),
                ConversationSummary = reader.IsDBNull(15) ? string.Empty : reader.GetString(15),
                PendingActionJson = reader.IsDBNull(16) ? null : reader.GetString(16),
                LastToolCallId = reader.IsDBNull(17) ? null : reader.GetGuid(17)
            }
        };
}

public sealed class ApprovalPersistenceException : Exception
{
    public ApprovalPersistenceException(Exception innerException)
        : base("Approval persistence failed.", innerException)
    {
    }
}
