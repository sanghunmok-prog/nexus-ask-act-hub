# API Contracts

This document defines the public contracts for NEXUS REST endpoints, POST-based SSE events, StructuredQuery, approval lifecycle, document retrieval, and Toolbelt tools.

## Orchestrator Endpoints

| Method | Endpoint | Purpose |
|---|---|---|
| `POST` | `/api/chat/stream` | Main workflow endpoint. Returns SSE events. |
| `GET` | `/api/approvals/pending` | Lists pending approval requests. |
| `GET` | `/api/approvals/ready` | Lists approved actions ready for explicit execution. |
| `POST` | `/api/approvals/{approvalId}/approve` | Approves a pending request and prepares the checkpoint. |
| `POST` | `/api/approvals/{approvalId}/reject` | Rejects a pending request and fails the checkpoint. |
| `POST` | `/api/approvals/{approvalId}/execute` | Executes an approved `github.create_issue` action. |
| `POST` | `/api/documents/upload` | Uploads a text-based policy document. |
| `POST` | `/api/documents/{docId}/ingest` | Embeds stored document chunks. |
| `GET` | `/api/health` | Health check. |

## Toolbelt Endpoints

| Method | Endpoint | Purpose |
|---|---|---|
| `GET` | `/api/tools/db/schema-summary` | Returns allowlisted schema summary. |
| `POST` | `/api/tools/db/query-readonly` | Validates and executes a read-only `StructuredQuery`. |
| `POST` | `/api/tools/docs/search` | Searches embedded policy chunks. |
| `POST` | `/api/tools/docs/get-chunk` | Loads full text for an explicit citation/chunk. |
| `POST` | `/api/tools/github/create-issue` | Creates a GitHub issue after approval and explicit execute. |
| `GET` | `/api/health` | Health check. |

## Chat Stream

### Request

```http
POST /api/chat/stream
Content-Type: application/json
```

```json
{
  "prompt": "Which delayed orders are most at risk, and what policy applies?"
}
```

### Response Transport

```text
Content-Type: text/event-stream
```

The frontend consumes this POST-based SSE stream using `fetch + ReadableStream`.

### SSE Envelope

```json
{
  "eventType": "workflow.started",
  "correlationId": "00000000-0000-0000-0000-000000000000",
  "timestampUtc": "2026-05-22T00:00:00Z",
  "payload": {}
}
```

### Event Types

| Event | Purpose |
|---|---|
| `workflow.started` | Workflow accepted and started. |
| `tool.call` | Sanitized tool call started. |
| `tool.retry` | Bounded read-path retry event. |
| `tool.result` | Sanitized tool result. |
| `checkpoint.saved` | Approval checkpoint persisted. |
| `approval.required` | External action paused for human approval. |
| `assistant.message` | Final assistant response or workflow status message. |
| `error` | Sanitized public error. |
| `done` | Stream completed. |

### Ask Path Sequence

```text
workflow.started
tool.call / tool.result: docs.search
tool.call / tool.result: docs.get_chunk
tool.call / tool.result: db.get_schema_summary
tool.call / tool.result: db.query_readonly
assistant.message
done
```

### Recover Path Sequence

```text
workflow.started
tool.call / tool.result: db.query_readonly failure
tool.retry
tool.call / tool.result: corrected db.query_readonly success
assistant.message
done
```

### Govern Path Sequence

```text
workflow.started
tool.call: github.create_issue requiresApproval=true
checkpoint.saved
approval.required
assistant.message
done
```

## StructuredQuery

User prompts never submit raw SQL. The Orchestrator produces a `StructuredQuery`, and Toolbelt validates it against the allowlist before compiling parameterized SQL.

```json
{
  "table": "Orders",
  "select": [
    "OrderId",
    "Status",
    "ExpectedShipDateUtc",
    "ActualShipDateUtc",
    "Carrier",
    "DelayReason"
  ],
  "filters": [
    {
      "column": "Status",
      "op": "eq",
      "value": "Delayed"
    }
  ],
  "orderBy": [
    {
      "column": "ExpectedShipDateUtc",
      "dir": "asc"
    }
  ],
  "limit": 50
}
```

### Allowed Operators

