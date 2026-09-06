# DevOps development loop — detailed checklists

Use these checklists with the orchestrator loop. Persist `/goal`,
review threads, and `/repro` notes under
`.cursor/thoughts/non-finalized/` (local; do not commit). After QA
PASS, move closed thoughts to `finalized/` (still local). See
[thoughts-layout.md](thoughts-layout.md),
[role-identity.md](role-identity.md), and
[department-pods.md](department-pods.md). Spawn roles
asynchronously **in pods**. Commands:
[agent-commands.md](agent-commands.md).

## 1. Planner

Deliver a plan that includes: goal and non-goals; environments;
components touched; CI/CD stages and gates; deploy + rollback;
observability; security constraints; testable acceptance criteria;
open questions for the human.

## 2. Researcher / Architect

1. Inventory existing pipelines, compose/k8s, IaC, scripts, and docs.
2. Prefer reuse or incremental extension.
3. If introducing a tool, cite why existing options are insufficient.
4. Produce: target architecture; reuse map; risks and rollback notes.
5. When the brief is done, **join the Coder of the same department**.

## 3. Coder

- Implement only what the plan authorizes. Stay on the current
  non-`main` branch (`AGENTS.md` Git branches).
- Keep secrets out of git. Prefer idempotent, non-interactive scripts.
- Run applicable CodeQL before Reviewers. Developer CodeQL does
  **not** authorize a push.
- Keep changes local until **QA gives the OK**. Write
  `push-<topic>.json` **before the first review**. Ask Researcher
  for a reuse map before duplicating code.

## 3b. Documentation & Research (online media)

- Inventory `docs/` and open thoughts first. Do not dump research
  into `docs/`.
- Fetch online media as needed. Write a brief into the review thread.

## 3c. Reviewers (entrypoint before QA)

- PR-style review. Compare Push JSON to
  `git diff <integration-base>...HEAD` ([push-json.md](push-json.md)).
- Cite research brief, reuse map, `docs/`, and fetched URLs.
  Duplicated code → request an import.
- Iterate until Satisfied. Satisfied does **not** authorize a push.
- Primaries and finish-the-line:
  [department-pods.md](department-pods.md).
- Template: [review-thread-template.md](review-thread-template.md).

## 4. Security (after Satisfied)

- Snyk / secret scan / `/review-security` on the change surface.
- Record verdict in the review thread. Security Clear does **not**
  authorize a push.

## 5. QA

QA owns [codeql-validation-publish-policy.md](codeql-validation-publish-policy.md).
`/code-review`: inspect; **do not edit**. `/repro` when needed.

Minimum set: repo-appropriate .NET / TypeScript / Rust fast
validation; applicable CodeQL + SARIF inspect; workflow / chart /
Terraform lint; secret scan if in CI; smoke; rollback notes;
`scripts/check-clean-timeline.sh --history <integration-base>`;
exact commands and exit codes; Definition of Done summary.

**Only QA may give the OK to push.** Fail → Handoff `To: Coder`
from a **VM** review. Open `triage-<id>.md` when tracked
([triage-template.md](triage-template.md)). After PASS, list
thoughts to finalize, then one push that **keeps reviewer-approved
Coder commits** ([thoughts-layout.md](thoughts-layout.md)).

## 6. Optimization

What dominates wall-clock? Can jobs parallelize? Are non-prod
resources oversized? Any redundant rebuilds? Propose measurable
changes (e.g. “cache key X should cut Y”).

## 7. Observability

Golden signals or RED/USE; log labels for triage; symptom-based
alerts with runbook links; SLI/SLO only when the human wants them.

## 8. Documentation

Deploy / promote, rollback, common failures, ownership. Match
final paths and flags. Keep the research brief current.

## 9. Refactoring

Consistent naming with existing `deploy/` / workflow conventions.
Extract repeated YAML/HCL via existing patterns. No behavior
change unless the plan says so.

## 10. Security (checklist detail)

Privileged containers; unpinned actions or images; secrets in
logs or commits; public endpoints without auth; over-broad CI
permissions (`contents: write`).

## 11. Performance profiling

Before/after: pipeline job durations, image size / pull time,
deploy reconcile time, hot app paths only if in scope.

## Interrupt routing

Human interrupts start a **side sprint from research**.

| Human says | Route to |
|------------|----------|
| Redo / change plan | Planner |
| Switch Compose ↔ K8s, GHA ↔ GitLab | Researcher then Planner then Coder |
| Add environment | Planner → Architect → Coder → QA |
| Tighten secrets | Security → Coder → QA |
| Reduce build time | Performance + Optimization → Coder → QA |
| Explain strategy | Researcher (Orchestrator summarizes) |
| Stop | Orchestrator: halt; leave plan/code consistent; do not push |
| Push / open PR | Blocked until **QA gives the OK** |

```text
[Orchestrator] Status
- Done: …
- In progress: … ([Role])
- Blocked / questions: …
- Next: …
```
