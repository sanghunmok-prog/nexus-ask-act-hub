# Security and Safety Rules

For current merged-state implementation conventions, also read `docs/IMPLEMENTATION_NOTES.md`.

## Core principle

No state-changing action executes without approval.

## Secret handling

- Never commit real secrets to the repository.
- Local development should use user-secrets or a gitignored `.env` file.
- `.env.example` may contain placeholders only.

## Mock/live mode

- Default local mode: `LLM_MODE=mock`
- Mock mode must be deterministic.
- Live mode is optional and comes later.

## Allowlist source of truth

Repository path:

- `src/Nexus.OrchestratorApi/Security/allowlist.json`

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
      ],
      "columnTypes": {
        "OrderId": "int",
        "CreatedAtUtc": "datetime",
        "Status": "string",
        "ExpectedShipDateUtc": "datetime",
        "ActualShipDateUtc": "datetime",
        "Carrier": "string",
        "DelayReason": "string"
      }
    }
  },
  "maxLimit": 200,
  "singleTableOnly": true
}
```

`columnTypes` metadata is used by query validation rules that depend on column type, such as restricting `contains` to string columns.

## StructuredQuery contract (locked for PR-05+)

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
```

## Allowed operators

- `eq`
- `neq`
- `gte`
- `lte`
- `between`
- `contains`

## Locked validation rules for PR-05

- table name must be allowlisted
- `select` must not be empty
- each `select` column must be allowlisted
- each filter column must be allowlisted
- each `orderBy` column must be allowlisted
- filters combine with `AND` only
- `orderBy.dir` must be `asc` or `desc`
- `limit` is required
- `limit <= 0` is invalid
- `limit > maxLimit` is clamped to `maxLimit`
- single-table only
- no JOIN support

### between

- `between` requires both `value` and `value2`
- `value2` is used only for `between`

### contains

- `contains` is allowed only for string columns
- For the initial Orders allowlist, `contains` is intended only for string filter columns such as:
  - `Status`
  - `Carrier`
- Do not allow `contains` on datetime columns such as:
  - `ExpectedShipDateUtc`

## SQL compilation rules

- Validate table name is allowlisted
- Validate every column in `select`, `filters`, and `orderBy`
- Compile against `dbo.<TableName>`
- Force `TOP (@p_limit)` and cap to `maxLimit`
- Always parameterize filter values
- Use deterministic parameter names:
  - `@p0`
  - `@p1`
  - ...
  - `@p_limit`
- `between` must compile to two parameters
- `contains` must compile to `LIKE` with a parameterized `%...%` pattern
- Single-table queries only
- No JOIN in MVP

## Compiler output rule for PR-05

PR-05 is validate-and-compile only.

In PR-05:
- return compiled SQL text plus parameter values in a plain result model
- do not execute SQL yet

Actual DB execution is deferred to PR-07.

## PR-07 SQL read execution

PR-07 enables SQL Server read execution only through `StructuredQuery` validation and compiler-generated parameterized `SELECT` statements.

Raw SQL input remains prohibited. Toolbelt must not accept caller-provided SQL text, joins, writes, stored procedures, or non-compiler-generated commands for `db.query_readonly`.

## PR-08 document ingestion safety

- Only `.txt`, `.md`, and text-based `.pdf` uploads are supported.
- OCR and scanned PDF extraction are out of scope.
- Scanned PDFs or files with no extractable text return sanitized upload errors.
- Public upload errors must not include full extracted document text, connection strings, generated SQL, or internal exception details.
- PR-08 does not create embeddings and does not implement document search.

## PR-09 embedding safety

- PR-09 uses deterministic mock embeddings and makes no external model or API calls.
- No embedding API keys or secrets are required.
- Public embedding responses do not expose embedding vectors or chunk text.
- SQL connection and embedding failures return sanitized JSON errors without internal exception details.
- PR-09 does not implement document search or vector search.

## PR-10 document retrieval safety

- `docs.search` and `docs.get_chunk` do not accept raw SQL input or SQL fragments.
- `docs.search` returns short snippets and citation metadata, not full chunk text by default.
- `docs.get_chunk` returns full chunk text only for explicit chunk lookup by `chunkId` or citation id.
- SQL and vector search failures return sanitized JSON errors without internal exception details.
- If SQL Server vector syntax is unavailable, do not use fake vector-search fallbacks such as keyword search, random distances, or in-memory scans.

## Approval gating rules

For any tool with `requiresApproval=true`:

1. create `ApprovalRequest`
2. create `AgentCheckpoint`
3. emit `approval.required`
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
