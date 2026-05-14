# Azure Deployment Readiness Guide

This is a deployment readiness guide for future work. The repository does not include Azure infrastructure-as-code and does not claim an Azure deployment is already complete.

## High-Level Options

NEXUS can be deployed as separate services:

- Orchestrator API as an API service.
- Toolbelt API as a separate internal API service.
- Angular app as a static frontend.
- SQL Server or Azure SQL as the persistence layer.

The service split is intentional. It keeps approval policy and workflow orchestration separate from tool execution and external credentials.

## Orchestrator API

The Orchestrator should be deployed as a public or protected API service that the Angular frontend can call.

Responsibilities:

- `POST /api/chat/stream`
- approval pending, ready, approve, reject, and execute endpoints
- document upload and ingestion endpoints
- audit/checkpoint persistence
- calls to Toolbelt through `NEXUS_TOOLBELT_BASE_URL`

The Orchestrator should not receive `NEXUS_GITHUB_TOKEN`.

## Toolbelt API

The Toolbelt should be deployed as a separate API service, ideally with restricted network exposure.

Responsibilities:

- SQL read tool endpoints
- document search and chunk lookup endpoints
- `POST /api/tools/github/create-issue`

The Toolbelt owns the GitHub token and repository allowlist.

## Angular Static Frontend

The Angular app can be built with:

```bash
cd src/Nexus.Web
npm run build
```

Deployment options include Azure Static Web Apps, Azure Storage static website hosting, or another static hosting service. Configure API base URLs and CORS according to the chosen hosting model.

## SQL Server / Azure SQL Considerations

For an Azure-hosted demo, Azure SQL is the natural target for the SQL persistence layer.

Consider:

- applying schema and seed scripts in a controlled migration step
- using least-privilege database users
- enabling TLS and appropriate firewall rules
- validating SQL Server vector type support for document retrieval
- separating demo data from any real customer or production data

## Environment Variable Mapping

Orchestrator:

- `NEXUS_SQL_CONNECTION_STRING`
- `NEXUS_TOOLBELT_BASE_URL`
- `LLM_MODE`
- `NEXUS_DEMO_GITHUB_REPO`

Toolbelt:

- `NEXUS_SQL_CONNECTION_STRING`
- `NEXUS_GITHUB_TOKEN`
- `NEXUS_GITHUB_ALLOWED_REPOS`

Frontend:

- API base URL or proxy configuration appropriate for the hosting option

## Secret Handling

Recommended practices:

- Store SQL passwords and GitHub tokens in a managed secret store.
- Do not place secrets in frontend configuration.
- Do not place `NEXUS_GITHUB_TOKEN` in Orchestrator configuration.
- Rotate the GitHub PAT after demos if it was exposed to local machines or shared environments.
- Prefer a fine-grained PAT scoped to the selected demo repo with Issues read/write permission only.

## Service-To-Service URL Requirements

The Orchestrator must be able to reach Toolbelt at `NEXUS_TOOLBELT_BASE_URL`.

For cloud deployment, decide whether Toolbelt is:

- private/internal and reachable only by Orchestrator
- public but protected by network and identity controls

The portfolio demo code currently uses simple HTTP service calls. Production hardening should add service authentication.

## CORS Considerations

If Angular and Orchestrator are served from different origins, configure Orchestrator CORS intentionally.

Do not enable broad wildcard CORS for a real deployment. Allow only the deployed frontend origin.

## Known Limitations

- No Azure IaC is included.
- No CI/CD deployment workflow is included.
- No production auth/RBAC is implemented.
- Service-to-service authentication is not implemented.
- The frontend demo assumes the existing API contracts and dev proxy pattern unless deployment-specific configuration is added later.
- This guide is readiness documentation, not a deployment record.

## Production Hardening Checklist

- [ ] Add real identity and RBAC.
- [ ] Add service-to-service authentication between Orchestrator and Toolbelt.
- [ ] Store secrets in a managed secret store.
- [ ] Use least-privilege SQL credentials.
- [ ] Lock down Toolbelt network exposure.
- [ ] Configure explicit CORS origins.
- [ ] Add structured operational logging with secret redaction.
- [ ] Add deployment IaC in a separate PR/project.
- [ ] Add CI/CD deployment workflow in a separate PR/project.
- [ ] Add backup and restore plan for SQL.
- [ ] Add rate limits and request size limits appropriate for deployment.
- [ ] Add monitoring and alerting for failed approval executions.
