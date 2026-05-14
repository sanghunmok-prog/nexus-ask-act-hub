# Architecture

NEXUS is organized around a narrow Orchestrator + Toolbelt split. The Orchestrator owns workflow and governance decisions. The Toolbelt owns constrained tool execution.

## High-Level Component Architecture

```mermaid
flowchart LR
    User[User] --> Web[Nexus.Web<br/>Angular]
    Web --> Orchestrator[Nexus.OrchestratorApi<br/>workflow, SSE, approvals]
    Orchestrator --> SQL[(SQL Server<br/>orders, docs, approvals, checkpoints)]
    Orchestrator --> Toolbelt[Nexus.Mcp.Toolbelt<br/>narrow tools]
    Toolbelt --> SQL
    Toolbelt --> GitHub[GitHub Issues API]
    Orchestrator -. sanitized trace .-> Web
```

Key boundaries:

- Browser talks to Orchestrator, not Toolbelt.
- Orchestrator calls Toolbelt through `NEXUS_TOOLBELT_BASE_URL`.
- GitHub token belongs only to Toolbelt.
- SQL reads use StructuredQuery and allowlisted compiler paths.

## Read Path Flow

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

## Correction Retry Flow

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
- Retry budget is one correction retry, two total `db.query_readonly` attempts.
- Correction never bypasses Toolbelt validation or StructuredQuery safety.
- Write actions are not retried.

## Approval-Gated Action Flow

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
    O->>DB: Approval Approved, checkpoint ReadyToResume
    O-->>W: approval recorded, not executed
    U->>W: Execute
    W->>O: POST /api/approvals/{approvalId}/execute
    O->>DB: atomic ReadyToResume -> Executing claim
    O->>T: POST /api/tools/github/create-issue
    T->>G: create issue
    G-->>T: issue URL
    T-->>O: issue result
    O->>DB: checkpoint Completed
    O-->>W: issue URL
```

Duplicate execute attempts fail with `409` because the checkpoint is no longer `ReadyToResume`.
