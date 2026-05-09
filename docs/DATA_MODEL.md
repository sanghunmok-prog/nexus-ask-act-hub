# Data Model

This file turns the master doc schema into a direct implementation reference for PR-02.

## Business tables

### Orders
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

### PolicyDocuments
```sql
CREATE TABLE dbo.PolicyDocuments (
  DocId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
  Title NVARCHAR(200) NOT NULL,
  SourceName NVARCHAR(200) NOT NULL,
  UploadedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);
```

### PolicyChunks
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

## Governance tables

### AuditLog
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

### ApprovalRequest
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

PR-13 status values:
- `Pending`
- `Approved`
- `Rejected`

PR-13 stores rejection as status only. Detailed rejection actor/time metadata can be added later through audit or schema changes.

### AgentCheckpoint
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

PR-13 status values:
- `WaitingApproval`
- `Failed`

## Notes
- VECTOR(1536) is the initial embedding dimension choice from the reference doc.
- `PolicyChunks.Embedding` is nullable during staged document ingestion.
- PR-08 upload/chunking stores extracted chunks with `Embedding = NULL`.
- PR-09 populates `PolicyChunks.Embedding` with deterministic mock embeddings.
- `PolicyChunks.Embedding` remains `VECTOR(1536)`.
- PR-10 `docs.search` searches only chunks where `Embedding IS NOT NULL`.
- PR-10 `docs.search` uses exact `VECTOR_DISTANCE` over `PolicyChunks.Embedding`.
- PR-10 `docs.get_chunk` retrieves chunk text by `ChunkId` or citation id (`DocId:ChunkIndex`) and does not require an embedding.
- PR-02 may implement this via SQL scripts first and EF migrations only if useful.
