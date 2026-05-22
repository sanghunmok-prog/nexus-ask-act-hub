# Final Smoke Checklist

Use this checklist before publishing the repo or recording the final demo.

## Build And Test

```bash
dotnet build Nexus.slnx
dotnet test Nexus.slnx

dotnet test tests/Nexus.OrchestratorApi.Tests/Nexus.OrchestratorApi.Tests.csproj
dotnet test tests/Nexus.Mcp.Toolbelt.Tests/Nexus.Mcp.Toolbelt.Tests.csproj

cd src/Nexus.Web
npm run build
npm test -- --watch=false
```

## Service Health

- [ ] SQL Server local container is running.
- [ ] Orchestrator health returns success: `GET /api/health`.
- [ ] Toolbelt health returns success: `GET /api/health`.
- [ ] Angular dev server starts successfully.
- [ ] Angular dev proxy reaches Orchestrator.
- [ ] Orchestrator can reach Toolbelt through `NEXUS_TOOLBELT_BASE_URL`.
- [ ] SQL connection string points at the local demo database.

Common local demo health commands:

```bash
curl -i http://localhost:5062/api/health
curl -i http://localhost:5281/api/health
```

Use the actual ports printed by AppHost or `dotnet run` if your local ports differ.

## Database

- [ ] EF Core migrations or SQL bootstrap scripts have been applied.
- [ ] `Orders` contains delayed shipment demo rows.
- [ ] `PolicyDocuments` contains demo policy metadata.
- [ ] `PolicyChunks` contains embedded chunks.
- [ ] `ApprovalRequest` and `AgentCheckpoint` tables exist.
- [ ] Local demo database has no confusing stale approvals, or stale rows are understood before recording.

## Ask Path

Prompt:

```text
Which delayed orders are most at risk, and what policy applies?
```

Expected:

- [ ] answer includes delayed orders
- [ ] answer includes policy section
- [ ] answer includes citation metadata
- [ ] trace shows `docs.search`
- [ ] trace shows `docs.get_chunk` when citation is found
- [ ] trace shows `db.get_schema_summary`
- [ ] trace shows `db.query_readonly`
- [ ] no raw SQL appears in the UI

## Recover Path

Prompt:

```text
Which delayed orders need correction retry?
```

Expected:

- [ ] first `db.query_readonly` attempt fails with sanitized validation-style result
- [ ] trace emits `tool.retry`
- [ ] corrected `db.query_readonly` succeeds
- [ ] there is no attempt 3
- [ ] no chain-of-thought, raw SQL, stack trace, or connection string appears

## Govern Path

Prompt:

```text
Create a GitHub issue for the delayed shipment findings.
```

Expected:

- [ ] trace shows `github.create_issue`
- [ ] trace marks action as `requiresApproval=true`
- [ ] `ApprovalRequest` is created
- [ ] `AgentCheckpoint` is created with `WaitingApproval`
- [ ] pending approval UI displays repo, title, labels, risk summary, requested user, and approval id
- [ ] no GitHub issue is created before approval

## Approval And Execute

- [ ] Reject path marks approval `Rejected` and checkpoint `Failed`.
- [ ] Approve path marks approval `Approved`.
- [ ] Approve path marks checkpoint `ReadyToResume`.
- [ ] Approve does not create a GitHub issue.
- [ ] Ready list displays the approved action.
- [ ] Explicit execute creates a GitHub issue in the configured demo repo.
- [ ] Execute response includes issue number and URL.
- [ ] GitHub repo shows the created issue.
- [ ] Checkpoint is `Completed` after successful execute.

## Duplicate Execute Guard

After a successful execute, call execute again for the same approval.

Expected:

```http
HTTP/1.1 409 Conflict
```

Checklist:

- [ ] duplicate execute returns `409`
- [ ] no second GitHub issue is created
- [ ] Toolbelt is not called for the duplicate execute

## Secret Hygiene

Run:

```bash
grep -R "ghp_" .
grep -R "github_pat_" .
grep -R "NEXUS_GITHUB_TOKEN" .
```

Confirm:

- [ ] no real GitHub token is committed
- [ ] no real SQL password is committed
- [ ] Orchestrator environment does not include `NEXUS_GITHUB_TOKEN`
- [ ] Toolbelt environment includes `NEXUS_GITHUB_ALLOWED_REPOS`
- [ ] demo repo is a test/demo repository
- [ ] SSE/API responses do not expose secrets, raw SQL, stack traces, connection strings, or raw GitHub responses

## Recording

- [ ] Use `LLM_MODE=mock`.
- [ ] Use a clean browser window.
- [ ] Keep GitHub repo Issues page ready.
- [ ] Keep old demo issues closed or clearly labeled.
- [ ] Use the short caption plan from `docs/DEMO_SCRIPT.md`.
