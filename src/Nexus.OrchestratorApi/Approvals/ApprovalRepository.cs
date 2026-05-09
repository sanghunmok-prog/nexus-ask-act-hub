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

    Task<ApprovalRequestRecord?> GetApprovalAsync(Guid approvalId, CancellationToken cancellationToken = default);

    Task ApproveAsync(
        Guid approvalId,
        DateTime approvedAtUtc,
        string approvedByUserId,
        CancellationToken cancellationToken = default);

    Task RejectAsync(Guid approvalId, Guid correlationId, CancellationToken cancellationToken = default);
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

    public async Task ApproveAsync(
        Guid approvalId,
        DateTime approvedAtUtc,
        string approvedByUserId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = """
            UPDATE dbo.ApprovalRequest
            SET
                Status = @status,
                ApprovedAtUtc = @approvedAtUtc,
                ApprovedByUserId = @approvedByUserId
            WHERE ApprovalId = @approvalId
              AND Status = @pendingStatus;
            """;
        command.Parameters.Add(new SqlParameter("@status", ApprovalStatuses.Approved));
        command.Parameters.Add(new SqlParameter("@approvedAtUtc", approvedAtUtc));
        command.Parameters.Add(new SqlParameter("@approvedByUserId", approvedByUserId));
        command.Parameters.Add(new SqlParameter("@approvalId", approvalId));
        command.Parameters.Add(new SqlParameter("@pendingStatus", ApprovalStatuses.Pending));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RejectAsync(Guid approvalId, Guid correlationId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (transaction)
        {
            try
            {
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
                    await approvalCommand.ExecuteNonQueryAsync(cancellationToken);
                }

                await using (var checkpointCommand = connection.CreateCommand())
                {
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
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
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
}

public sealed class ApprovalPersistenceException : Exception
{
    public ApprovalPersistenceException(Exception innerException)
        : base("Approval persistence failed.", innerException)
    {
    }
}
