# Environment

This project uses explicit environment variables for local services and demo integrations. Keep real values in user-secrets, shell profile exports, local launch settings, or a gitignored `.env` file. Never commit local SQL passwords or GitHub tokens.

## Variables

### `NEXUS_SQL_CONNECTION_STRING`

Used by Orchestrator and Toolbelt code paths that read or write the local SQL Server database.

Example placeholder:

```text
Server=localhost,1433;Database=Nexus;User Id=sa;Password=<YOUR_LOCAL_SA_PASSWORD>;TrustServerCertificate=True;
```

### `NEXUS_TOOLBELT_BASE_URL`

Used by Orchestrator to call Toolbelt HTTP endpoints.

Example:

```text
http://localhost:5002
```

Use the actual local Toolbelt URL from AppHost or your direct `dotnet run` output.

### `LLM_MODE`

Controls planner behavior.

Recommended local demo value:

```text
mock
```

`LLM_MODE=mock` must remain supported for deterministic local development and portfolio demos.

### `NEXUS_DEMO_GITHUB_REPO`

Configured demo repository for approval-gated GitHub issue creation.

Example placeholder:

```text
<owner>/<repo>
```

Use a test/demo repo, not a production repository.

### `NEXUS_GITHUB_TOKEN`

GitHub token used by Toolbelt when executing `github.create_issue`.

Example placeholder:

```text
<YOUR_FINE_GRAINED_PAT_WITH_ISSUES_WRITE>
```

Rules:

- This token belongs only to the Toolbelt process.
- The Orchestrator should not receive this token.
- Use a fine-grained GitHub PAT with selected repository access.
- The token needs Issues read/write permission for the selected demo repository.
- Never commit the token.

### `NEXUS_GITHUB_ALLOWED_REPOS`

Comma-separated repository allowlist for Toolbelt GitHub issue creation.

Example placeholder:

```text
<owner>/<repo>
```

Rules:

- This variable is mandatory for GitHub execution.
- It must include the configured `NEXUS_DEMO_GITHUB_REPO`.
- Repositories not on the allowlist are rejected before any GitHub call.

## Labels

If the demo uses labels, create the `nexus-demo` label in the configured demo repo before recording. GitHub may reject issue creation when a requested label does not exist.

## Recommended Process Boundary

Orchestrator:

- `NEXUS_SQL_CONNECTION_STRING`
- `NEXUS_TOOLBELT_BASE_URL`
- `LLM_MODE`
- `NEXUS_DEMO_GITHUB_REPO`

Toolbelt:

- `NEXUS_SQL_CONNECTION_STRING`
- `NEXUS_GITHUB_TOKEN`
- `NEXUS_GITHUB_ALLOWED_REPOS`

Do not copy `NEXUS_GITHUB_TOKEN` into Orchestrator configuration.
