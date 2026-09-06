# Role identity, questions, and handoffs

Every DevOps role reads this file. Put the same rules in the agent's
identity so a bounce or a side sprint keeps context.

## Ask paths (main)

Anyone may ask anyone when blocked. Prefer this order:

| Who has a question | Asks first | Then |
|--------------------|------------|------|
| QA | **Coder** (primary) | Reviewer |
| Coder | **Reviewer** (review Q&A) or **Researcher** | Orchestrator |
| Reviewer | **Coder** (review Q&A) or **Orchestrator** (Team Lead) | Coder |
| Security / CI / Verifier / Ticket Lead | Orchestrator or the role that opened the task | — |
| Orchestrator (Team Lead) | **The Client** (human) | — |

The Orchestrator is the only role that asks the human unless the human
spoke first.

Coder ↔ Reviewer questions on a change set use the review thread
`## Q&A` table and the Push JSON `qa` array (same ids). That is the
exchange; do not open a separate file. Team Lead calls still go to
the Orchestrator.

## Human interrupt (side sprint)

When The Client gives instructions while the skill is running:

1. The Orchestrator pauses the current pod only as needed.
2. Start a **side sprint from research**: how the ask integrates with
   existing docs, then how it integrates in code.
3. Talk to Coder, Reviewers, QA, and Security as that sprint needs —
   do not wait for the original topic to finish.
4. Write a role goal under `.cursor/thoughts/non-finalized/`.
5. Resume the original loop when the side sprint is parked or merged
   into the plan.
6. A human interrupt does **not** authorize a push.
7. Stay on the current branch. Do not cut a new branch for the
   interrupt or the next increment. Canonical: `AGENTS.md` Git
   branches.

## Role goals

Each role type writes its own goal file while the concept is open:

`.cursor/thoughts/non-finalized/goal-<role>-<topic>.md`

Move it to `.cursor/thoughts/finalized/` when QA signs off on that
concept (local only; see [thoughts-layout.md](thoughts-layout.md)).
Do not `git add` either file.

## Handoff block (required)

When sending work to another role, or sending it back, append:

```markdown
## Handoff
- From: <role>
- To: <role>
- Pass-along: <what the next role must know>
- Sent back because: <reason, or "n/a">
- Ask: <question, or "n/a">
```

A send-back without **Sent back because** is incomplete. The receiving
role copies that block into its goal or the review thread.

## Coder notify + Push JSON

The Coder writes `.cursor/thoughts/non-finalized/push-<topic>.json`
and a Handoff `To: Reviewer` **before the first review**. A fresh
change has no findings to close; `closes` may be empty. Reviewers
do not start without that file. Schema: [push-json.md](push-json.md).

Every Coder rewrite updates that JSON before Reviewers look
again, and again when a change **should** close review feedback.
The Handoff names the findings that should close. Reviewers
compare the new JSON to the real local git history
(`git diff <integration-base>...HEAD`). Reviews may run long;
do not rush Satisfied.

Reviewers always compare that JSON to the real
`git diff <integration-base>...HEAD`. They write their own Push
JSON when they have line-level feedback. Bounce is **not** a
linear queue — either side may Push when they have something new,
including a question or an answer. Continue until both are happy
(Satisfied). Open `qa` rows must be answered or withdrawn first.
Satisfied still does **not** authorize a git push.

## QA send-back and triage

QA may send work **back to the Coder** when a quality or bug
standard fails. Run that review on a **VM** (this environment or
`computerUse`), not Markdown-only. Handoff `To: Coder` with
**Sent back because**.

QA tracks bugs and discoveries during command execution in
`.cursor/thoughts/non-finalized/triage-<id>.md`
([triage-template.md](triage-template.md)). Triage items use the
same **Q&A** table + Push JSON `qa` (same ids) as the review
thread. Either side may ask or answer. If there is no tree
change, keep `files` as `{}` and do not commit. An **active**
item restarts research → coder → reviewer → QA for that id.

## What lands on the committed timeline

The rule is about the **class of output**, not who typed it.

Reviewer, Security and QA **process output** never lands: review
threads, Push JSON, triage and repro notes, probe files, CodeQL
databases and SARIF dumps. Product, pipeline, infra, test code and
durable `docs/` updates *do* land as reviewer-approved keep-commits,
whichever role drafted them.

Prefer probing in a throwaway clone
(`git clone --no-hardlinks . /tmp/probe`). When a probe must sit in the
shared worktree, use a reserved lower-case name — a `_scratch/`
directory or a `.scratch` infix — and delete it before reporting. Undo
a probe that edited a tracked file with `git checkout -- <exact path>`,
never `git checkout -- .`, `git restore :/` or `git stash`. Finish with
`git status --short` clean **of files you created**; list anything else
by path and leave it in place.

`scripts/check-clean-timeline.sh` enforces this in CI, and its
`--history <base>` form also catches a blob added and later deleted
inside the branch. Details and the fixed-name probe list:
[thoughts-layout.md](thoughts-layout.md).

## Reuse existing structures

Every role searches for an existing helper, script, workflow, or
doc **before** adding a parallel one. Researcher records a reuse
map. Reviewer request-changes when the Coder duplicated code that
could be imported. Coder asks Researcher when unsure.