| Operator | Meaning |
|---|---|
| `eq` | equals |
| `neq` | not equals |
| `gte` | greater than or equal |
| `lte` | less than or equal |
| `between` | range filter using `value` and `value2` |
| `contains` | string contains only |

### Validation Rules

- table must be allowlisted
- selected columns must be allowlisted
- filter columns must be allowlisted
- order columns must be allowlisted
- `select` must not be empty
- `limit` is required
- `limit > maxLimit` is clamped
- filters combine with `AND`
- `orderBy.dir` must be `asc` or `desc`
- `contains` is allowed only for string columns
- MVP reads are single-table only
- joins and raw SQL are not supported

## `db.get_schema_summary`

```http
GET /api/tools/db/schema-summary
```

Response:

```json
{
  "tables": [
    {
      "name": "Orders",
      "columns": [
        "OrderId",
        "CreatedAtUtc",
        "Status",
        "ExpectedShipDateUtc",
        "ActualShipDateUtc",
        "Carrier",
        "DelayReason"
      ]
    }
  ]
}
```

## `db.query_readonly`

```http
POST /api/tools/db/query-readonly
Content-Type: application/json
```

Request:

```json
{
  "table": "Orders",
  "select": ["OrderId", "Status", "ExpectedShipDateUtc", "Carrier"],
  "filters": [
    { "column": "Status", "op": "eq", "value": "Delayed" }
  ],
  "orderBy": [
    { "column": "ExpectedShipDateUtc", "dir": "asc" }
  ],
  "limit": 50
}
```

Success:

```json
{
  "rowCount": 1,
  "rows": [
    {
      "OrderId": 11,
      "Status": "Delayed",
      "ExpectedShipDateUtc": "2026-02-01T00:00:00Z",
      "Carrier": "USPS"
    }
  ]
}
```

Validation failure:

```json
{
  "code": "QUERY_VALIDATION_FAILED",
  "message": "StructuredQuery failed validation.",
  "errors": [
    "Select column 'InternalCost' is not allowlisted."
  ]
}
```

## Document Upload

```http
POST /api/documents/upload
Content-Type: multipart/form-data
```

Fields:

| Field | Required | Description |
|---|---:|---|
| `file` | yes | `.txt`, `.md`, or text-based `.pdf`. |
| `title` | no | Human-readable document title. |
| `sourceName` | no | Source name shown in citations. |

Success:

```json
{
  "docId": "00000000-0000-0000-0000-000000000000",
  "title": "Shipping Delay Policy",
  "sourceName": "nexus-shipping-policy.md",
  "status": "ChunkedPendingEmbedding",
  "chunkCount": 3
}
```

## Document Ingest

```http
POST /api/documents/{docId}/ingest
```

Success:

```json
{
  "docId": "00000000-0000-0000-0000-000000000000",
  "status": "Embedded",
  "embeddingProvider": "mock-token-hashing",
  "embeddingDimension": 1536,
  "embeddedChunkCount": 3,
  "skippedChunkCount": 0
}
```

## `docs.search`

```http
POST /api/tools/docs/search
Content-Type: application/json
```

Request:

```json
{
  "query": "delayed shipping policy escalation carrier",
  "topK": 5
}
```

Success:

```json
{
  "query": "delayed shipping policy escalation carrier",
  "topK": 5,
  "resultCount": 1,
  "results": [
    {
      "citationId": "590c239e-47b1-4b51-8641-368e76c6ecd0:0",
      "docId": "590c239e-47b1-4b51-8641-368e76c6ecd0",
      "chunkId": "11111111-1111-1111-1111-111111111111",
      "chunkIndex": 0,
      "title": "Shipping Delay Policy",
      "sourceName": "nexus-shipping-policy.md",
      "snippet": "When an order is delayed...",
      "distance": 0.21
    }
  ]
}
```

## `docs.get_chunk`

```http
POST /api/tools/docs/get-chunk
Content-Type: application/json
```

Request by chunk id:

```json
{
  "chunkId": "11111111-1111-1111-1111-111111111111"
}
```

Request by citation id:

```json
{
  "citationId": "590c239e-47b1-4b51-8641-368e76c6ecd0:0"
}
```

Success:

