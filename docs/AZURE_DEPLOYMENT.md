# Azure Deployment Readiness

This guide describes how NEXUS could be deployed to Azure.

It is a readiness guide, not a claim that this repository already includes Azure infrastructure-as-code or a completed Azure deployment.

## Target Shape

```text
Angular static frontend
  -> Orchestrator API
      -> Azure SQL / SQL Server
      -> Toolbelt API
          -> Azure SQL / SQL Server
          -> GitHub Issues API
```

## Components

| Component | Azure-ready option | Notes |
|---|---|---|
| Angular frontend | Azure Static Web Apps or static website hosting | Configure API base URL and CORS intentionally. |
| Orchestrator API | Azure App Service, Azure Container Apps, or equivalent | Public/protected API for frontend calls. |
| Toolbelt API | Internal App Service, Container App, or private service | Prefer restricted exposure. Owns GitHub token. |
| SQL persistence | Azure SQL or SQL Server | Validate vector type support for document retrieval. |
| Secrets | Managed secret store | Do not store tokens in frontend config or committed files. |

## Orchestrator Responsibilities

- `POST /api/chat/stream`
- approval pending/ready/approve/reject/execute endpoints
- document upload and ingestion endpoints
- audit/checkpoint persistence
- calls to Toolbelt through `NEXUS_TOOLBELT_BASE_URL`

The Orchestrator should not receive `NEXUS_GITHUB_TOKEN`.

## Toolbelt Responsibilities

- SQL read tool endpoints
- document search and chunk lookup endpoints
- `POST /api/tools/github/create-issue`
- GitHub repo allowlist enforcement
- GitHub token usage

The Toolbelt owns `NEXUS_GITHUB_TOKEN`.

## Environment Mapping

### Orchestrator

```text
NEXUS_SQL_CONNECTION_STRING
NEXUS_TOOLBELT_BASE_URL
LLM_MODE
NEXUS_DEMO_GITHUB_REPO
```

### Toolbelt

```text
NEXUS_SQL_CONNECTION_STRING
NEXUS_GITHUB_TOKEN
NEXUS_GITHUB_ALLOWED_REPOS
```

### Frontend

```text
API base URL or hosting-specific proxy configuration
```

Do not expose secrets through frontend build-time variables.

## Database Migration

Use a controlled migration step for schema changes.

Recommended pattern:

```bash
dotnet ef database update \
  --project <migration-project> \
  --startup-project src/Nexus.OrchestratorApi
```

For demo seed data, apply seed scripts only to non-production/demo databases.

## Security Considerations

- Use least-privilege SQL credentials.
- Store secrets in a managed secret store.
- Use a fine-grained GitHub PAT scoped to the selected demo repo.
- Do not configure `NEXUS_GITHUB_TOKEN` in Orchestrator.
- Lock down Toolbelt network exposure.
- Add service-to-service authentication before production use.
- Configure explicit CORS origins.
- Add structured logging with secret redaction.
- Add rate limits and request size limits.

## CORS

If Angular and Orchestrator are served from different origins:

- allow only the deployed frontend origin
- do not use wildcard CORS in production
- keep Toolbelt inaccessible from the browser

## Known Gaps

- No Azure IaC is included.
- No CI/CD deployment workflow is included.
- No production SSO or RBAC is implemented.
- No service-to-service authentication is implemented.
- No production monitoring/alerting is included.
- This is not a production deployment record.

## Production Hardening Checklist

- [ ] Add identity and RBAC.
- [ ] Add service-to-service authentication between Orchestrator and Toolbelt.
- [ ] Store secrets in a managed secret store.
- [ ] Use least-privilege SQL credentials.
- [ ] Restrict Toolbelt network exposure.
- [ ] Configure explicit CORS origins.
- [ ] Add deployment IaC.
- [ ] Add CI/CD deployment workflow.
- [ ] Add backup and restore plan.
- [ ] Add monitoring for failed approval executions.
- [ ] Add structured operational logging with secret redaction.
