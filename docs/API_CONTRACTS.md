# API Contracts

This file captures the repository contract for REST, SSE, and tool interfaces.

## Orchestrator REST endpoints
- POST /api/chat/stream
- GET /api/approvals/pending
- POST /api/approvals/{id}/approve
- POST /api/approvals/{id}/reject
- GET /api/audit/recent
- GET /api/audit?correlationId=...
- POST /api/documents/upload
- POST /api/documents/{docId}/ingest
- GET /api/health

## SSE event types
- workflow.started
- tool.call
- tool.result
- checkpoint.saved
- approval.required
- approval.status
- workflow.resumed
- assistant.message
- error
- done

## Repository envelope decision
Use a single event envelope shape for PR-03 and later:

```json
{
  "eventType": "workflow.started",
  "correlationId": "GUID",
  "timestampUtc": "2026-04-02T03:00:00Z",
  "payload": {}
}
```

## Minimal payload guidance

### workflow.started
```json
{
  "prompt": "Show delayed shipments..."
}
```

### tool.call
```json
{
  "toolName": "docs.search",
  "sanitizedArgs": {
    "query": "delayed shipments policy",
    "topK": 5
  },
  "requiresApproval": false
}
```

### tool.result
```json
{
  "toolName": "docs.search",
  "rowCount": 0,
  "citationCount": 3,
  "summary": "Top 3 policy chunks returned"
}
```

### checkpoint.saved
```json
{
  "checkpointId": "GUID",
  "approvalId": "GUID"
}
```

### approval.required
```json
{
  "approvalId": "GUID",
  "toolName": "github.create_issue",
  "riskSummary": "Creates a GitHub issue in the configured repo"
}
```

### approval.status
```json
{
  "approvalId": "GUID",
  "status": "Approved"
}
```

### workflow.resumed
```json
{
  "checkpointId": "GUID"
}
```

### assistant.message
```json
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
```

### error
```json
{
  "code": "QUERY_VALIDATION_FAILED",
  "message": "Column is not allowlisted",
  "retryable": true
}
```

### done
```json
{
  "success": true
}
```

## MCP tool contracts

### db.get_schema_summary
Output:

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

### db.query_readonly
Input shape:

```json
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
    { "column": "Status", "op": "eq", "value": "Delayed" }
  ],
  "orderBy": [
    { "column": "ExpectedShipDateUtc", "dir": "desc" }
  ],
  "limit": 50
}
```

### docs.search
Input:

```json
{
  "query": "delayed shipments policy",
  "topK": 5
}
```

Output:

```json
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
```

### docs.get_chunk
Input:

```json
{
  "citationId": "doc-guid:12"
}
```

### github.create_issue
Input:

```json
{
  "repo": "org/repo",
  "title": "Delayed Shipments: 30-day review",
  "body": "...",
  "labels": ["nexus-demo"]
}
```

Output:

```json
{
  "issueUrl": "https://github.com/.../issues/123",
  "issueNumber": 123
}
```