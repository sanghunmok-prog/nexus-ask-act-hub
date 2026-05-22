# Data Model

NEXUS stores business data, policy retrieval data, and governance state in SQL Server.

## Table Overview

| Table | Purpose |
|---|---|
| `Orders` | Demo business data for delayed shipment questions. |
| `PolicyDocuments` | Uploaded policy document metadata. |
| `PolicyChunks` | Chunked document text and embeddings for retrieval. |
| `ApprovalRequest` | Human approval record for external actions. |
| `AgentCheckpoint` | Paused or executable workflow state. |
| `AuditLog` | Governance event log reserved for audit review. |

## Migration Strategy

The schema is compatible with an EF Core migration workflow. Use EF Core migrations for application-owned schema evolution and keep deterministic local demo seed scripts under `infra/docker/sql/`.

Recommended migration command shape:

```bash
dotnet ef database update \
  --project src/Nexus.OrchestratorApi \
  --startup-project src/Nexus.OrchestratorApi
```

If migrations live in a dedicated infrastructure project in your branch, use that project for `--project`.

## Business Tables

### `Orders`

```sql
CREATE TABLE dbo.Orders (
  OrderId INT IDENTITY(1,1) PRIMARY KEY,
  CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
  Status NVARCHAR(50) NOT NULL,
  ExpectedShipDateUtc DATETIME2 NOT NULL,
  ActualShipDateUtc DATETIME2 NULL,
  Carrier NVARCHAR(100) NULL,
  DelayReason NVARCHAR(200) NULL
);
```

Purpose:

- stores operational order data
- supports delayed shipment demo queries
- read through `StructuredQuery` only

## Policy Retrieval Tables

### `PolicyDocuments`

```sql
CREATE TABLE dbo.PolicyDocuments (
  DocId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
  Title NVARCHAR(200) NOT NULL,
  SourceName NVARCHAR(200) NOT NULL,
  UploadedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);
```

### `PolicyChunks`

```sql
CREATE TABLE dbo.PolicyChunks (
  ChunkId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
  DocId UNIQUEIDENTIFIER NOT NULL,
  ChunkIndex INT NOT NULL,
  ChunkText NVARCHAR(MAX) NOT NULL,
  Embedding VECTOR(1536) NULL,
  MetadataJson NVARCHAR(MAX) NOT NULL,
  CONSTRAINT FK_PolicyChunks_Documents
    FOREIGN KEY (DocId) REFERENCES dbo.PolicyDocuments(DocId)
);
```

Notes:

- upload creates `PolicyDocuments` and `PolicyChunks`
- chunks are initially stored with `Embedding = NULL`
- ingest populates deterministic mock embeddings
- `docs.search` searches chunks where embeddings exist
- `docs.get_chunk` loads full text by `chunkId` or citation id

## Governance Tables

### `ApprovalRequest`

```sql
CREATE TABLE dbo.ApprovalRequest (
  ApprovalId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
  CorrelationId UNIQUEIDENTIFIER NOT NULL,
  RequestedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
  RequestedByUserId NVARCHAR(100) NOT NULL,
  Status NVARCHAR(20) NOT NULL,
  ToolName NVARCHAR(200) NOT NULL,
  ParamsHash NVARCHAR(128) NOT NULL,
  ParamsJson NVARCHAR(MAX) NOT NULL,
  RiskSummary NVARCHAR(1000) NOT NULL,
  ApprovedAtUtc DATETIME2 NULL,
  ApprovedByUserId NVARCHAR(100) NULL
);
```

Status values:

| Status | Meaning |
|---|---|
| `Pending` | Waiting for a human decision. |
| `Approved` | Human approval recorded. |
| `Rejected` | Human rejection recorded. |

`ParamsHash` binds the approval decision to deterministic action parameters.

### `AgentCheckpoint`

```sql
CREATE TABLE dbo.AgentCheckpoint (
  CheckpointId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
  CorrelationId UNIQUEIDENTIFIER NOT NULL,
  CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
  Status NVARCHAR(20) NOT NULL,
  ConversationSummary NVARCHAR(MAX) NOT NULL,
  PendingActionJson NVARCHAR(MAX) NULL,
  LastToolCallId UNIQUEIDENTIFIER NULL
);
```

Status values:

| Status | Meaning |
|---|---|
| `WaitingApproval` | Workflow is paused until approval or rejection. |
| `ReadyToResume` | Approval was recorded; action is ready for explicit execute. |
| `Executing` | Orchestrator atomically claimed the checkpoint and is running the action. |
| `Completed` | External action completed successfully. |
| `Failed` | Rejected or failed execution. |

Important boundary:

```text
Approve != Execute
```

Approve records the human decision and prepares the checkpoint. Execute performs the external action.

### `AuditLog`

```sql
CREATE TABLE dbo.AuditLog (
  AuditId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
  CorrelationId UNIQUEIDENTIFIER NOT NULL,
  OccurredAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
  ActorUserId NVARCHAR(100) NOT NULL,
  EventType NVARCHAR(60) NOT NULL,
  PayloadJson NVARCHAR(MAX) NOT NULL
);
```

`AuditLog` is reserved for governance/audit review workflows.

## Relationship Notes

- `PolicyDocuments` has many `PolicyChunks`.
- `ApprovalRequest` and `AgentCheckpoint` are linked by `CorrelationId`.
- `ApprovalRequest` stores the human decision.
- `AgentCheckpoint` stores executable workflow state.
- Duplicate execute is prevented by atomically claiming `ReadyToResume -> Executing`.

## Seed Data

Local demo seed scripts should create delayed order examples and policy documents suitable for the default demo prompts:

```text
Which delayed orders are most at risk, and what policy applies?
Which delayed orders need correction retry?
Create a GitHub issue for the delayed shipment findings.
```
