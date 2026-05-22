# Environment

NEXUS uses explicit environment variables for local services and demo integrations.

Keep real values in user-secrets, shell exports, local launch settings, or a gitignored `.env` file. Never commit SQL passwords or GitHub tokens.

## Variables

| Variable | Process | Required | Purpose |
|---|---|---:|---|
| `NEXUS_SQL_CONNECTION_STRING` | Orchestrator, Toolbelt | yes | SQL Server persistence. |
| `NEXUS_TOOLBELT_BASE_URL` | Orchestrator | yes | Toolbelt HTTP base URL. |
| `LLM_MODE` | Orchestrator | recommended | Use `mock` for deterministic local demos. |
| `NEXUS_DEMO_GITHUB_REPO` | Orchestrator | for GitHub demo | Demo repo used when preparing approval-gated issue requests. |
| `NEXUS_GITHUB_TOKEN` | Toolbelt only | for GitHub execution | Fine-grained GitHub PAT with Issues read/write permission. |
| `NEXUS_GITHUB_ALLOWED_REPOS` | Toolbelt only | for GitHub execution | Comma-separated repo allowlist. |

## Recommended Local Values

### SQL Server

Use the database name and credentials from your local SQL bootstrap setup.

```text
NEXUS_SQL_CONNECTION_STRING=Server=localhost,1433;Database=<YOUR_NEXUS_DATABASE>;User Id=sa;Password=<YOUR_LOCAL_SA_PASSWORD>;TrustServerCertificate=True;
```

### Toolbelt Base URL

```text
NEXUS_TOOLBELT_BASE_URL=http://localhost:5062
```

Use the actual URL from AppHost or `dotnet run` if your local port differs.

### Mock Mode

```text
LLM_MODE=mock
```

`mock` mode is recommended for recording and portfolio demos.

### GitHub Demo Repo

```text
NEXUS_DEMO_GITHUB_REPO=<owner>/<repo>
```

Use a test/demo repo.

### GitHub Token

```text
NEXUS_GITHUB_TOKEN=<YOUR_FINE_GRAINED_PAT_WITH_ISSUES_WRITE>
```

Rules:

- configure this only in Toolbelt
- do not configure this in Orchestrator
- do not expose this to Angular
- use a fine-grained PAT scoped to the selected demo repo
- grant Issues read/write permission only

### GitHub Repo Allowlist

```text
NEXUS_GITHUB_ALLOWED_REPOS=<owner>/<repo>
```

Rules:

- required for GitHub execution
- must include `NEXUS_DEMO_GITHUB_REPO`
- disallowed repos are rejected before any GitHub call

## Process Boundary

### Orchestrator

```bash
export NEXUS_SQL_CONNECTION_STRING='Server=localhost,1433;Database=<YOUR_NEXUS_DATABASE>;User Id=sa;Password=<YOUR_LOCAL_SA_PASSWORD>;TrustServerCertificate=True;'
export NEXUS_TOOLBELT_BASE_URL='http://localhost:5062'
export LLM_MODE='mock'
export NEXUS_DEMO_GITHUB_REPO='<owner>/<repo>'
```

Start:

```bash
dotnet run --project src/Nexus.OrchestratorApi --launch-profile http
```

### Toolbelt

```bash
export NEXUS_SQL_CONNECTION_STRING='Server=localhost,1433;Database=<YOUR_NEXUS_DATABASE>;User Id=sa;Password=<YOUR_LOCAL_SA_PASSWORD>;TrustServerCertificate=True;'
export NEXUS_GITHUB_ALLOWED_REPOS='<owner>/<repo>'
read -s -p 'NEXUS_GITHUB_TOKEN: ' NEXUS_GITHUB_TOKEN
echo
export NEXUS_GITHUB_TOKEN
```

Start:

```bash
dotnet run --project src/Nexus.Mcp.Toolbelt --launch-profile http
```

### Angular

```bash
cd src/Nexus.Web
npm start
```

The Angular app should use the dev proxy or configured API base URL. Do not put secrets in frontend configuration.

## GitHub Label

If the demo request uses `nexus-demo`, create the label in the configured GitHub repository before recording. GitHub can reject issue creation when a requested label does not exist.

## Health Checks

Use the actual local ports printed by AppHost or `dotnet run`.

Common local demo ports:

```bash
curl -i http://localhost:5062/api/health
curl -i http://localhost:5281/api/health
```

## Secret Hygiene

Before publishing or recording:

```bash
grep -R "ghp_" .
grep -R "github_pat_" .
grep -R "NEXUS_GITHUB_TOKEN" .
```

Only placeholder references should appear in committed files.
