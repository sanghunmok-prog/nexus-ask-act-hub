# Demo Script

This is the final ultra-compressed demo plan for the README video or GIF.

No narration. No terminal setup. No build steps. Show only screen recording plus short on-screen captions.

Recommended runtime: **75–90 seconds**.

## Recording Setup

Open:

- NEXUS Angular UI
- GitHub demo repository Issues page
- optional terminal or API client only for duplicate `409` proof

Use:

```text
LLM_MODE=mock
```

Use a test/demo GitHub repository.

Do not show real tokens.

## Required Timeline

| Time | Screen | Action | Caption |
|---:|---|---|---|
| 0–3s | README or NEXUS UI | Show project title and core value. | `Ask · Recover · Govern` |
| 3–15s | NEXUS UI | Enter: `Which delayed orders are most at risk, and what policy applies?` | `Ask with data` |
| 15–28s | Answer + Trace | Show delayed orders, policy section, citations, `docs.search`, `db.get_schema_summary`, `db.query_readonly`. | `SQL + policy` |
| 28–38s | NEXUS UI | Enter: `Which delayed orders need correction retry?` | `Recover safely` |
| 38–52s | Trace | Show first read failure, `tool.retry`, corrected `db.query_readonly`, success. | `One retry only` |
| 52–63s | NEXUS UI | Enter: `Create a GitHub issue for the delayed shipment findings.` | `Action requested` |
| 63–72s | Trace + Approval Panel | Show `github.create_issue`, `requiresApproval=true`, `checkpoint.saved`, `approval.required`. | `Approval required` |
| 72–80s | Pending Approvals | Click `Approve`; show item moves to Ready to Execute. | `Approve ≠ execute` |
| 80–88s | Ready to Execute | Click `Execute`; show issue number and URL. | `Explicit execute` |
| 88–95s | GitHub | Open created GitHub issue. | `Issue created` |
| Optional | Terminal/API/UI | Execute same approval again; show `409 Conflict`. | `Duplicate blocked` |

## Required Outputs

Show these outputs clearly:

- assistant answer with delayed orders
- policy section with citation
- trace event: `docs.search`
- trace event: `docs.get_chunk`
- trace event: `db.get_schema_summary`
- trace event: `db.query_readonly`
- retry trace: first failure
- retry trace: `tool.retry`
- retry trace: corrected query success
- action trace: `github.create_issue` with `requiresApproval=true`
- `ApprovalRequest` pending card
- `AgentCheckpoint` status or UI equivalent: `WaitingApproval`
- approved item in Ready to Execute
- GitHub issue number and URL
- actual GitHub issue page
- optional duplicate execute `409`

## Caption Guide

Use 3–5 words per caption.

| Scene | Caption |
|---|---|
| Intro | `Ask · Recover · Govern` |
| First prompt | `Ask with data` |
| Read trace | `SQL + policy` |
| Answer | `Cited answer` |
| Retry prompt | `Recover safely` |
| Retry event | `One retry only` |
| Retry success | `Corrected query success` |
| Action prompt | `External action requested` |
| Approval gate | `Approval required` |
| Pending card | `Human decision needed` |
| Approve click | `Approve ≠ execute` |
| Ready list | `Ready to execute` |
| Execute click | `Explicit execute` |
| GitHub result | `Issue created` |
| Duplicate guard | `Duplicate blocked` |

## What Not To Show

Do not show:

- package restore
- build output
- test output
- environment variable setup
- real GitHub token
- local SQL password
- long terminal logs
- source code scrolling
- narration subtitles longer than one short phrase

## Success Criteria

The viewer should understand three things in under 90 seconds:

1. NEXUS answers with SQL data and policy citations.
2. NEXUS can recover once from a safe read-path schema mistake.
3. NEXUS cannot write to GitHub until approval and explicit execute.
