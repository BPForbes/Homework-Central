# DevOps development loop — checklists

Persist `/goal`, review threads, and `/repro` under
`.cursor/thoughts/non-finalized/` (local). After QA PASS, move to
`finalized/`. See [thoughts-layout.md](thoughts-layout.md),
[role-identity.md](role-identity.md), [department-pods.md](department-pods.md).
Spawn with `/create-subagent` in async pods. Commands:
[agent-commands.md](agent-commands.md). Publish gate:
[codeql-validation-publish-policy.md](codeql-validation-publish-policy.md).

## 1. Planner

Goal, non-goals, environments, components, CI/CD gates, deploy/rollback,
observability, security constraints, acceptance criteria, open questions.

## 2. Researcher / Architect

Inventory pipelines, IaC, scripts, docs. Prefer reuse. Produce architecture
notes, **reuse map**, risks. Fetch online media; cite URLs in the brief.

## 3. Coder

Implement per plan. Non-interactive scripts; secrets out of git. Show diffs.
Run applicable CodeQL before Reviewers (does not authorize push). Local only
until QA PASS. Write `push-<topic>.json` before first review; update on
notify. Ask Researcher before duplicating code.

## 3b. Documentation & Research

Inventory `docs/` and thoughts first. Fetch as needed. Brief → review thread
(local; not `docs/` dumps).

## 3c. Reviewers

PR-style review via thread + Push JSON. Always compare JSON to
`git diff <integration-base>...HEAD`. Cite brief, reuse map, docs, URLs.
Iterate to Satisfied → Security → QA. Template:
[review-thread-template.md](review-thread-template.md). Pod rules:
[department-pods.md](department-pods.md).

## 4. Security

After Satisfied: Snyk / `/review-security`. Record verdict in thread.
Security Clear ≠ push authorization.

## 5. QA

Agent: `devops-quality-engineer.md`. Follow
[codeql-validation-publish-policy.md](codeql-validation-publish-policy.md).
`/code-review` inspect-only; `/repro`; `/triage` when tracked.

Minimum: fast .NET/TS validation; applicable CodeQL + SARIF; lint/validate
workflows or IaC; smoke; `check-clean-timeline.sh --history <base>`;
record commands and DoD summary.

Fail → VM Handoff to Coder; active triage restarts research → coder →
reviewer → QA. After PASS → finalize thoughts; Orchestrator one push
(keep approved Coder commits).

## 6–11. Optimization / Observability / Docs / Refactoring / Security detail / Performance

See skill `SKILL.md` role sections. Match repo conventions; no invented SLOs.

## Interrupt routing

Side sprint from research ([role-identity.md](role-identity.md)). Push only
after QA PASS.

| Human says | Route |
|------------|-------|
| Redo plan | Planner |
| Tool switch | Researcher → Planner → Coder |
| Add env | Planner → Architect → Coder → QA |
| Secrets | Security → Coder → QA |
| Build time | Performance + Optimization → Coder → QA |
| Stop | Orchestrator halt; no push |
| Push / PR | Blocked until QA PASS |

## Progress report

```text
[Orchestrator] Done / In progress / Blocked / Next
```
