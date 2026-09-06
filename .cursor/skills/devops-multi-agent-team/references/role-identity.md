# Role identity, questions, and handoffs

Every DevOps role reads this file. Agents point here — do not copy these
rules into agent prompts.

## Ask paths

| Who has a question | Asks first | Then |
|--------------------|------------|------|
| QA | **Coder** | Reviewer |
| Coder | **Reviewer** or **Researcher** | Orchestrator |
| Reviewer | **Coder** or **Orchestrator** | Coder |
| Security / CI / Verifier / Ticket Lead | Orchestrator or task opener | — |
| Orchestrator | **The Client** | — |

Only the Orchestrator asks the human unless the human spoke first.

Coder ↔ Reviewer Q&A uses the review thread `## Q&A` table and Push JSON
`qa` array (same ids). Triage items reuse that shape.

## Human interrupt (side sprint)

1. Orchestrator pauses conflicting work only.
2. Start from research: docs integration, then code integration.
3. Talk to Coder, Reviewers, QA, Security as needed — do not wait for the original topic.
4. Write `.cursor/thoughts/non-finalized/goal-<role>-<topic>.md`.
5. Resume the original loop when parked or merged into the plan.
6. Interrupt does **not** authorize a push.
7. Stay on the current branch (`AGENTS.md` Git branches).

## Role goals

`.cursor/thoughts/non-finalized/goal-<role>-<topic>.md` while open.
Move to `finalized/` after QA PASS (local only). Do not `git add`.

## Handoff block (required)

```markdown
## Handoff
- From: <role>
- To: <role>
- Pass-along: …
- Sent back because: … or n/a
- Ask: … or n/a
```

## Process output vs product output

**Never commit:** review threads, Push JSON, triage/repro notes, probe
files, CodeQL DB/SARIF dumps.

**May commit (reviewer-approved):** product, pipeline, infra, test code,
durable `docs/`.

## Probes and shared worktree

Prefer `git clone --no-hardlinks . /tmp/probe`. In this worktree use a
reserved gitignored name: `_scratch/` or `.scratch` infix/suffix. Delete
before reporting.

Undo a probe that edited a tracked file: `git checkout -- <exact path>`.
Never `git checkout -- .`, `git restore :/`, or `git stash`.

Finish `git status --short` clean **of files you created**. List others
by path and leave in place. Backstop:
`scripts/check-clean-timeline.sh --history <integration-base>`.

## Reuse

Reuse existing helpers, scripts, and docs. Do not duplicate them.
Import or extend; flag duplicates in the research reuse map.

## Branch and push

Stay on the current non-`main` branch. Do not cut a new branch per increment
unless The Client asks.

**Only QA may give the OK to push.** Coders run applicable CodeQL on their
changes; that run does not authorize a push. After QA PASS, Orchestrator
compresses into one push (keep reviewer-approved Coder commits) —
[thoughts-layout.md](thoughts-layout.md).

Department handoffs: [department-pods.md](department-pods.md).
