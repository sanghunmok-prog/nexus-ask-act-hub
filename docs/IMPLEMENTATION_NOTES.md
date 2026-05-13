# IMPLEMENTATION_NOTES.md

## Purpose

This file locks the current repository-level implementation conventions that reflect the merged implementation baseline through PR-04 and the query-safety design locked before PR-05 implementation.

Use this file together with the repo code as the canonical source of truth for current implementation behavior and repo conventions.

Historical uploaded transcripts, exported txt files, and earlier setup notes are useful references, but they are not the canonical implementation guide for future PR work.

## Canonical sources

When there is any implementation-detail mismatch, follow this order:

1. Current repo code
2. This file
3. Other repo docs
4. Historical transcripts / exported txt files

## Current merged implementation baseline (PR-01 to PR-04)

### PR-01 — executable .NET backbone

Merged outcomes:
- The canonical .NET solution file is `Nexus.slnx`
- The local distributed app host exists under `src/Nexus.AppHost`
- The AppHost entry file is `src/Nexus.AppHost/AppHost.cs`
- Shared service defaults exist under `src/Nexus.ServiceDefaults`
- `src/Nexus.OrchestratorApi` exists
- `src/Nexus.Mcp.Toolbelt` exists
- `src/Nexus.Contracts` exists
- `GET /api/health` exists in OrchestratorApi
- `GET /api/health` exists in Mcp.Toolbelt
- For local development, AppHost health checks currently target `/api/health` over the `http` endpoint

### PR-02 — SQL schema and seed scripts

Merged outcomes:
- SQL scripts live under `infra/docker/sql/`
- The current SQL script set is:
  - `001_schema.sql`
  - `002_seed_orders.sql`
  - `003_verify_orders.sql`
- Local SQL development uses the `nexus-sql` Docker container
- The SQL schema and Orders seed flow have been smoke-tested locally against the container

### PR-03 — mock SSE backend endpoint

Merged outcomes:
- `src/Nexus.OrchestratorApi/Program.cs` exposes `POST /api/chat/stream`
- Request body shape is:

  ```json
  {
    "prompt": "..."
  }
  ```

- The endpoint returns deterministic mock SSE envelopes
- The current minimal happy-path mock sequence is:
  - `workflow.started`
  - `tool.call`
  - `tool.result`
  - `assistant.message`
  - `done`

### PR-04 — Angular chat + trace UI

Merged outcomes:
- Angular app lives under `src/Nexus.Web`
- The Angular app is in the same repo, but it is not a .NET solution project
- The frontend consumes `POST /api/chat/stream`
- Because the backend contract is POST SSE, the frontend uses `fetch + ReadableStream`
- Do not treat `EventSource` as the primary client pattern for the current implementation
- Local frontend development uses a dev proxy to the local OrchestratorApi HTTP endpoint

## Locked repository conventions

These conventions are locked unless intentionally changed in a future PR.

### Solution and project layout

- Use `Nexus.slnx` as the canonical .NET solution file
- Keep Angular under `src/Nexus.Web`
- Do not try to add Angular as a .NET solution project
- AppHost entry file is `src/Nexus.AppHost/AppHost.cs`

### Local health checks

- OrchestratorApi and Mcp.Toolbelt expose `GET /api/health`
- Local AppHost health checks currently use `/api/health` over `http`
- If this convention changes later, update the repo docs in the same PR

### Current SSE convention

- `POST /api/chat/stream` is the current backend SSE endpoint
- The current frontend consumption pattern is `fetch + ReadableStream`
- Do not assume `EventSource` for the current POST-based SSE contract

### Documentation rule

If implementation conventions change, update:
- `docs/IMPLEMENTATION_NOTES.md`
- any impacted contract/security docs
- `AGENTS.md` when repo-level guidance changes

Do this in the same PR as the code change.

## Current local dev assumptions

### Backend

Useful current local commands:
- `dotnet build Nexus.slnx`
- `dotnet run --project src/Nexus.AppHost`
- `dotnet run --project src/Nexus.OrchestratorApi`

### Frontend

Useful current local commands:
- `cd src/Nexus.Web`
- `npm install`
- `npm start`
- `npm run build`

### SQL

Useful current local assumptions:
- SQL Server runs in Docker under the `nexus-sql` container
- SQL scripts are applied from `infra/docker/sql`
- Orders seed data has already been verified locally

## PR-05 query safety lock

The following decisions are locked before implementing PR-05.

### Allowlist source of truth

- `src/Nexus.OrchestratorApi/Security/allowlist.json`

### Code ownership

- StructuredQuery DTOs belong in `Nexus.Contracts`
- allowlist loading, validation, and compilation belong in `Nexus.OrchestratorApi`
- tests belong in `tests/Nexus.OrchestratorApi.Tests`