```json
{
  "citationId": "590c239e-47b1-4b51-8641-368e76c6ecd0:0",
  "docId": "590c239e-47b1-4b51-8641-368e76c6ecd0",
  "chunkId": "11111111-1111-1111-1111-111111111111",
  "chunkIndex": 0,
  "title": "Shipping Delay Policy",
  "sourceName": "nexus-shipping-policy.md",
  "chunkText": "Full chunk text...",
  "metadata": {
    "charStart": 0,
    "charEnd": 375
  }
}
```

## Approval Lifecycle

### Pending Approvals

```http
GET /api/approvals/pending
```

```json
{
  "approvals": [
    {
      "approvalId": "00000000-0000-0000-0000-000000000000",
      "correlationId": "11111111-1111-1111-1111-111111111111",
      "requestedAtUtc": "2026-05-22T00:00:00Z",
      "requestedByUserId": "demo-user",
      "status": "Pending",
      "toolName": "github.create_issue",
      "paramsHash": "sha256-hex",
      "params": {
        "repo": "owner/repo",
        "title": "Delayed shipments review",
        "body": "Review delayed shipment findings from NEXUS.",
        "labels": ["nexus-demo"]
      },
      "riskSummary": "Creates a GitHub issue. No action will run until approved."
    }
  ]
}
```

### Approve

```http
POST /api/approvals/{approvalId}/approve
```

```json
{
  "approvalId": "00000000-0000-0000-0000-000000000000",
  "status": "Approved",
  "checkpointStatus": "ReadyToResume",
  "resumeAvailable": true,
  "message": "Approval recorded. The approved action is ready to execute. No external action has been executed yet."
}
```

Approve records the human decision. It does not execute GitHub.

### Reject

```http
POST /api/approvals/{approvalId}/reject
```

```json
{
  "approvalId": "00000000-0000-0000-0000-000000000000",
  "status": "Rejected",
  "checkpointStatus": "Failed",
  "resumeAvailable": false,
  "message": "Approval rejected. No external action was executed."
}
```

### Ready Approvals

```http
GET /api/approvals/ready
```

```json
{
  "approvals": [
    {
      "approvalId": "00000000-0000-0000-0000-000000000000",
      "checkpointId": "22222222-2222-2222-2222-222222222222",
      "checkpointStatus": "ReadyToResume",
      "toolName": "github.create_issue",
      "params": {
        "repo": "owner/repo",
        "title": "Delayed shipments review",
        "labels": ["nexus-demo"]
      },
      "executionAvailable": true
    }
  ]
}
```

### Execute

```http
POST /api/approvals/{approvalId}/execute
```

Success:

```json
{
  "approvalId": "00000000-0000-0000-0000-000000000000",
  "checkpointId": "22222222-2222-2222-2222-222222222222",
  "toolName": "github.create_issue",
  "status": "Executed",
  "checkpointStatus": "Completed",
  "issueNumber": 123,
  "issueUrl": "https://github.com/owner/repo/issues/123",
  "message": "GitHub issue created after explicit approval."
}
```

Duplicate execute:

```http
HTTP/1.1 409 Conflict
```

## `github.create_issue`

This Toolbelt endpoint is called only by Orchestrator after approval and explicit execute.

```http
POST /api/tools/github/create-issue
Content-Type: application/json
```

Request:

```json
{
  "repo": "owner/repo",
  "title": "Delayed shipments review",
  "body": "Review delayed shipment findings from NEXUS.",
  "labels": ["nexus-demo"]
}
```

Success:

```json
{
  "number": 123,
  "htmlUrl": "https://github.com/owner/repo/issues/123",
  "title": "Delayed shipments review"
}
```

Sanitized errors include:

- `GITHUB_NOT_CONFIGURED`
- `GITHUB_REPO_NOT_ALLOWED`
- `GITHUB_REPO_INVALID`
- `GITHUB_TITLE_REQUIRED`
- `GITHUB_PERMISSION_FAILED`
- `GITHUB_REPO_NOT_ACCESSIBLE`
- `GITHUB_ISSUES_DISABLED`
- `GITHUB_VALIDATION_FAILED`
- `GITHUB_TEMPORARY_FAILURE`
- `GITHUB_CREATE_ISSUE_FAILED`
