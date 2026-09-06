# Agent commands

The Orchestrator and every subagent accept these as `/name` or
plain wording. Invocable copies live in `.cursor/commands/`.

Working notes live under `.cursor/thoughts/non-finalized/`
(**gitignored**). After QA PASS, move them to `finalized/` (local).
See [thoughts-layout.md](thoughts-layout.md) and
[push-json.md](push-json.md). Stay on the current non-`main` branch.
**Only QA may give the OK to push.** After PASS, one compressed
push that keeps reviewer-approved Coder commits.

## `/goal` — do until you achieve X

1. Write `.cursor/thoughts/non-finalized/goal-<topic>.md` (and
   optional `goal-<role>-<topic>.md`).
2. If the human invoked a long-running goal, use Cursor goal tools.
3. Keep looping until X is achieved. Do not stop at a plan.

## `/create-subagent` — spawn roles asynchronously

- Spawn from `.cursor/agents/devops-*.md` with Cursor `Task`.
- Run **asynchronously in pods** (`is_background: true` /
  `run_in_background: true`). Department rules:
  [department-pods.md](department-pods.md).
- Do not poll background subagents. The Orchestrator synthesizes.
  Subagents do not push. Orchestrator may push only after QA PASS.

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

**Primary owner: QA.** Reviewers may use the same inspect-only bar.

- Confirm the Coder Push JSON exists. Always
  `git diff <integration-base>...HEAD`. Write findings to
  `review-<topic>.md`. **Do not edit** product code.
- Probe in a throwaway clone, or a reserved lower-case name
  (`_scratch/`, `.scratch`). Undo tracked edits with
  `git checkout -- <exact path>`. See
  [thoughts-layout.md](thoughts-layout.md).

## `/repro` — reproduce before declaring a cause

Recreate the failure with exact commands and exit codes. Write
`repro-<topic>.md`. Files the repro creates are process output.

## `/triage` — QA tracks a bug or discovery

**Owner: QA.** Copy [triage-template.md](triage-template.md) to
`triage-<id>.md`. State `active`. Handoff `To: Coder`. Restarts
research → coder → reviewer → QA.

## Other `/` skills

`/buildkite-*`, `/sonar-*`, `/review-security`, `/browser-automation`,
`/share-video`, `/docs-canvas`, `/loop`, `/babysit`,
`/secure-dependency-health-check`, and the same names without a slash.
