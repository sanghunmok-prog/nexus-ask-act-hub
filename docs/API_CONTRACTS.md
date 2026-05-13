# API Contracts

This file captures the repository contract for REST, SSE, and query-shape interfaces.

For current merged-state implementation conventions, also read `docs/IMPLEMENTATION_NOTES.md`.

## Orchestrator REST endpoints

- `POST /api/chat/stream`
- `GET /api/approvals/pending`
- `POST /api/approvals/{approvalId}/approve`
- `POST /api/approvals/{approvalId}/reject`
- `GET /api/audit/recent`
- `GET /api/audit?correlationId=...`
- `POST /api/documents/upload`
- `POST /api/documents/{docId}/ingest`
- `GET /api/health`

## Document upload contract

### Endpoint
`POST /api/documents/upload`

### Request
`multipart/form-data`

Required form field:
- `file`

Optional form fields:
- `title`
- `sourceName`

Supported file types:
- `.txt`
- `.md`
- text-based `.pdf`

If `title` is omitted, the Orchestrator derives it from the file name without extension. If `sourceName` is omitted, the Orchestrator uses the uploaded file name.

### Success response

PR-08 stores chunks before embeddings exist, so the status is `ChunkedPendingEmbedding`.

```json
{
  "docId": "00000000-0000-0000-0000-000000000000",
  "title": "Shipping Delay Policy",
  "sourceName": "shipping-policy.md",
  "status": "ChunkedPendingEmbedding",
  "chunkCount": 3,
  "chunks": [
    {
      "chunkIndex": 0,
      "charStart": 0,
      "charEnd": 1000,
      "preview": "First 200 characters..."
    }
  ]
}
```

### Error response

```json
{
  "code": "DOCUMENT_TEXT_EMPTY",
  "message": "Document text could not be extracted.",
  "errors": []
}
```

Sanitized error codes:
- `DOCUMENT_FILE_REQUIRED`
- `DOCUMENT_FILE_EMPTY`
- `DOCUMENT_TYPE_NOT_SUPPORTED`
- `DOCUMENT_TEXT_EMPTY`
- `DOCUMENT_INGESTION_FAILED`
- `SQL_CONNECTION_NOT_CONFIGURED`

## Document ingest contract

### Endpoint
`POST /api/documents/{docId}/ingest`

### Request
No request body is required. `docId` comes from the route.

The endpoint completes staged ingestion for a document already uploaded through `POST /api/documents/upload`. It embeds chunks where `PolicyChunks.Embedding IS NULL` and skips chunks that already have embeddings.

### Success response

```json
{
  "docId": "00000000-0000-0000-0000-000000000000",
  "status": "Embedded",
  "embeddingProvider": "mock-token-hashing",
  "embeddingDimension": 1536,
  "embeddedChunkCount": 1,
  "skippedChunkCount": 0
}
```

### Already embedded response

```json
{
  "docId": "00000000-0000-0000-0000-000000000000",
  "status": "AlreadyEmbedded",
  "embeddingProvider": "mock-token-hashing",
  "embeddingDimension": 1536,
  "embeddedChunkCount": 0,
  "skippedChunkCount": 1
}
```

The public response does not include embedding vectors, chunk text, generated SQL, or connection strings.

### Error response

```json
{
  "code": "DOCUMENT_NOT_FOUND",
  "message": "Document was not found.",
  "errors": []
}
```

Sanitized error codes:
- `DOCUMENT_NOT_FOUND`
- `DOCUMENT_CHUNKS_NOT_FOUND`
- `SQL_CONNECTION_NOT_CONFIGURED`
- `DOCUMENT_EMBEDDING_FAILED`

## Approval contract

PR-14 adds the approval UI and internal ReadyToResume checkpoint scaffold. Approve and reject endpoints only record decisions and update checkpoint status; they do not resume workflows, expose a public resume endpoint, execute GitHub, or execute any external action in PR-14. `resumeAvailable` remains `false`.

### Pending approvals

`GET /api/approvals/pending`

Returns pending approvals ordered by `requestedAtUtc` descending.

```json
{
  "approvals": [
    {
      "approvalId": "00000000-0000-0000-0000-000000000000",
      "correlationId": "11111111-1111-1111-1111-111111111111",
      "requestedAtUtc": "2026-04-02T03:00:00Z",
      "requestedByUserId": "demo-user",
      "status": "Pending",
      "toolName": "github.create_issue",
      "paramsHash": "sha256-hex",
      "params": {
        "repo": "sanghunmok-prog/nexus-ask-act-hub",
        "title": "Delayed shipments review",
        "labels": ["nexus-demo"]
      },
      "riskSummary": "Creates a GitHub issue. No action will run until approved."
    }
  ]
}
```

### Approve

`POST /api/approvals/{approvalId}/approve`

Uses `X-Nexus-UserId` when present; otherwise records `demo-user`.

```json
{
  "approvalId": "00000000-0000-0000-0000-000000000000",
  "status": "Approved",
  "checkpointStatus": "ReadyToResume",
  "resumeAvailable": false,
  "message": "Approval recorded. The checkpoint is marked ready for future resume. No external action has been executed."
}
```

