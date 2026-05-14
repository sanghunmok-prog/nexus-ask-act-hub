# Security and Safety Rules

For current merged-state implementation conventions, also read `docs/IMPLEMENTATION_NOTES.md`.

## Core principle

No state-changing action executes without approval.

NEXUS is a governed demo workflow, not a broad autonomous agent. Read tools and write tools have different safety rules, and write actions require both approval and explicit execution.

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

Final SQL read boundary:
- callers never submit raw SQL
- the Orchestrator produces StructuredQuery requests
- Toolbelt validates against the allowlist
- compiler output is parameterized
- MVP reads remain single-table only

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

## PR-11 Orchestrator runtime safety

- `POST /api/chat/stream` must not stream chain-of-thought, hidden prompts, secrets, connection strings, or internal exception details.
- Mock planner behavior is deterministic and bounded.
- In PR-11, Orchestrator invokes only known read tool contracts:
  - `docs.search`
  - `db.get_schema_summary`
  - `db.query_readonly`
- No write/action tools are enabled in PR-11.
- Live mode is optional, not required for tests, and does not bypass StructuredQuery allowlists or Toolbelt validation.
- Unsupported `LLM_MODE` values and unconfigured live mode return sanitized SSE `error` events.

## PR-12 hybrid response safety

- `assistant.message` must not include chain-of-thought, hidden prompts, embeddings, connection strings, raw JSON blobs, or internal exception details.
- Policy text in the final answer must be extractive from `docs.get_chunk` `chunkText` or, when chunk loading is unavailable, the `docs.search` snippet fallback.
- The Orchestrator must not invent policy language or unsupported recommendations.
- SQL rows in the final answer must come from the `db.query_readonly` result.
- No write/action tools are enabled in PR-12.

## PR-13 approval/checkpoint safety

- External actions require explicit approval before execution.
- PR-13 persists approval requests and checkpoints but does not execute GitHub or other external actions.
- `ParamsHash` is SHA-256 over deterministic action parameter JSON and binds the approval record to the pending action parameters.
- Pending action params must not include secrets, connection strings, hidden prompts, chain-of-thought, or internal exception details.
- There is no auth/RBAC in PR-13. `X-Nexus-UserId` is a demo identity placeholder, with `demo-user` as the fallback.
- Approval transitions are one-way from `Pending` to `Approved` or `Rejected`; approve/reject on non-pending approvals returns a sanitized conflict.

## PR-14 approval UI and ReadyToResume safety

- PR-14 adds pending approval UI and approve/reject UX.
- AgentCheckpoint status values are `WaitingApproval`, `ReadyToResume`, and `Failed`.
- Approve marks a related `WaitingApproval` checkpoint `ReadyToResume`.
- Reject marks a related `WaitingApproval` checkpoint `Failed`.
- `ReadyToResume` is internal future execution readiness only.
- `ReadyToResume` does not expose execution to the user.
- PR-14 does not resume workflow execution.
- PR-14 does not execute GitHub or any external action.
- PR-14 does not add a public resume endpoint.
- `resumeAvailable` remains false.
- PR-16 will add GitHub create issue execution.
- Approval UI errors must remain sanitized and must not display stack traces, secrets, connection strings, or raw internal exceptions.

## PR-15 bounded correction safety

- PR-15 adds bounded read-path correction for recoverable `db.query_readonly` schema/allowlist errors only.
- The default retry budget is 1 correction retry.
- At most 2 total `db.query_readonly` attempts are allowed.
- Correction is deterministic in mock mode.
- Correction produces a `StructuredQuery`; it does not generate raw SQL and does not bypass QuerySafety.
- The corrected query still goes through the Toolbelt `db.query_readonly` path.
- `tool.retry` is a sanitized operational trace event.
- No chain-of-thought, raw SQL, stack traces, secrets, connection strings, or internal prompts may be exposed in retry traces or errors.
- Correction does not apply to external actions.
- PR-15 does not execute GitHub.
- PR-15 does not resume `ReadyToResume` checkpoints.
- GitHub issue execution remains PR-16.

## PR-16 GitHub issue execution safety

- PR-16 adds approval-gated GitHub issue execution for `github.create_issue` only.
- Approve does not execute external actions; an explicit execute action is required.
- Only `Approved` approvals with a related `ReadyToResume` checkpoint can execute.
- AgentCheckpoint status values are `WaitingApproval`, `ReadyToResume`, `Executing`, `Completed`, and `Failed`.
- Duplicate issue creation is prevented by an atomic `ReadyToResume` -> `Executing` checkpoint claim.
- GitHub write actions have no automatic retry in Orchestrator or Toolbelt HTTP clients. PR-15 read-path retry does not apply to write actions.
- There is no generic public resume endpoint.
- `NEXUS_GITHUB_TOKEN` belongs only in the Toolbelt environment.
- `NEXUS_GITHUB_ALLOWED_REPOS` is mandatory; disallowed repos are rejected before any GitHub call.
- GitHub auth/config/validation failures return sanitized 4xx Toolbelt responses where applicable, such as `GITHUB_AUTH_FAILED` for a GitHub 401.
- Tokens, secrets, raw GitHub responses, stack traces, connection strings, and internal exception details must not be committed, logged into public responses, or displayed in the UI.

Final write-action boundary:
- approval is not execution
- execute is required after approval
- duplicate execute is prevented by the checkpoint claim
- write actions have no retry
- GitHub execution is limited to allowed repositories
- GitHub token configuration is isolated to Toolbelt

## PR-17 final packaging safety

- PR-17 adds portfolio/demo documentation and low-risk UI polish only.
- It does not add product features, backend behavior changes, API behavior changes, schema changes, Azure IaC, deployment automation, dependencies, or secrets.
- Azure deployment guidance is documented as readiness guidance only. The project does not claim to be deployed to Azure.

## Approval gating rules

For any tool with `requiresApproval=true`:

1. create `ApprovalRequest`
2. create `AgentCheckpoint`
3. emit `approval.required`
4. stop workflow
5. approve prepares the checkpoint as `ReadyToResume` but does not execute
6. in PR-16, explicit execute can run only approved `github.create_issue` actions

## Ownership rule

The Orchestrator owns approval policy decisions.

The Toolbelt only executes tools and returns results.

## Trace rules

- Show tool names
- Show sanitized args only
- Show durations
- Show rowCount and citationCount
- Never show chain-of-thought
- Never show raw SQL, stack traces, connection strings, tokens, local passwords, or secrets in SSE/API responses
- Public errors should be sanitized and stable enough for the UI to display safely

## Scope locks

Do not add before MVP:
- OCR
- multi-tenant SSO
- reasoning graph UI
- raw SQL execution
- 5+ connectors
- ANN tuning

## Final security summary

- No raw SQL input.
- StructuredQuery plus allowlist for SQL reads.
- Parameterized SQL path.
- Approval required before external writes.
- Approve is not execute.
- Execute required after approval.
- Duplicate execute prevention.
- No write-action retry.
- GitHub token isolated to Toolbelt.
- Sanitized errors.
- No chain-of-thought exposure.
- No secrets in SSE/API responses.
