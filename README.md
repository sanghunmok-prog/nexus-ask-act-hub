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

## Explicit non-goals before MVP
- OCR for scanned PDFs
- multi-tenant SSO
- reasoning graph / chain-of-thought UI
- multiple external connectors beyond GitHub
- approximate vector index tuning / ANN
- raw SQL execution from user input

## Main components
- Nexus.Web (Angular)
- Nexus.OrchestratorApi (.NET 10)
- Nexus.Mcp.Toolbelt (.NET 10)
- SQL Server 2025 local container / Azure SQL in cloud

## Local development model
- Local dev uses SQL Server 2025 in Docker
- .NET Aspire orchestrates local services
- Cloud demo later uses Azure Container Apps + Azure SQL

## Repo docs
- docs/NEXUS_MASTER.md
- docs/API_CONTRACTS.md
- docs/SECURITY.md
- docs/DATA_MODEL.md
- docs/DEMO_SCRIPT.md
- AGENTS.md

## Working rule
Implement one PR at a time and keep the repo runnable after every PR.

## Current phase
PR-00: repo skeleton + docs + CI stub