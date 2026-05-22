# Architecture

NEXUS is organized around a narrow Orchestrator + Toolbelt split.

The Orchestrator owns workflow, governance decisions, streaming trace events, approvals, and checkpoints. The Toolbelt owns constrained tool execution, including safe SQL reads, document retrieval, and GitHub issue creation.

## Component Architecture

```mermaid
flowchart LR
    User[User] --> Web[Nexus.Web<br/>Angular]
    Web --> Orchestrator[Nexus.OrchestratorApi<br/>API, workflow, SSE, approvals]
    Orchestrator --> SQL[(SQL Server<br/>orders, docs, approvals, checkpoints)]
    Orchestrator --> Toolbelt[Nexus.Mcp.Toolbelt<br/>safe tools]
    Toolbelt --> SQL
    Toolbelt --> GitHub[GitHub Issues API]
    Orchestrator -. sanitized trace .-> Web
```

## Runtime Services

| Service | Responsibility |
|---|---|
| `Nexus.Web` | Angular UI for prompt input, assistant answer, trace timeline, pending approvals, and ready-to-execute actions. |
| `Nexus.OrchestratorApi` | Main API boundary. Owns prompt workflow, POST SSE, approvals, checkpoints, document upload/ingestion, and explicit execution coordination. |
| `Nexus.Mcp.Toolbelt` | Tool execution host. Exposes SQL read, document search, chunk lookup, and GitHub issue tools. |
| SQL Server | Stores business data, policy documents, chunks, approvals, checkpoints, and audit records. |
| GitHub Issues API | External write target for the approved `github.create_issue` action. |

## Project Responsibility Map

| Project | Domain |
|---|---|
| `src/Nexus.Web` | Frontend |
| `src/Nexus.OrchestratorApi` | API + application workflow |
| `src/Nexus.Mcp.Toolbelt` | Infrastructure-facing tool execution |
| `src/Nexus.Contracts` | Shared contracts |
| `src/Nexus.QuerySafety` | SQL read safety |
| `src/Nexus.Embeddings` | Embedding provider abstraction |
| `src/Nexus.AppHost` | Local orchestration |
| `src/Nexus.ServiceDefaults` | Shared service defaults |

## Boundary Rules

- Browser talks to Orchestrator, not Toolbelt.
- Orchestrator calls Toolbelt through `NEXUS_TOOLBELT_BASE_URL`.
- GitHub token belongs only to Toolbelt.
- Orchestrator owns approval policy.
- Toolbelt executes tools but does not decide governance.
- SQL reads use `StructuredQuery`, allowlists, and parameterized compiler output.
- External writes require approval and explicit execute.

## Ask Path: SQL + Policy Documents

```mermaid
sequenceDiagram
    participant U as User
    participant W as Nexus.Web
    participant O as Orchestrator
    participant T as Toolbelt
    participant DB as SQL Server

    U->>W: Ask delayed-order policy question
    W->>O: POST /api/chat/stream
    O-->>W: workflow.started
    O->>T: docs.search
    T->>DB: exact vector search
    T-->>O: citation metadata
    O->>T: docs.get_chunk
    T->>DB: load cited chunk
    T-->>O: cited policy text
    O->>T: db.get_schema_summary
    T-->>O: allowlisted schema summary
    O->>T: db.query_readonly StructuredQuery
    T->>DB: parameterized SELECT
    T-->>O: delayed order rows
    O-->>W: assistant.message with rows + citations
    O-->>W: done
```

## Recover Path: Bounded Read Correction

```mermaid
sequenceDiagram
    participant W as Nexus.Web
    participant O as Orchestrator
    participant T as Toolbelt

    W->>O: POST /api/chat/stream
    O->>T: db.query_readonly with recoverable schema mismatch
    T-->>O: sanitized validation failure
    O-->>W: tool.result failure summary
    O-->>W: tool.retry attempt 2 of 2
    O->>O: deterministic StructuredQuery correction
    O->>T: corrected db.query_readonly
    T-->>O: success
    O-->>W: assistant.message
    O-->>W: done
```

Rules:

- Retry applies only to recoverable read-path schema/allowlist failures.
- Retry budget is one correction retry.
- At most two `db.query_readonly` attempts are allowed.
- Correction never bypasses Toolbelt validation or QuerySafety.
- Write actions are never retried automatically.

## Govern Path: Approval-Gated GitHub Issue

```mermaid
sequenceDiagram
    participant U as User
    participant W as Nexus.Web
    participant O as Orchestrator
    participant DB as SQL Server
    participant T as Toolbelt
    participant G as GitHub

    U->>W: Ask to create GitHub issue
    W->>O: POST /api/chat/stream
    O-->>W: tool.call github.create_issue requiresApproval=true
    O->>DB: insert ApprovalRequest
    O->>DB: insert AgentCheckpoint WaitingApproval
    O-->>W: approval.required
    O-->>W: done

    U->>W: Approve
    W->>O: POST /api/approvals/{approvalId}/approve
    O->>DB: Approval Approved
    O->>DB: Checkpoint ReadyToResume
    O-->>W: approval recorded; not executed

    U->>W: Execute
    W->>O: POST /api/approvals/{approvalId}/execute
    O->>DB: atomic ReadyToResume -> Executing claim
    O->>T: POST /api/tools/github/create-issue
    T->>G: Create issue
    G-->>T: issue number + URL
    T-->>O: issue result
    O->>DB: checkpoint Completed
    O-->>W: issue number + URL
```

Duplicate execute attempts fail with `409 Conflict` because the checkpoint is no longer `ReadyToResume`.

## Persistence Overview

```mermaid
erDiagram
    Orders {
        int OrderId PK
        datetime CreatedAtUtc
        string Status
        datetime ExpectedShipDateUtc
        datetime ActualShipDateUtc
        string Carrier
        string DelayReason
    }

    PolicyDocuments {
        guid DocId PK
        string Title
        string SourceName
        datetime UploadedAtUtc
    }

    PolicyChunks {
        guid ChunkId PK
        guid DocId FK
        int ChunkIndex
        string ChunkText
        vector Embedding
        string MetadataJson
    }

    ApprovalRequest {
        guid ApprovalId PK
        guid CorrelationId
        datetime RequestedAtUtc
        string RequestedByUserId
        string Status
        string ToolName
        string ParamsHash
        string ParamsJson
        string RiskSummary
        datetime ApprovedAtUtc
        string ApprovedByUserId
    }

    AgentCheckpoint {
        guid CheckpointId PK
        guid CorrelationId
        datetime CreatedAtUtc
        string Status
        string ConversationSummary
        string PendingActionJson
        guid LastToolCallId
    }

    AuditLog {
        guid AuditId PK
        guid CorrelationId
        datetime OccurredAtUtc
        string ActorUserId
        string EventType
        string PayloadJson
    }

    PolicyDocuments ||--o{ PolicyChunks : contains
```

## Design Tradeoffs

| Decision | Reason |
|---|---|
| POST SSE with `fetch + ReadableStream` | The chat stream accepts a JSON request body, so `EventSource` is not the right primary client pattern. |
| Mock-first local mode | Keeps demos and tests deterministic without requiring live model credentials. |
| StructuredQuery instead of raw SQL | Enforces allowlists, single-table MVP reads, and parameterized compiler output. |
| Approve is not execute | Separates human decision from external side effect. |
| Toolbelt-only GitHub token | Keeps external write credentials out of Orchestrator. |
| No write retry | Prevents duplicate external side effects. |