### Allowlist table metadata

Each allowlist table entry may include `columnTypes` metadata.

Initial `Orders` implementation uses `columnTypes` so validator rules can distinguish string columns from non-string columns.

Current `Orders` columnTypes mapping includes:
- `OrderId`: `int`
- `CreatedAtUtc`: `datetime`
- `Status`: `string`
- `ExpectedShipDateUtc`: `datetime`
- `ActualShipDateUtc`: `datetime`
- `Carrier`: `string`
- `DelayReason`: `string`

This metadata is used by PR-05 validation rules such as:
- `contains` is allowed only for string columns

### StructuredQuery shape

StructuredQuery uses:
- `table`
- `select`
- `filters`
- `orderBy`
- `limit`

### Filter shape

Each filter uses:
- `column`
- `op`
- `value`
- optional `value2`

Rule:
- `value2` is used only for `between`

### Operator support

Allowed operators:
- `eq`
- `neq`
- `gte`
- `lte`
- `between`
- `contains`

### Locked validation rules

- single-table only
- no JOIN support
- `select` must not be empty
- filters combine with `AND` only
- `orderBy.dir` must be `asc` or `desc`
- `limit` is required
- `limit <= 0` is invalid
- `limit > maxLimit` is clamped to `maxLimit`
- `contains` is allowed only on string columns

### Initial Orders-specific intent for string filtering

For the initial Orders allowlist, `contains` is intended only for string filterable columns such as:
- `Status`
- `Carrier`

It is not intended for datetime columns such as:
- `ExpectedShipDateUtc`

### Compiler output rules

PR-05 compiler output must be:
- pure
- deterministic
- parameterized

Use deterministic parameter names:
- `@p0`
- `@p1`
- ...
- `@p_limit`

Compiler output should be a plain result model such as:
- `SqlText`
- `Parameters`

Do not couple PR-05 compiler output to live DB execution.

### PR boundary

PR-05 validates and compiles only.

Actual DB execution is deferred to PR-07.

## Explicit out-of-scope through PR-05

Still out of scope through PR-05:
- raw SQL execution from user input
- multi-table queries
- JOIN support
- real DB execution in PR-05
- Semantic Kernel
- approvals UI
- audit viewer
- document upload
- future PR work beyond the validator/compiler/test boundary

## PR-06 Toolbelt schema summary convention

- `src/Nexus.OrchestratorApi/Security/allowlist.json` remains the allowlist source of truth.
- `src/Nexus.Mcp.Toolbelt` reads that existing allowlist through a project-file content link with copy-to-output / copy-to-publish.
- The PR-06 local HTTP shim for `db.get_schema_summary` is `GET /api/tools/db/schema-summary`.
- PR-06 projects allowlisted tables to table name plus allowlisted `select` columns only.
- PR-06 does not execute SQL and does not implement `db.query_readonly`.

## PR-14 approval UI and checkpoint scaffold convention

- Angular approval UI lives under `src/Nexus.Web/src/app/approvals/`.
- The approval panel is rendered inside the existing app left panel below the assistant answer; there is no new route or page.
- The frontend approval service uses `fetch` for:
  - `GET /api/approvals/pending`
  - `POST /api/approvals/{approvalId}/approve`
  - `POST /api/approvals/{approvalId}/reject`
- The chat stream still uses POST `fetch + ReadableStream`; do not convert it to `EventSource`.
- `approval.required` stream events refresh the pending approval panel and render as dedicated trace cards.
- AgentCheckpoint status values are:
  - `WaitingApproval`
  - `ReadyToResume`
  - `Failed`
- Approve marks a related `WaitingApproval` checkpoint `ReadyToResume`.
- Reject marks a related `WaitingApproval` checkpoint `Failed`.
- `ReadyToResume` is internal future execution readiness only.
- `ReadyToResume` does not expose execution to the user.
- PR-14 does not resume workflow execution.
- PR-14 does not execute GitHub or any external action.
- PR-14 does not add a public resume endpoint.
- `resumeAvailable` remains false.
- PR-16 will add GitHub create issue execution.

## PR-07 Toolbelt readonly query convention

- Query safety code now belongs to `src/Nexus.QuerySafety`.
- `src/Nexus.OrchestratorApi/Security/allowlist.json` remains the allowlist source of truth.
- `src/Nexus.Mcp.Toolbelt` owns runtime execution for `db.query_readonly`.
- The PR-07 local HTTP shim for `db.query_readonly` is `POST /api/tools/db/query-readonly`.
- `db.query_readonly` accepts `Nexus.Contracts.StructuredQuery`, validates it with `Nexus.QuerySafety`, compiles it to a parameterized `SELECT`, and executes only that compiler-generated SQL.

## PR-08 document ingestion convention

