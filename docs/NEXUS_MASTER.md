# NEXUS Master Notes

## Current Repository Implementation Note

This file is the product and architecture blueprint.

For current merged-state implementation conventions and current repo-level execution rules, also read `docs/IMPLEMENTATION_NOTES.md`.

If this blueprint and the current repo code differ on implementation-level details such as:
- solution file naming
- Angular repo placement
- AppHost entry file naming
- current local dev conventions
- current POST-based SSE frontend consumption

follow `docs/IMPLEMENTATION_NOTES.md` and the repo code.


## Goal
Build a portfolio-safe internal platform where users can:
1. query SQL data and policy documents together
2. see transparent execution trace and citations
3. request a GitHub issue creation action
4. require approval before that action executes
5. keep audit history and checkpoint/resume flow

## MVP included
- Hybrid retrieval: SQL + document search
- Approval-gated GitHub issue creation
- Audit log + execution trace
- Bounded self-correction / retry
- Mock-first local development

## Explicitly out of scope before MVP
- OCR for scanned PDFs
- multi-tenant SSO
- approximate vector index tuning / ANN
- multiple connectors beyond GitHub
- chain-of-thought visualization
- raw SQL execution

## Architecture
- Nexus.Web (Angular): chat, trace, approvals, audit viewer, document upload
- Nexus.OrchestratorApi (.NET 10): workflow, SSE, approvals, checkpoint/resume
- Nexus.Mcp.Toolbelt (.NET 10): standardized tools for SQL, documents, GitHub
- SQL Server local container for dev; Azure SQL later for cloud demo

## Pipeline A — Ask
1. Web sends POST /api/chat/stream
2. Orchestrator emits workflow.started
3. Orchestrator calls docs.search(query, topK)
4. Toolbelt returns document chunks + citations
5. Orchestrator calls db.get_schema_summary()
6. LLM or mock planner produces StructuredQuery
7. Orchestrator calls db.query_readonly(StructuredQuery)
8. Toolbelt executes parameterized SQL using allowlist
9. Orchestrator emits assistant.message with merged answer + citations
10. Orchestrator emits done

## Pipeline B — Document ingestion
1. Upload text-based PDF / TXT / MD
2. Save PolicyDocuments row
3. Extract text
4. Chunk text
5. Save PolicyChunks with `Embedding = NULL`
6. Return chunked pending embedding state

Embedding generation is deferred to PR-09.

## Pipeline C — Act
1. User requests GitHub issue creation
2. Orchestrator identifies approval-required action
3. Insert ApprovalRequest
4. Insert AgentCheckpoint
5. Emit approval.required
6. Pause workflow
7. After approve, load checkpoint and resume
8. Execute github.create_issue
9. Write AuditLog events
10. Emit assistant.message and done

## Pipeline D — Self-correction
1. db.query_readonly fails
2. get schema summary
3. regenerate StructuredQuery
4. retry
5. stop after max 2 retries

## Repo layout
- README.md
- docs/
  - NEXUS_MASTER.md
  - IMPLEMENTATION_NOTES.md
  - API_CONTRACTS.md
  - SECURITY.md
  - DEMO_SCRIPT.md
  - ADR/
- infra/
  - azd/
  - docker/
- src/
  - Nexus.AppHost/
  - Nexus.ServiceDefaults/
  - Nexus.OrchestratorApi/
  - Nexus.Mcp.Toolbelt/
  - Nexus.Contracts/
  - Nexus.Web/

## Working rules
- One PR at a time
- Keep the repo runnable after each PR
- Add minimal tests where practical
- Update docs when contracts change
- Prefer mock-first before live integrations

## PR backlog
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

## Definition of done
- Local run works in under 15 minutes on a clean machine
- Hybrid query shows SQL + document citations
- Approval gating blocks any action before approval
- Checkpoint/resume appears in trace
- Audit log shows lifecycle
- StructuredQuery compiler blocks non-allowlisted access
- Tests exist for compiler + approvals + retry budget
- README explains value quickly
- Demo script is recorded and runnable