Approval marks a related `WaitingApproval` checkpoint as `ReadyToResume` when present. `ReadyToResume` is internal future execution readiness only. It does not expose execution to the user and does not execute the pending action.

### Reject

`POST /api/approvals/{approvalId}/reject`

Rejection records `Status = "Rejected"` and marks a related `WaitingApproval` checkpoint as `Failed` when present.

```json
{
  "approvalId": "00000000-0000-0000-0000-000000000000",
  "status": "Rejected",
  "checkpointStatus": "Failed",
  "resumeAvailable": false,
  "message": "Approval rejected. No external action was executed."
}
```

Reject marks a related `WaitingApproval` checkpoint as `Failed` when present.

### Approval errors

```json
{
  "code": "APPROVAL_NOT_FOUND",
  "message": "Approval request was not found.",
  "errors": []
}
```

Error codes:
- `APPROVAL_NOT_FOUND`
- `APPROVAL_NOT_PENDING`
- `APPROVAL_PERSISTENCE_FAILED`

## Chat stream contract

### Endpoint
`POST /api/chat/stream`

### Request body
Current minimal request body:

```json
{
  "prompt": "Show delayed shipments last 30 days and the policy that applies."
}
```

### Response transport
- `Content-Type: text/event-stream`
- This is a POST-based SSE endpoint.
- The current frontend consumption pattern is `fetch + ReadableStream`.
- Do not assume `EventSource` as the primary client for this endpoint because the current contract is POST-based.

### PR-14 behavior

`POST /api/chat/stream` uses the Orchestrator agent runtime. In default `LLM_MODE=mock`, the deterministic planner calls the Toolbelt HTTP shims for:
- `docs.search`
- `db.get_schema_summary`
- `db.query_readonly`

After `docs.search` returns a top result, the runtime may dynamically call `docs.get_chunk` with the result `chunkId` or `citationId` to load full citation text for composition. This dynamic call is runtime behavior, not a static mock planner step.

PR-12 `assistant.message` contains the final deterministic hybrid answer for the read path, including delayed order rows, relevant policy text, citations, and summary counts. It does not call a live LLM.

For action prompts containing `create` plus `issue` or `ticket`, the runtime creates a pending `ApprovalRequest` and `AgentCheckpoint` for `github.create_issue`, emits `approval.required`, and stops. Initial checkpoint status is `WaitingApproval`.

PR-14 approval decisions update the related checkpoint only:
- approve: `WaitingApproval` -> `ReadyToResume`
- reject: `WaitingApproval` -> `Failed`

PR-14 does not call GitHub, execute Toolbelt action tools, resume workflow execution, or add a public resume endpoint. PR-16 will add GitHub create issue execution.

### SSE event types

Allowed PR-14 event types:
- `workflow.started`
- `tool.call`
- `tool.result`
- `checkpoint.saved`
- `approval.required`
- `assistant.message`
- `error`
- `done`

### Repository envelope decision

Use a single event envelope shape:

```json
{
  "eventType": "workflow.started",
  "correlationId": "GUID",
  "timestampUtc": "2026-04-02T03:00:00Z",
  "payload": {}
}
```

### Minimal payload guidance

workflow.started
```json
{
  "prompt": "Show delayed shipments..."
}
```

tool.call
```json
{
  "toolName": "docs.search",
  "sanitizedArgs": {
    "query": "delayed shipping policy escalation carrier",
    "topK": 5
  },
  "requiresApproval": false
}
```

tool.result
```json
{
  "toolName": "docs.search",
  "resultCount": 1,
  "topResult": {
    "citationId": "doc-guid:0",
    "sourceName": "ShippingPolicy.pdf",
    "title": "Shipping Delay Policy"
  },
  "result": {}
}
```

checkpoint.saved
```json
{
  "checkpointId": "22222222-2222-2222-2222-222222222222",
  "approvalId": "00000000-0000-0000-0000-000000000000",
  "status": "WaitingApproval"
}
```

approval.required
```json
{
  "approvalId": "00000000-0000-0000-0000-000000000000",
  "toolName": "github.create_issue",
  "riskSummary": "Creates a GitHub issue. No action will run until approved.",
  "params": {
    "repo": "sanghunmok-prog/nexus-ask-act-hub",
    "title": "Delayed shipments review",
    "labels": ["nexus-demo"]
  }
}
```

assistant.message
```json
{
  "message": "## Delayed orders\n5 delayed orders were returned by the current demo query.\n\n| OrderId | Status | Carrier | Expected ship date | Actual ship date | Delay reason |\n| --- | --- | --- | --- | --- | --- |\n| 11 | Delayed | USPS | 2026-02-01T00:00:00Z | — | Carrier exception |\n\n## Relevant policy\nEscalate delayed carrier shipments according to the cited policy text.\n\n## Citations\n[1] Shipping Delay Policy — nexus-shipping-policy.md — chunk 0",
  "citations": [
    {
      "citationId": "doc-guid:0",
      "title": "Shipping Delay Policy",
      "sourceName": "nexus-shipping-policy.md",
      "chunkIndex": 0
    }
  ],
  "summary": {
    "sqlRowCount": 5,
    "documentResultCount": 1,
    "citationCount": 1
  }
}
```

