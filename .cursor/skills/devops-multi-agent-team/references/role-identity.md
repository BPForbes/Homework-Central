# Role identity, questions, and handoffs

Every DevOps role **reads** this file and
[department-pods.md](department-pods.md). Do not paste either into
an agent prompt.

## Ask paths

Anyone may ask anyone when blocked. Prefer:

| Who has a question | Asks first | Then |
|--------------------|------------|------|
| QA | **Coder** (primary) | Reviewer |
| Coder | **Reviewer** (review Q&A) or **Researcher** | Orchestrator |
| Reviewer | **Coder** (review Q&A) or **Orchestrator** (Team Lead) | Coder |
| Security / CI / Verifier / Ticket Lead | Orchestrator or the role that opened the task | — |
| Orchestrator (Team Lead) | **The Client** (human) | — |

The Orchestrator is the only role that asks the human unless the
human spoke first. Coder ↔ Reviewer questions use the review
thread `## Q&A` and Push JSON `qa` (same ids). Do not open a
separate file.

## Human interrupt (side sprint)

Pause only conflicting work. Start from research, then code. Talk
to Coder, Reviewers, QA, and Security as needed. Write a role goal
under `.cursor/thoughts/non-finalized/`. Stay on the current
branch. An interrupt does **not** authorize a push.

## Role goals and Handoff

Write `.cursor/thoughts/non-finalized/goal-<role>-<topic>.md`.
Move to `finalized/` after QA (local; do not `git add`). See
[thoughts-layout.md](thoughts-layout.md).

```markdown
## Handoff
- From: <role>
- To: <role>
- Pass-along: <what the next role must know>
- Sent back because: <reason, or "n/a">
- Ask: <question, or "n/a">
```

A send-back without **Sent back because** is incomplete.

## Coder notify + Push JSON

Coder writes `push-<topic>.json` and a Handoff `To: Reviewer`
**before the first review**. Schema: [push-json.md](push-json.md).
Update it on every rewrite. Reviewers compare it to
`git diff <integration-base>...HEAD` and may write their own.
Bounce is not a queue. Satisfied does **not** authorize a git push.

## QA send-back and triage

Quality or bug-standard fail → VM review, Handoff `To: Coder`.
Track in `triage-<id>.md` ([triage-template.md](triage-template.md)).
Same Q&A + `qa` ids. `"files": {}` if the tree is unchanged. An
**active** item restarts research → coder → reviewer → QA.

## Timeline and reuse

Process output (threads, Push JSON, probes, CodeQL DBs, SARIF)
never lands. Product, pipeline, infra, tests, and durable `docs/`
**do** land as reviewer-approved keep-commits. Probe names and
`git checkout -- <exact path>` (never `git checkout -- .`):
[thoughts-layout.md](thoughts-layout.md). Search for an existing
helper before adding a parallel one.
