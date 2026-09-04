# Agent commands

The Orchestrator and every subagent accept these as `/name` or
plain wording (`set a goal`, `code review this`, `reproduce it`, `create a
subagent`). Invocable copies live in `.cursor/commands/`. Use any installed
`/` skill the same way when it fits the work.

Working notes, review threads, goal logs, and repro notes are **local Markdown
under `.cursor/reviews/`**. They are gitignored. Do not `git add` them.

## `/goal` — do until you achieve X

Also: `set a goal`, `do until X`, `keep going until X`.

1. Write the objective to `.cursor/reviews/goal-<topic>.md` (acceptance criteria,
   non-goals, done-when).
2. If the human invoked a long-running goal, also use the Cursor goal tools
   (`CreateGoal` / `UpdateGoal`).
3. Keep looping the DevOps cycle until X is actually achieved. Do not stop at a
   plan or a partial implement.
4. Mark the local goal file (and `UpdateGoal`) complete only when the criteria
   are met, or the human stops the goal.

## `/create-subagent` — spawn roles asynchronously

Also: `create a subagent`, `spawn`, `Task` tool.

- Spawn roles from `.cursor/agents/devops-*.md` with Cursor `Task`.
- Run them **asynchronously** (`run_in_background: true`) unless the next
  step is blocked on that one result and there is no other work.
- Do not poll a background subagent. Continue other work or end the turn;
  the completion notification is enough.
- The Orchestrator synthesizes. Subagents do not push or open PRs.

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

- Read the diff, tests, logs, and SARIF. Write findings into
  `.cursor/reviews/<topic>.md`.
- **Do not edit product code, workflows, or docs to "fix" findings** while
  acting as `/code-review`. Hand remediations to the Coder.
- Do not push.

## `/repro` — reproduce before declaring a cause

Also: `reproduce`, `write a repro`.

- Recreate the failure with exact commands, inputs, and exit codes.
- Write the repro to `.cursor/reviews/repro-<topic>.md`.
- Do not claim a root cause until the repro ran (or the environment cannot
  run it — then say so).

## Other `/` skills

Orchestrator and subagents may use any installed slash skill when it matches
the phase: `/buildkite-*`, `/sonar-*`, `/review-security`, `/browser-automation`,
`/share-video`, `/docs-canvas`, `/loop`, `/babysit`, `/secure-dependency-health-check`,
and the same names without the slash.
