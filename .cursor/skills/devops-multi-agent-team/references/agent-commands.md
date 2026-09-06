# Agent commands

Orchestrator and subagents accept `/name` or plain wording. Invocable copies:
`.cursor/commands/`. Shared rules: [role-identity.md](role-identity.md),
[department-pods.md](department-pods.md), [thoughts-layout.md](thoughts-layout.md).

**Only QA may give the OK to push.**

## `/goal`

Write `.cursor/thoughts/non-finalized/goal-<topic>.md` (or `goal-<role>-<topic>.md`).
Loop the DevOps cycle until done-when is met. Mark complete only when criteria
are met or the human stops.

## `/create-subagent`

Spawn from `.cursor/agents/devops-*.md` with Cursor `Task`,
`run_in_background: true`. Launch pods together; do not poll. Orchestrator
synthesizes; subagents do not push.

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

## `/code-review`

**Primary owner: QA.** Reviewers use the same inspect-only bar.

Read Coder Push JSON as index; **always** `git diff <integration-base>...HEAD`,
tests, logs, SARIF. Compare mock and real diff. Write findings to
`review-<topic>.md`. **Do not edit** product code. Hand fixes to Coder.

## `/repro`

Reproduce with exact commands and exit codes. Write
`.cursor/thoughts/non-finalized/repro-<topic>.md`. Probes: throwaway clone or
reserved name per [role-identity.md](role-identity.md).

## `/triage`

**Owner: QA.** Copy [triage-template.md](triage-template.md) to
`triage-<id>.md`. Active item restarts research → coder → reviewer → QA.

## Other `/` skills

Use when the phase matches: `/buildkite-*`, `/sonar-*`, `/review-security`,
`/browser-automation`, `/share-video`, `/docs-canvas`, `/loop`, `/babysit`,
`/secure-dependency-health-check`.
