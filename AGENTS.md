# AGENTS.md

Guidance for Codex (or any agent) working in this repository.

## Project

Homework Central is a self-hosted, school-focused chat/communication app: subject-based
chat rooms, staff rooms, an inbox for mentions/replies, role-based permissions, a captcha
gate for account verification, and an admin "Server Maintenance" panel.

- `backend/` — .NET API (`HomeworkCentral.Api`), EF Core migrations, SignalR chat hubs.
- `frontend/` — React + TypeScript + Vite SPA, plain CSS (no Tailwind/component library).
- `scripts/` — local dev stack helpers (`run-dev.sh` / `run-dev.ps1`, etc.); see [`README.md`](./README.md)
  for application setup and [`SETUP.md`](./SETUP.md) for optional local contributor tooling.
- `docs/` — architecture and engineering standards; start at [`docs/README.md`](./docs/README.md).

## Comments, documentation, naming, and readability

Before adding or modifying comments, XML docs, Markdown, naming, or function structure, read
[`docs/COMMENT_DOCUMENTATION_GUIDE.md`](./docs/COMMENT_DOCUMENTATION_GUIDE.md). Inspect the
implementation, tests, config, and branch diff; improve structure, names, and explicit types
before adding comments.

Hard rules:

- Do not use C# `var`; use explicit local and iteration types, including pattern positions
  (`is var x`, `case var x`) — match the real type. Anonymous types with no nameable type stay
  inline. Do not write the word `var` in C# prose; write "implicitly typed local".
- Do not use C# `dynamic` for locals, fields, parameters, return types, or type arguments.
  "Dynamic" is allowed in prose for domain terms (e.g. risk-engine threshold).
- Do not use TypeScript/JavaScript `var`; use `const`, or `let` when reassigned.
- These rules are **pinned** in toolchain config: **C#** — `.editorconfig`
  (`csharp_style_var_* = false:error`), `Directory.Build.props` (`EnforceCodeStyleInBuild` →
  `IDE0008`); **JS/TS** — `frontend/eslint.config.js`, root `eslint.config.mjs` (`no-var`,
  `prefer-const`: error; `npm run lint:ci` uses `--no-inline-config`); **Backstop** —
  `scripts/check-no-var.sh` greps `var`, `dynamic`, suppressions, and nested overrides.
  Reviewers treat any `var` as a blocking finding regardless of a green build.
- Prefer pattern matching over large `if` / `else if` chains for closed-set decisions.
- Prefer **fail-first** control flow: validate and return/throw early; keep the happy path
  unindented at the end of the function.
- Prefer speakable names; rename abbreviations that cannot be spoken clearly as words or
  standard domain terms. Conventional short forms (`ct` for `CancellationToken`, loop indices)
  remain acceptable.
- Prefer collection transforms over hand-written loops when clearer (`map`/`filter`/`reduce`,
  LINQ `Select()`/`Where()`/`Aggregate()`). Filter with `.Where(...)` before `foreach` on a
  subset (CodeQL `cs/linq/missed-where`). Prefer `!flag` over `flag == false` (CodeQL
  `cs/simplifiable-boolean-expression`). Use explicit loops for multi-step side effects or
  performance-critical kernels.
- Comments explain project-specific intent, constraints, trust boundaries, or non-obvious
  decisions; never mention an AI agent, prompt, conversation, or temporary branch state.
- Functions with high cognitive complexity, excessive nesting, or a readability score below
  threshold must split into cohesive subfunctions unless an approved exception applies.

## Optional local tooling

When CodeGraph / Graphify are installed (see [`SETUP.md`](./SETUP.md)), prefer
`codegraph search <term>` over broad directory reads.

- Do not stage generated dirs (`.codegraph/`, `.code-review-graph/`, `claude-mem/`,
  `node_modules/`, `.codeql-db-*`, `.cursor/thoughts/`) or local CodeQL SARIF (`codeql-*.sarif`).
- Reviewer, Security, and QA **process output** never lands on the committed timeline
  (review threads, Push JSON, triage/repro notes, probe files, CodeQL databases/SARIF).
  Product, pipeline, infra, test code, and durable `docs/` updates do land as
  reviewer-approved keep-commits.
- Probe in a throwaway clone when possible; otherwise use a reserved lower-case name
  (`_scratch/` or `.scratch` infix) and delete before reporting. Undo tracked edits with
  `git checkout -- <exact path>` only — never `git checkout -- .`, `git restore :/`, or
  `git stash`.
- `scripts/check-clean-timeline.sh` is the CI backstop (reserved scratch, thoughts, review
  write-ups, CodeQL artifacts, nested `.gitignore`); CI passes `--history <merge-base>`.
- Confirm destructive actions (deletes, force-pushes, hard resets) with the user.

## UI and styling work

**Before touching any color, animation, spacing, or component style in `frontend/`, read
[`design.md`](./design.md).** Every visual value in `frontend/src/index.css` must trace to a
token there. Do not hardcode hex colors, shadows, or transition timings — extend tokens in
`:root` / `:root[data-theme='dark']` and update `design.md` for genuinely new tokens.

Entry points: `frontend/src/index.css` (tokens + component styles),
`frontend/src/context/ThemeContext.tsx` (theme persisted to `localStorage`, `<ThemeToggle />`),
`index.html` (anti-flash script before first paint).

## Conventions

- No Tailwind, no CSS-in-JS — plain CSS with custom properties, styled by class name.
- FontAwesome (`@fortawesome/*`) for icons.
- Keep component structure changes and pure styling changes separate where possible;
  most visual work in this app can be done at the CSS-token level without touching JSX.
- CI rejects unparameterized EF raw SQL (`FromSqlRaw` / `ExecuteSqlRaw` / `SqlQueryRaw`)
  in `backend/HomeworkCentral.Api`.

## Git branches and pull requests

Minimize branches and PRs. Do not open extra workstreams unless a human asks.

- **On `main` only:** create a feature branch when starting work that needs one.
- **Already on a non-`main` branch:** stay on that branch. Do **not** create a new
  branch unless a human explicitly asks for a separate branch.
- **Opening or updating a PR on a non-`main` branch:** check for an open PR for the current
  branch (or integration target); push commits there. Do **not** open a new PR unless a
  human explicitly asks.
- Prefer landing related follow-up work on the existing integration PR instead of stacking
  new `cursor/*` PRs. Stay on the current branch for side sprints; do not cut a new
  `feature/*` per increment.
- **DevOps multi-agent publish:** after Security Clear and QA PASS, compress to one push
  per the DevOps skill (`thoughts-layout.md` One-push). Keep reviewer-approved
  product/pipeline/infra/test commits; fold process-only commits.

## Security, validation, and publish

This repository uses C#, TypeScript/JavaScript, and Rust. GitHub Actions runs CodeQL
for all three. **Only QA may give the OK to push.** Anyone who changes code must run
applicable CodeQL; QA re-checks and marks the publish gate PASS. Compilation, tests, and
linters do not substitute for required CodeQL.

Full validation commands, target selection, finding handling, pre-publish checklist, and
definition of done:
[`.cursor/skills/devops-multi-agent-team/references/codeql-validation-publish-policy.md`](.cursor/skills/devops-multi-agent-team/references/codeql-validation-publish-policy.md).
