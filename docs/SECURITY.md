# Security and Safety Rules

## Core principle
No state-changing action executes without approval.

## Secret handling
- Never commit real secrets to the repository.
- Local development should use user-secrets or a gitignored .env file.
- .env.example may contain placeholders only.

## Mock/live mode
- Default local mode: LLM_MODE=mock
- Mock mode must be deterministic.
- Live mode is optional and comes later.

## Allowlist source of truth
Repository path:
- src/Nexus.OrchestratorApi/Security/allowlist.json

Recommended initial content:

```json
{
  "tables": {
    "Orders": {
      "select": [
        "OrderId",
        "CreatedAtUtc",
        "Status",
        "ExpectedShipDateUtc",
        "ActualShipDateUtc",
        "Carrier",
        "DelayReason"
      ],
      "filter": [
        "Status",
        "ExpectedShipDateUtc",
        "Carrier"
      ],
      "orderBy": [
        "ExpectedShipDateUtc",
        "CreatedAtUtc"
      ]
    }
  },
  "maxLimit": 200,
  "singleTableOnly": true
}
```

## StructuredQuery MVP schema

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
    { "column": "Status", "op": "eq", "value": "Delayed" },
    { "column": "ExpectedShipDateUtc", "op": "gte", "value": "2026-01-19T00:00:00Z" }
  ],
  "orderBy": [
    { "column": "ExpectedShipDateUtc", "dir": "desc" }
  ],
  "limit": 50
}
```

## Allowed operators
- eq
- neq
- gte
- lte
- between
- contains

## SQL compilation rules
- Validate table name is allowlisted.
- Validate every column in select, filters, and orderBy.
- Force TOP (@limit) and cap to maxLimit.
- Always parameterize filter values.
- Single-table queries only.
- No JOIN in MVP.
- Enforce timeout and cancellation token.

## Approval gating rules
For any tool with requiresApproval=true:
1. create ApprovalRequest
2. create AgentCheckpoint
3. emit approval.required
4. stop workflow
5. only resume after approve/reject endpoint decision

## Ownership rule
The Orchestrator owns approval policy decisions.  
The Toolbelt only executes tools and returns results.

## Trace rules
- Show tool names
- Show sanitized args only
- Show durations
- Show rowCount and citationCount
- Never show chain-of-thought

## Scope locks
Do not add before MVP:
- OCR
- multi-tenant SSO
- reasoning graph UI
- raw SQL execution
- 5+ connectors
- ANN tuning