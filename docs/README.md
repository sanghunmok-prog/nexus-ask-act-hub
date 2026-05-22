# NEXUS Docs

This folder contains the final public documentation set for NEXUS.

## Recommended Reading Order

1. [`../README.md`](../README.md) — portfolio landing page, demo placeholder, architecture summary, and local quick start.
2. [`ARCHITECTURE.md`](ARCHITECTURE.md) — service boundaries and workflow diagrams.
3. [`API_CONTRACTS.md`](API_CONTRACTS.md) — REST, SSE, StructuredQuery, approval, document, and Toolbelt contracts.
4. [`DATA_MODEL.md`](DATA_MODEL.md) — SQL Server tables and persistence model.
5. [`SECURITY.md`](SECURITY.md) — SQL safety, approval gating, trace hygiene, and secret boundaries.
6. [`ENVIRONMENT.md`](ENVIRONMENT.md) — local environment variables and process isolation.
7. [`DEMO_SCRIPT.md`](DEMO_SCRIPT.md) — ultra-compressed no-narration screen-recording plan.
8. [`FINAL_SMOKE_CHECKLIST.md`](FINAL_SMOKE_CHECKLIST.md) — build, test, demo, and secret hygiene checks.
9. [`AZURE_DEPLOYMENT.md`](AZURE_DEPLOYMENT.md) — cloud deployment readiness guide.

## Public Docs Policy

The final public docs should explain the completed project, not the historical PR-by-PR planning process.

Historical planning notes, old PR milestones, internal agent instructions, and personal recording materials can be kept under:

```text
docs/archive/
```

The archive folder is intentionally not part of the recommended reading path.
