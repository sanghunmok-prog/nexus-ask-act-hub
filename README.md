# NEXUS

NEXUS is an Enterprise Knowledge & Action Hub that combines SQL data, policy document retrieval, and approval-gated external actions into one auditable agent workflow.

It is a portfolio project for demonstrating how an AI-assisted internal tool can answer business questions, cite policy context, recover from bounded read-path errors, and require explicit human approval before writing to an external system.

## Ask + Act + Govern

NEXUS is built around a simple operating story:

- **Ask**: ask a question that needs both operational data and policy context.
- **Act**: request a controlled external action, currently GitHub issue creation.
- **Govern**: see the trace, approve or reject the pending action, then explicitly execute only after approval.

The project is intentionally not a broad autonomous agent. The Orchestrator owns policy decisions, the Toolbelt executes narrow tools, and write actions are gated by persisted approval and checkpoint records.

## Architecture Overview

- **Nexus.Web**: Angular chat, trace, pending approval, and ready-to-execute UI.
- **Nexus.OrchestratorApi**: .NET API for chat streaming, workflow orchestration, approvals, checkpoints, document upload, and ingestion.
- **Nexus.Mcp.Toolbelt**: .NET API exposing narrow tools for SQL reads, document retrieval, and GitHub issue creation.
- **Nexus.Contracts**: shared StructuredQuery and tool contract types.
- **SQL Server**: local development data store for orders, policy documents, chunks, audit records, approvals, and checkpoints.

For diagrams, see [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Key Capabilities

- Hybrid answer path using `docs.search`, `docs.get_chunk`, `db.get_schema_summary`, and `db.query_readonly`.
- Read-only SQL access through StructuredQuery, allowlisted tables/columns, and parameterized SQL.
- Deterministic mock-first local development with `LLM_MODE=mock`.
- Citation-ready policy document retrieval over embedded chunks.
- Bounded `db.query_readonly` correction retry for recoverable schema/allowlist failures.
- Approval request and checkpoint persistence for external actions.
- Approval UI with pending, rejected, and ready-to-execute states.
- Approval-gated `github.create_issue` execution.
- Duplicate execute prevention through an atomic checkpoint claim.
- Sanitized SSE trace that avoids chain-of-thought, raw SQL, stack traces, connection strings, and secrets.

## Tech Stack

- .NET 10 minimal APIs
- .NET Aspire AppHost and ServiceDefaults
- Angular
- SQL Server 2025 local container
- SQL Server vector type for exact document chunk search
- Deterministic mock embeddings
- GitHub REST API for issue creation

## Final Demo Walkthrough

Use [docs/DEMO_SCRIPT.md](docs/DEMO_SCRIPT.md) for the polished step-by-step demo.

The recommended flow:

1. Ask: `Which delayed orders are most at risk, and what policy applies?`
2. Recover: `Which delayed orders need correction retry?`
3. Act + Govern: `Create a GitHub issue for the delayed shipment findings.`

The third demo creates a real GitHub issue in the configured demo repository after approval and explicit execute. Use a test/demo repo.

## Local Quick Start

Prerequisites:

- .NET 10 SDK
- Node.js and npm
- Docker
- SQL Server local container configured with the repo SQL scripts

Build and test:

```bash
dotnet build Nexus.slnx
dotnet test Nexus.slnx
cd src/Nexus.Web
npm install
npm run build
npm test -- --watch=false
```

Run locally:

```bash
dotnet run --project src/Nexus.AppHost
```

In another terminal:

```bash
cd src/Nexus.Web
npm start
```

The Angular app uses the repo dev proxy and consumes the POST-based SSE endpoint with `fetch + ReadableStream`.

## Environment Variables

Detailed setup guidance is in [docs/ENVIRONMENT.md](docs/ENVIRONMENT.md).

Common variables:

- `NEXUS_SQL_CONNECTION_STRING`
- `NEXUS_TOOLBELT_BASE_URL`
- `LLM_MODE`
- `NEXUS_DEMO_GITHUB_REPO`
- `NEXUS_GITHUB_TOKEN`
- `NEXUS_GITHUB_ALLOWED_REPOS`

Important boundary: `NEXUS_GITHUB_TOKEN` belongs only to the Toolbelt process. The Orchestrator should not receive a GitHub token.

## Testing Summary

The final smoke checklist is in [docs/FINAL_SMOKE_CHECKLIST.md](docs/FINAL_SMOKE_CHECKLIST.md).

Primary checks:

- `dotnet build Nexus.slnx`
- `dotnet test Nexus.slnx`
- Orchestrator API tests
- Toolbelt tests
- Angular production build
- Angular unit tests
- live health checks
- read path smoke
- correction retry smoke
- approval and execute smoke
- duplicate execute `409` smoke
- secret grep checks

## Security Boundaries

- No raw SQL input is accepted.
- SQL reads use StructuredQuery, allowlists, and parameterized compiler output.
- MVP SQL reads are single-table only.
- Approval is required before external writes.
- Approve does not execute. Execute is a separate explicit action.
- GitHub issue execution is limited to configured allowed repositories.
- Duplicate execute is blocked.
- GitHub write actions are not retried automatically.
- GitHub token configuration is isolated to Toolbelt.
- SSE and API responses must not expose secrets, chain-of-thought, raw SQL, stack traces, or connection strings.

See [docs/SECURITY.md](docs/SECURITY.md) for the complete rule set.

## Known Limitations

- This is a portfolio/demo project and is not intended for production use as-is.
- Local development is mock-first; live LLM integration is intentionally not required.
- Only GitHub issue creation is implemented as an external write action.
- There is no multi-tenant SSO or production RBAC.
- Document ingestion supports text-based files only; OCR is out of scope.
- SQL read support is intentionally narrow and single-table for the MVP.
- Azure deployment guidance is documented, but Azure infrastructure is not included in this repo.

## Future Work / Next Portfolio Projects

- Production identity and role-based approval policies.
- Richer audit review UI and export.
- Additional governed action tools with the same approval model.
- Cloud deployment implementation as a separate portfolio project.
- More complete document ingestion operations and admin tooling.
- Live model evaluation harness and prompt/version governance.

## Repo Docs

- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)
- [docs/DEMO_SCRIPT.md](docs/DEMO_SCRIPT.md)
- [docs/FINAL_SMOKE_CHECKLIST.md](docs/FINAL_SMOKE_CHECKLIST.md)
- [docs/ENVIRONMENT.md](docs/ENVIRONMENT.md)
- [docs/AZURE_DEPLOYMENT.md](docs/AZURE_DEPLOYMENT.md)
- [docs/NEXUS_MASTER.md](docs/NEXUS_MASTER.md)
- [docs/IMPLEMENTATION_NOTES.md](docs/IMPLEMENTATION_NOTES.md)
- [docs/API_CONTRACTS.md](docs/API_CONTRACTS.md)
- [docs/SECURITY.md](docs/SECURITY.md)
- [docs/DATA_MODEL.md](docs/DATA_MODEL.md)

## Working Rule

Implement one PR at a time and keep the repo runnable after every PR. If implementation conventions change, update the repo docs in the same PR.
