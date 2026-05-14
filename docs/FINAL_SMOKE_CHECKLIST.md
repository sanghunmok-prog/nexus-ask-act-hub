# Final Smoke Checklist

Use this checklist before recording or publishing the final portfolio demo.

## Build And Test

- [ ] `dotnet build Nexus.slnx`
- [ ] `dotnet test Nexus.slnx`
- [ ] `dotnet test tests/Nexus.OrchestratorApi.Tests/Nexus.OrchestratorApi.Tests.csproj`
- [ ] `dotnet test tests/Nexus.Mcp.Toolbelt.Tests/Nexus.Mcp.Toolbelt.Tests.csproj`
- [ ] `cd src/Nexus.Web && npm run build`
- [ ] `cd src/Nexus.Web && npm test -- --watch=false`

## Services

- [ ] Toolbelt health returns success: `GET /api/health`
- [ ] Orchestrator health returns success: `GET /api/health`
- [ ] Angular dev server starts successfully
- [ ] Angular dev proxy reaches Orchestrator
- [ ] Orchestrator can reach Toolbelt through `NEXUS_TOOLBELT_BASE_URL`
- [ ] SQL connection string points at the local demo database

## Demo Path

- [ ] Normal read path returns delayed orders and policy citations
- [ ] Trace shows `docs.search`
- [ ] Trace shows `db.get_schema_summary`
- [ ] Trace shows `db.query_readonly`
- [ ] Correction retry prompt emits first read failure, `tool.retry`, and corrected success
- [ ] Retry stops after the bounded correction budget
- [ ] Action prompt creates a pending approval
- [ ] Approval pending UI displays repo, title, labels, risk, requested user, and approval id
- [ ] Reject path marks the pending item rejected/failed without executing
- [ ] Approve path marks the checkpoint `ReadyToResume`
- [ ] Ready list displays approved action
- [ ] Explicit execute creates a GitHub issue in the configured demo repo
- [ ] Execute response includes the GitHub issue URL
- [ ] Duplicate execute returns `409`
- [ ] Database checkpoint status is `Completed` after successful execute
- [ ] Demo issue can be closed after recording if desired

## Security And Hygiene

- [ ] `grep -R "ghp_" .`
- [ ] `grep -R "github_pat_" .`
- [ ] Grep for the prohibited local SQL password pattern used during earlier smoke tests
- [ ] Confirm only placeholder secrets are present
- [ ] Confirm Orchestrator environment does not include `NEXUS_GITHUB_TOKEN`
- [ ] Confirm Toolbelt environment includes `NEXUS_GITHUB_ALLOWED_REPOS`
- [ ] Confirm demo repo is a test/demo repository
- [ ] Confirm no raw SQL, stack traces, connection strings, or tokens appear in SSE/API responses

## Recording Notes

- [ ] Use `LLM_MODE=mock` for deterministic demo behavior
- [ ] Use a clean browser window
- [ ] Keep GitHub repo page ready for verifying the created issue
- [ ] Close or label demo issues after recording according to your repo hygiene preference
