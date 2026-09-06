# Agent commands

The Orchestrator and every subagent accept these as `/name` or
plain wording (`set a goal`, `code review this`, `reproduce it`, `create a
subagent`). Invocable copies live in `.cursor/commands/`. Use any installed
`/` skill the same way when it fits the work.

Working notes, review threads, goal logs, and repro notes are Markdown
under `.cursor/thoughts/non-finalized/` while the concept is open
(**do not commit them**). Push JSON lives there too. After QA PASS,
move them to `.cursor/thoughts/finalized/` (still local). See
[thoughts-layout.md](thoughts-layout.md) and [push-json.md](push-json.md).
Do not write thought dumps into `docs/`. Durable history is `docs/`
or skill `references/` only.
Stay on the current non-`main` branch. Do not cut a new branch for
each increment (`AGENTS.md` Git branches).
After QA PASS, compress the skill workstream into one push
(keep reviewer-approved Coder commits)
([thoughts-layout.md](thoughts-layout.md) One push).
QA `/triage`: [triage-template.md](triage-template.md).

**Only QA may give the OK to push.** Anyone who changes code (Coder /
primary developers) must run applicable CodeQL first; that run does not
authorize a push. DO NOT PUSH, PUBLISH, OPEN OR UPDATE A PULL REQUEST,
MERGE, OR OTHERWISE SUBMIT CODE UNTIL QA MARKS THE PUBLISH GATE PASS.

## `/goal` — do until you achieve X

Also: `set a goal`, `do until X`, `keep going until X`.

1. Write the objective to `.cursor/thoughts/non-finalized/goal-<topic>.md`
   (acceptance criteria, non-goals, done-when). Each role may also write
   `goal-<role>-<topic>.md`.
2. If the human invoked a long-running goal, also use the Cursor goal tools
   (`CreateGoal` / `UpdateGoal`).
3. Keep looping the DevOps cycle until X is actually achieved. Do not stop at a
   plan or a partial implement.
4. Mark the local goal file (and `UpdateGoal`) complete only when the criteria
   are met, or the human stops the goal.

## `/create-subagent` — spawn roles asynchronously

Also: `create a subagent`, `spawn`, `Task` tool.

- Spawn roles from `.cursor/agents/devops-*.md` with Cursor `Task`.
- Run them **asynchronously in pods**. Agent files set `is_background:
  true`. Cursor `Task` uses `run_in_background: true`. Launch a whole
  group in one turn; do not queue roles one-by-one.
- Do not poll a background subagent. Continue other work or end the turn;
  the completion notification is enough.
- The Orchestrator synthesizes. Subagents do not push or open PRs.
  The Orchestrator may push only after **QA gives the OK**.

| Role | Agent file |
|------|------------|
| Researcher | `devops-researcher.md` |
| Reviewer | `devops-reviewer.md` |
| CI Engineer | `devops-ci-engineer.md` |
| QA | `devops-quality-engineer.md` |
| Security | `devops-security-engineer.md` |
| Ticket Lead | `devops-ticket-lead.md` |
| Verifier | `devops-verifier.md` |
| Integrator | `devops-integrator.md` |
| Communicator | `devops-communicator.md` |

## `/code-review` — look at, do not edit

Also: `/review-bugbot`, `review the diff`, `look but don't edit`.

**Primary owner: QA.** Reviewers may use the same inspect-only bar.

- Confirm the Coder Push JSON exists. Read it as an index, then
  **always** `git diff <integration-base>...HEAD`, tests, logs,
  and SARIF. Compare mock and real diff. Write findings into
  `.cursor/thoughts/non-finalized/review-<topic>.md`.
- **Do not edit product code, workflows, or docs to "fix" findings** while
  acting as `/code-review`. Hand remediations to the Coder.
- Do not push. **Only QA may give the OK to push.**

## `/repro` — reproduce before declaring a cause

Also: `reproduce`, `write a repro`.

- Recreate the failure with exact commands, inputs, and exit codes.
- Write the repro to `.cursor/thoughts/non-finalized/repro-<topic>.md`.
- Do not claim a root cause until the repro ran (or the environment cannot
  run it — then say so).

## `/triage` — QA tracks a bug or discovery

Also: `open triage`, `track this bug`.

**Owner: QA.** Copy [triage-template.md](triage-template.md) to
`.cursor/thoughts/non-finalized/triage-<id>.md`. Set State `active`.
Handoff `To: Coder`. The Orchestrator restarts research → coder →
reviewer → QA for that id. Invocable copy: `.cursor/commands/triage.md`.

## Other `/` skills

Orchestrator and subagents may use any installed slash skill when it matches
the phase: `/buildkite-*`, `/sonar-*`, `/review-security`, `/browser-automation`,
`/share-video`, `/docs-canvas`, `/loop`, `/babysit`, `/secure-dependency-health-check`,
and the same names without the slash.
