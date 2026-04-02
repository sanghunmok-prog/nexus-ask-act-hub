# AGENTS.md

You are working in the NEXUS repository.

## Read first
Before changing code, read:
- README.md
- docs/NEXUS_MASTER.md
- docs/API_CONTRACTS.md
- docs/SECURITY.md
- docs/DATA_MODEL.md

## Core behavior
- Work on exactly one PR at a time.
- Do not implement future PRs early.
- Keep the implementation minimal, buildable, and testable.
- Prefer straightforward code over framework-heavy abstraction.
- At the end of a task, report:
  1. files changed
  2. exact build/run commands
  3. anything still manual or incomplete

## Scope locks
Do NOT add any of the following unless explicitly requested:
- OCR
- multi-tenant SSO
- reasoning graph UI
- raw SQL execution from user input
- multiple external connectors beyond GitHub
- unbounded retries
- broad autonomous agent behavior

## Product rules
- Local development must support LLM_MODE=mock.
- Orchestrator owns approval decisions.
- Toolbelt does not decide policy.
- Read-only DB queries must use StructuredQuery + allowlist.
- Single-table queries only in MVP.
- Always parameterize SQL values.
- Do not display chain-of-thought in UI.
- Trace may show tool names, sanitized args, durations, rowCount, citationCount.

## PR order
- PR-00: Repo skeleton + docs + CI stub
- PR-01: Aspire AppHost + ServiceDefaults + health endpoints
- PR-02: SQL schema + seed scripts + EF migrations (if used)
- PR-03: Orchestrator SSE endpoint (mock events)
- PR-04: Angular Chat + Trace UI consumes SSE
- PR-05: Allowlist + StructuredQuery validator/compiler
- PR-06: MCP Toolbelt skeleton + db.get_schema_summary
- PR-07: db.query_readonly implemented with compiler + SQL execution
- PR-08: Document upload + ingestion (text extraction + chunker)
- PR-09: Embedding provider abstraction + mock embeddings
- PR-10: docs.search (exact vector search) + citations panel
- PR-11: Semantic Kernel wiring (mock/live)
- PR-12: Hybrid response composer
- PR-13: ApprovalRequest + AgentCheckpoint tables + API endpoints
- PR-14: Approval UI + resume workflow from checkpoint
- PR-15: Self-correction loop + retry budget tests
- PR-16: GitHub create issue tool + approval gating
- PR-17: Azure deploy (azd) + final README + demo script

## Additional constraints
- Do not add SQL schema before PR-02.
- Do not add SSE chat before PR-03.
- Do not add Angular chat UI before PR-04.
- Do not add Semantic Kernel before PR-11.
- Do not add approval workflow before PR-13.
- Do not add GitHub execution before PR-16.