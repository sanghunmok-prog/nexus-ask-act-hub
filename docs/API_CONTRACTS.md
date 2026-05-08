# API Contracts

This file captures the repository contract for REST, SSE, and query-shape interfaces.

For current merged-state implementation conventions, also read `docs/IMPLEMENTATION_NOTES.md`.

## Orchestrator REST endpoints

- `POST /api/chat/stream`
- `GET /api/approvals/pending`
- `POST /api/approvals/{id}/approve`
- `POST /api/approvals/{id}/reject`
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

## Chat stream contract

### Endpoint
`POST /api/chat/stream`

### Request body
Current minimal request body:

```json
{
  "prompt": "Show delayed shipments last 30 days and the policy that applies."
}
Response transport
Content-Type: text/event-stream
This is a POST-based SSE endpoint
The current frontend consumption pattern is fetch + ReadableStream
Do not assume EventSource as the primary client for this endpoint because the current contract is POST-based
SSE event types
workflow.started
tool.call
tool.result
checkpoint.saved
approval.required
approval.status
workflow.resumed
assistant.message
error
done
Repository envelope decision

Use a single event envelope shape:

{
  "eventType": "workflow.started",
  "correlationId": "GUID",
  "timestampUtc": "2026-04-02T03:00:00Z",
  "payload": {}
}
Minimal payload guidance
workflow.started
{
  "prompt": "Show delayed shipments..."
}
tool.call
{
  "toolName": "docs.search",
  "sanitizedArgs": {
    "query": "delayed shipments policy",
    "topK": 3
  },
  "requiresApproval": false
}
tool.result
{
  "toolName": "docs.search",
  "rowCount": 0,
  "citationCount": 1,
  "summary": "Returned 1 mock policy citation"
}
checkpoint.saved
{
  "checkpointId": "GUID",
  "approvalId": "GUID"
}
approval.required
{
  "approvalId": "GUID",
  "toolName": "github.create_issue",
  "riskSummary": "Creates a GitHub issue in the configured repo"
}
approval.status
{
  "approvalId": "GUID",
  "status": "Approved"
}
workflow.resumed
{
  "checkpointId": "GUID"
}
assistant.message
{
  "message": "Merged answer text",
  "citations": [
    {
      "citationId": "doc-guid:12",
      "sourceName": "ShippingPolicy.pdf",
      "snippet": "..."
    }
  ]
}
error
{
  "code": "QUERY_VALIDATION_FAILED",
  "message": "Column is not allowlisted",
  "retryable": true
}
done
{
  "success": true
}
Current minimal mock sequence (PR-03 / PR-04)

The current mock happy-path sequence is:

workflow.started
tool.call
tool.result
assistant.message
done

This is intentionally minimal for the current merged implementation.

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

Input:

{
  "query": "delayed shipments policy",
  "topK": 5
}

Output:

{
  "results": [
    {
      "citationId": "doc-guid:12",
      "sourceName": "ShippingPolicy.pdf",
      "snippet": "...",
      "distance": 0.21
    }
  ]
}
docs.get_chunk

Input:

{
  "citationId": "doc-guid:12"
}
github.create_issue

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
