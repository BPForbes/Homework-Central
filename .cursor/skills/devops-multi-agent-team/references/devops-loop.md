# DevOps development loop — detailed checklists

Use these checklists when executing the orchestrator loop. Keep the Markdown plan as the source of truth.

## 1. Planner

Deliver a plan that includes:

- Goal and non-goals
- Environments (dev / staging / prod or repo-specific)
- Components touched (apps, infra modules, workflows)
- CI/CD stages and gates
- Deployment strategy and rollback
- Observability requirements (signals + alerts)
- Security constraints (secrets, IAM, network)
- Acceptance criteria (testable)
- Open questions for the human

## 2. Researcher / Architect

Before proposing new structure:

1. Inventory existing pipelines, compose/k8s manifests, IaC, scripts, and docs.
2. Prefer reuse or incremental extension.
3. If introducing a tool/pattern, briefly cite why existing options are insufficient (and optionally external best practice).
4. Produce:
   - Target architecture ( Mermaid or structured notes )
   - Reuse map (existing → reused / extended / replaced)
   - Risks and rollback notes for the Planner

## 3. Coder

- Implement only what the plan authorizes.
- Keep secrets out of git; use the repo’s secret mechanism.
- Make scripts non-interactive for agents (`--yes`, flags, env vars).
- Prefer idempotent apply/deploy paths and dry-run where available.
- Show file paths and diffs.

## 4. QA

Minimum validation set (adapt to stack):

- Workflow / chart / Terraform lint or validate
- Policy / secret scanning if available in repo CI
- Smoke: health endpoints or `kubectl`/`compose` readiness
- Rollback drill notes (or actual rollback dry-run)
- Record exact commands and exit codes

Fail → feedback list for Coder → retest.

## 5. Optimization

Ask:

- What dominates wall-clock (restore cache, image pull, test suite, apply)?
- Can jobs parallelize safely?
- Are resources oversized for non-prod?
- Any redundant rebuilds or layer invalidation?

Propose measurable changes (e.g. “cache key X should cut Y”).

## 6. Observability

- Golden signals or RED/USE as appropriate
- Log labels/fields needed for incident triage
- Alert: symptom-based, with runbook link when docs exist
- Define SLI/SLO only when the human wants them (do not invent targets)

## 7. Documentation

Update or add:

- Deploy / promote steps
- Rollback
- Common failures and fixes
- Ownership / when to page

Match final paths and flags from the implementation.

## 8. Refactoring

- Consistent naming and layout with existing `deploy/` / workflow conventions
- Extract repeated YAML/HCL via existing patterns (not new frameworks unless planned)
- No behavior change unless the plan says so

## 9. Security

Review for:

- Privileged containers / cluster-admin sprawl
- Unpinned actions or base images (flag; fix if plan allows)
- Secrets in logs, PR output, or committed files
- Public endpoints without auth
- Over-broad CI permissions (`contents: write`, etc.)

## 10. Performance profiling

Capture before/after where possible:

- Pipeline job durations
- Image size / pull time
- Deploy reconcile time
- Hot app paths only if in scope

## Interrupt routing cheat sheet

| Human says | Route to |
|------------|----------|
| Redo / change plan | Planner |
| Switch Compose ↔ K8s, GHA ↔ GitLab | Researcher then Planner then Coder |
| Add environment | Planner → Architect → Coder → QA |
| Tighten secrets | Security → Coder → QA |
| Reduce build time | Performance + Optimization → Coder → QA |
| Explain strategy | Researcher (Orchestrator summarizes) |
| Stop | Orchestrator: halt loop; leave plan/code consistent |

## Progress report template

```text
[Orchestrator] Status
- Done: …
- In progress: … ([Role])
- Blocked / questions: …
- Next: …
```
