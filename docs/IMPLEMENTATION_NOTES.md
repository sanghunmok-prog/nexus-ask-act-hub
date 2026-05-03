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