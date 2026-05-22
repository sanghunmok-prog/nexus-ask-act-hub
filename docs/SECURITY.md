# Security And Safety Rules

NEXUS is a governed workflow demo. It is not a broad autonomous agent.

The core rule is simple:

```text
No external write runs without approval and explicit execute.
```

## Security Principles

- No raw SQL from user prompts.
- SQL reads go through `StructuredQuery`, allowlists, validation, and parameterized compiler output.
- Toolbelt executes constrained tools only.
- Orchestrator owns workflow and approval policy.
- GitHub token belongs only to Toolbelt.
- Approval is not execution.
- Execute is explicit and separate.
- Duplicate execute is blocked.
- Write actions are not retried automatically.
- Public traces and errors are sanitized.

## SQL Read Boundary

Final SQL read boundary:

1. User submits a natural-language prompt.
2. Orchestrator creates a `StructuredQuery`.
3. Toolbelt validates the query against the allowlist.
4. QuerySafety compiles a parameterized SQL `SELECT`.
5. Toolbelt executes only compiler-generated SQL.
6. UI receives sanitized result metadata and assistant output.

Prohibited:

- raw SQL input
- joins in the MVP read path
- multi-table user-directed queries
- stored procedure execution from user prompts
- writes through `db.query_readonly`
- non-allowlisted table or column access

## Allowlist Rules

The allowlist defines:

- selectable columns
- filterable columns
- sortable columns
- column types
- maximum limit
- single-table boundary

`contains` is allowed only on string columns.

`between` requires both `value` and `value2`.

## Document Retrieval Safety

Supported upload inputs:

- `.txt`
- `.md`
- text-based `.pdf`

Out of scope:

- OCR
- scanned PDF extraction
- fake vector search fallback
- returning embedding vectors to the UI

Rules:

- `docs.search` returns citation metadata and snippets.
- `docs.get_chunk` returns full chunk text only for explicit chunk lookup.
- document errors must not expose extracted full text, SQL, connection strings, stack traces, or internal exceptions.

## Trace Safety

Trace events may show:

- event type
- tool name
- sanitized arguments
- duration
- row count
- citation count
- approval id
- checkpoint status
- public result summary

Trace events must not show:

- chain-of-thought
- hidden prompts
- raw SQL
- stack traces
- connection strings
- local SQL passwords
- GitHub tokens
- raw GitHub response bodies
- internal exception details

## Bounded Correction Safety

The read-path correction retry applies only to recoverable `db.query_readonly` schema or allowlist validation failures.

Rules:

- default correction retry budget is `1`
- at most two `db.query_readonly` attempts are allowed
- attempt three must never occur
- correction produces another `StructuredQuery`
- correction does not generate raw SQL
- correction does not bypass QuerySafety
- correction does not apply to external actions
- correction does not apply to GitHub writes

## Approval Gating Rules

For any tool call with `requiresApproval=true`:

1. create `ApprovalRequest`
2. create `AgentCheckpoint`
3. emit `approval.required`
4. stop workflow
5. approve records the human decision
6. approve marks the checkpoint `ReadyToResume`
7. approve does not call Toolbelt
8. execute atomically claims `ReadyToResume -> Executing`
9. execute calls Toolbelt only after the claim succeeds
10. success marks checkpoint `Completed`
11. failure marks checkpoint `Failed`

## GitHub Issue Safety

`github.create_issue` is the only implemented write action.

Rules:

- Orchestrator may prepare a GitHub issue request.
- Orchestrator must not execute it before approval.
- Toolbelt owns `NEXUS_GITHUB_TOKEN`.
- Toolbelt requires `NEXUS_GITHUB_ALLOWED_REPOS`.
- Disallowed repos are rejected before any GitHub call.
- GitHub writes are not automatically retried.
- Duplicate execute returns `409`.
- GitHub errors returned to UI are sanitized.

## Secret Handling

Never commit:

- GitHub tokens
- SQL passwords
- connection strings with real credentials
- `.env` files with real values
- local launch settings containing secrets

Recommended storage:

- user-secrets
- shell profile exports
- gitignored `.env`
- managed secret store in cloud environments

## Process Boundary

Orchestrator may receive:

- `NEXUS_SQL_CONNECTION_STRING`
- `NEXUS_TOOLBELT_BASE_URL`
- `LLM_MODE`
- `NEXUS_DEMO_GITHUB_REPO`

Toolbelt may receive:

- `NEXUS_SQL_CONNECTION_STRING`
- `NEXUS_GITHUB_TOKEN`
- `NEXUS_GITHUB_ALLOWED_REPOS`

Frontend must not receive secrets.

## Public Error Policy

Public errors should be:

- stable
- short
- user-displayable
- free of secrets
- free of raw internal exception text

Example:

```json
{
  "code": "GITHUB_AUTH_FAILED",
  "message": "GitHub issue execution failed. No sensitive details were exposed.",
  "errors": []
}
```

## Explicitly Out Of Scope

- production SSO
- production RBAC
- multi-tenant authorization
- OCR
- raw SQL execution
- reasoning graph UI
- multiple external write connectors
- automatic write retries
- production deployment hardening
