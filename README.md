# NEXUS

NEXUS is an internal enterprise knowledge and action hub for legacy enterprise data silos.

## One-liner

Users can:
- ask questions across SQL data and policy/runbook documents
- request safe, approval-gated actions
- see execution trace and audit history for every step

## MVP outcomes

- Hybrid retrieval: SQL + document search
- Approval-gated GitHub issue creation
- Audit log + execution trace
- Bounded self-correction / retry
- Mock-first local development

## Current repository truth

Current implementation conventions and merged-state notes live in `docs/IMPLEMENTATION_NOTES.md`.

Repository docs plus repo code are the canonical source of truth for current behavior and current repo conventions.

## Explicit non-goals before MVP

- OCR for scanned PDFs
- multi-tenant SSO
- reasoning graph / chain-of-thought UI
- multiple external connectors beyond GitHub
- approximate vector index tuning / ANN
- raw SQL execution from user input

## Main components

- Nexus.Web (Angular, same repo, not a .NET solution project)
- Nexus.AppHost (.NET Aspire AppHost)
- Nexus.ServiceDefaults (.NET Aspire shared service defaults)
- Nexus.OrchestratorApi (.NET 10)
- Nexus.Mcp.Toolbelt (.NET 10)
- Nexus.Contracts (.NET shared contracts)
- SQL Server 2025 local container / Azure SQL in cloud

## Local development model

- .NET solution file: `Nexus.slnx`
- Local orchestration uses `Nexus.AppHost`
- Local dev DB uses the `nexus-sql` Docker container
- Angular app lives under `src/Nexus.Web`
- Mock SSE endpoint exists at `POST /api/chat/stream`

## Repo docs

- `docs/NEXUS_MASTER.md`
- `docs/IMPLEMENTATION_NOTES.md`
- `docs/API_CONTRACTS.md`
- `docs/SECURITY.md`
- `docs/DATA_MODEL.md`
- `docs/DEMO_SCRIPT.md`
- `AGENTS.md`

## Working rule

Implement one PR at a time and keep the repo runnable after every PR.

If implementation conventions change, update the repo docs in the same PR.