# NEXUS Demo Script

This script is designed for a portfolio walkthrough. It shows the complete NEXUS story: ask across data and documents, recover from a bounded read-path error, then execute a governed external action only after explicit approval.

Before recording, use a test/demo GitHub repository and confirm `NEXUS_GITHUB_ALLOWED_REPOS` includes that repo.

## Demo 1: Ask

Prompt:

```text
Which delayed orders are most at risk, and what policy applies?
```

Expected trace:

- `workflow.started`
- `tool.call` / `tool.result` for `docs.search`
- `tool.call` / `tool.result` for `docs.get_chunk` when a citation is found
- `tool.call` / `tool.result` for `db.get_schema_summary`
- `tool.call` / `tool.result` for `db.query_readonly`
- `assistant.message`
- `done`

Expected result:

- Delayed orders table with order id, status, carrier, expected ship date, actual ship date, and delay reason.
- Policy section that explains the applicable shipping delay guidance.
- Citation section that references the policy document and chunk.

Talk track:

NEXUS is not just chatting over documents. It combines a governed SQL read path with document retrieval, then presents a trace so the operator can see which tools were used.

## Demo 2: Recover

Prompt:

```text
Which delayed orders need correction retry?
```

Expected trace:

- `workflow.started`
- normal read-path setup events
- first `db.query_readonly` attempt fails with a sanitized validation-style result
- `tool.retry`
- corrected `db.query_readonly` call
- corrected query succeeds
- `assistant.message`
- `done`

What to point out:

- The retry budget is bounded: one correction retry, two total `db.query_readonly` attempts.
- Correction applies only to recoverable read-path schema/allowlist failures.
- The corrected request still goes through StructuredQuery and Toolbelt validation.
- The trace is operational and sanitized. It does not expose chain-of-thought, raw SQL, stack traces, connection strings, or secrets.

Talk track:

When a read query uses the wrong schema shape, the Orchestrator can recover once in a controlled way. It does not keep retrying, does not bypass the allowlist, and does not apply this retry behavior to write actions.

## Demo 3: Act + Govern

Prompt:

```text
Create a GitHub issue for the delayed shipment findings.
```

Expected trace:

- `workflow.started`
- `tool.call` for `github.create_issue` with `requiresApproval=true`
- `checkpoint.saved`
- `approval.required`
- `assistant.message`
- `done`

Expected UI flow:

1. Pending approval appears in the approval panel.
2. Show the repo, issue title, labels, risk summary, requested user, and approval id.
3. Click approve.
4. Point out that approve does not create the issue.
5. The approved action moves to Ready to Execute.
6. Click execute.
7. The UI shows the GitHub issue URL returned from Toolbelt.
8. Triggering execute again for the same approval should be blocked with `409`.

What to point out:

- GitHub execution creates a real issue in the configured demo repo.
- Use a test/demo repo.
- Close demo issues after recording if desired.
- The GitHub token belongs only to Toolbelt.
- Orchestrator persists the approval and checkpoint state, then calls Toolbelt only after explicit execute.
- Duplicate execute is blocked by the checkpoint transition from `ReadyToResume` to `Executing`.

Talk track:

This is the main governance story. NEXUS can prepare an external action, but it cannot write to GitHub until a user approves the request and then explicitly executes the approved action.

## Closing Line

NEXUS demonstrates a practical Ask + Act + Govern workflow: answer with data and citations, recover safely from bounded read-path mistakes, and require explicit human control for external writes.