error
```json
{
  "code": "TOOLBELT_CALL_FAILED",
  "message": "Toolbelt call failed.",
  "retryable": false
}
```

done
```json
{
  "success": true
}
```

Current PR-12 happy-path sequence when `docs.search` has a top result:

- `workflow.started`
- `tool.call` / `tool.result` for `docs.search`
- `tool.call` / `tool.result` for `docs.get_chunk`
- `tool.call` / `tool.result` for `db.get_schema_summary`
- `tool.call` / `tool.result` for `db.query_readonly`
- `assistant.message`
- `done`

When `docs.search` returns zero results, `docs.get_chunk` is not called and the final answer includes a no-policy-found section.

Current PR-14 action-intent sequence:

- `workflow.started`
- `tool.call` for `github.create_issue` intent with `requiresApproval = true`
- `checkpoint.saved`
- `approval.required`
- `assistant.message`
- `done`

This preserves the POST SSE contract while replacing hard-coded mock events with runtime tool orchestration.

MCP tool contracts
db.get_schema_summary

PR-06 local HTTP shim:

`GET /api/tools/db/schema-summary`

Output:

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
db.query_readonly

PR-07 local HTTP shim:

`POST /api/tools/db/query-readonly`

The following StructuredQuery shape is locked for PR-05+ and should be treated as the authoritative query contract going forward.

Input shape:

{
  "table": "Orders",
  "select": [
    "OrderId",
    "Status",
    "ExpectedShipDateUtc",
    "ActualShipDateUtc",
    "Carrier"
  ],
  "filters": [
    { "column": "Status", "op": "eq", "value": "Delayed" },
    {
      "column": "ExpectedShipDateUtc",
      "op": "between",
      "value": "2026-01-01T00:00:00Z",
      "value2": "2026-01-31T23:59:59Z"
    }
  ],
  "orderBy": [
    { "column": "ExpectedShipDateUtc", "dir": "desc" }
  ],
  "limit": 50
}

Locked conventions for this shape:

filters combine with AND only
limit is required
value2 is used only for between

Success output:

```json
{
  "rowCount": 5,
  "rows": [
    {
      "OrderId": 11,
      "Status": "Delayed",
      "ExpectedShipDateUtc": "2026-01-20T17:00:00",
      "ActualShipDateUtc": null,
      "Carrier": "USPS"
    }
  ]
}
```

Validation error output:

```json
{
  "code": "QUERY_VALIDATION_FAILED",
  "message": "StructuredQuery failed validation.",
  "errors": [
    "Select column 'InternalCost' is not allowlisted."
  ]
}
```
docs.search

PR-10 local HTTP shim:

`POST /api/tools/docs/search`

Input:

```json
{
  "query": "delayed shipping policy escalation carrier",
  "topK": 5
}
```

`topK` defaults to `5` when omitted or null. `topK <= 0` is invalid. `topK > 20` is clamped to `20`.

Output:

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
      "snippet": "When an order is delayed, the shipping team must review carrier status...",
      "distance": 0.21
    }
  ]
}
```

`docs.search` returns citation-ready snippets and metadata only. It does not return full chunk text, embedding vectors, raw SQL, or connection strings.

Validation error output:

```json
{
  "code": "DOCS_QUERY_INVALID",
  "message": "Document search query is invalid.",
  "errors": [
    "Query is required."
  ]
}
```

docs.get_chunk

PR-10 local HTTP shim:

`POST /api/tools/docs/get-chunk`

Input by chunk id:

```json
{
  "chunkId": "11111111-1111-1111-1111-111111111111"
}
```

Input by citation id:

```json
{
  "citationId": "590c239e-47b1-4b51-8641-368e76c6ecd0:0"
}
```

Exactly one of `chunkId` or `citationId` is required. `citationId` uses `{docId}:{chunkIndex}`.

Output:

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

`docs.get_chunk` returns full chunk text only for explicit chunk lookup. It does not return embedding vectors, raw SQL, or connection strings.

Validation error output:

```json
{
  "code": "DOCS_CHUNK_LOOKUP_INVALID",
  "message": "Document chunk lookup is invalid.",
  "errors": [
    "Provide exactly one of chunkId or citationId."
  ]
}
```

Not found output:

```json
{
  "code": "DOCS_CHUNK_NOT_FOUND",
  "message": "Document chunk was not found.",
  "errors": []
}
```

github.create_issue

Future PR-16 Toolbelt action contract. This is not executed or exposed as an approval-driven action in PR-14.

Input:

{
  "repo": "org/repo",
  "title": "Delayed Shipments: 30-day review",
  "body": "...",
  "labels": ["nexus-demo"]
}

Output:

{
  "issueUrl": "https://github.com/.../issues/123",
  "issueNumber": 123
}
