# 2-Minute Demo Script

## Goal
Show that NEXUS can answer safely and act safely.

## Demo step 1 — Hybrid answer

Prompt:
"Show delayed shipments last 30 days and the policy that applies."

Expected:
- Trace shows SQL work and document search work
- Response includes delayed orders
- Response includes policy citations

## Demo step 2 — Approval-gated action

Prompt:
"Create a GitHub issue for the shipping team with recommended actions."

Expected:
- Approval card appears
- Trace shows checkpoint saved
- After approval, workflow resumes
- GitHub issue is created
- Audit trail is present

## Demo closing line
"AI cannot change systems without approval. Everything is auditable."