- `src/Nexus.OrchestratorApi` owns document upload and ingestion.
- The PR-08 upload endpoint is `POST /api/documents/upload`.
- Supported inputs are `.txt`, `.md`, and text-based `.pdf`; OCR remains out of scope.
- Uploaded document text is chunked deterministically with 1000-character chunks and 150-character overlap.
- PR-08 inserts `PolicyDocuments` and `PolicyChunks`.
- `PolicyChunks.Embedding` is stored as `NULL` in PR-08.

## PR-09 embedding convention

- `src/Nexus.Embeddings` is the shared embedding provider project.
- The shared provider project exposes `IEmbeddingProvider` and `MockEmbeddingProvider` under the `Nexus.Embeddings` namespace.
- PR-09 uses deterministic token-hashing mock embeddings with provider name `mock-token-hashing` and dimension `1536`.
- `src/Nexus.OrchestratorApi` owns `POST /api/documents/{docId}/ingest`.
- The ingest endpoint embeds existing `PolicyChunks` rows where `Embedding IS NULL` and persists embeddings to `PolicyChunks.Embedding`.
- Live OpenAI or Azure OpenAI embeddings remain out of scope.
- PR-09 does not implement `docs.search` or vector search.

## PR-10 document retrieval convention

- `src/Nexus.Mcp.Toolbelt` owns document retrieval tools.
- The PR-10 local HTTP shims are `POST /api/tools/docs/search` and `POST /api/tools/docs/get-chunk`.
- `docs.search` uses `Nexus.Embeddings` `mock-token-hashing` query embeddings and exact SQL Server `VECTOR_DISTANCE` against `PolicyChunks.Embedding`.
- `docs.search` returns citation-ready snippets and metadata, not full chunk text or vectors.
- `docs.get_chunk` returns full chunk text for an explicit selected `chunkId` or citation id.
- Angular citation UI and Orchestrator chat integration remain future work.

## PR-11 Orchestrator agent runtime convention

- `src/Nexus.OrchestratorApi` owns the chat agent runtime for `POST /api/chat/stream`.
- The POST-based SSE contract remains unchanged, and the frontend continues to consume the stream with `fetch + ReadableStream`.
- `LLM_MODE=mock` is the default when no mode is configured.
- Mock mode uses a deterministic planner for the delayed shipments / delayed orders policy demo prompt family.
- Live mode is a configured seam only in PR-11; it returns a sanitized not-configured error and does not require API keys or live LLM packages.
- `NEXUS_TOOLBELT_BASE_URL` controls Orchestrator-to-Toolbelt HTTP calls. `Toolbelt:BaseUrl` is the secondary configuration key. Development falls back to `http://localhost:5062`.
- The Toolbelt local HTTP shims remain the integration path for PR-11:
  - `POST /api/tools/docs/search`
  - `GET /api/tools/db/schema-summary`
  - `POST /api/tools/db/query-readonly`
- PR-11 emits tool orchestration trace events and an `assistant.message` execution summary only.
- Final hybrid SQL + policy answer composition remains PR-12.

## PR-12 hybrid response composition convention

- `src/Nexus.OrchestratorApi` owns deterministic read-path response composition.
- `HybridResponseComposer` consumes collected PR-11 Toolbelt results from `docs.search`, optional `docs.get_chunk`, `db.get_schema_summary`, and `db.query_readonly`.
- After `docs.search` returns a top result, the runtime dynamically calls `POST /api/tools/docs/get-chunk` with `chunkId` when present, otherwise `citationId`.
- The composer uses `docs.get_chunk` `chunkText` for policy text. If chunk loading fails and `docs.search` has a snippet, the composer uses that snippet with a note that full citation text was unavailable.
- PR-12 does not add live LLM answer generation, Semantic Kernel packages, Angular changes, approvals, checkpoints, or action tools.
- PR-13+ handles approval and action-path implementation.

## PR-13 approval/checkpoint foundation convention

- `src/Nexus.OrchestratorApi` owns approval/checkpoint backend APIs.
- Action prompts containing `create` plus `issue` or `ticket` create a pending `ApprovalRequest` and `AgentCheckpoint` for `github.create_issue`.
- Approval and checkpoint inserts use the existing `dbo.ApprovalRequest` and `dbo.AgentCheckpoint` tables in a single transaction.
- `X-Nexus-UserId` is used as the demo user id when present; otherwise `demo-user` is used.
- `GET /api/approvals/pending`, `POST /api/approvals/{approvalId}/approve`, and `POST /api/approvals/{approvalId}/reject` are implemented in OrchestratorApi.
- PR-13 approve/reject updated approval status only. PR-14 adds approval UI and related checkpoint status updates, but still does not resume workflows, execute GitHub issue creation, or add audit endpoints.
