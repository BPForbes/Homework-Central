# CLAUDE.md

Guidance for Claude Code (or any agent) working in this repository.

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

Before adding or modifying source comments, XML documentation, Markdown files, naming,
or function structure, read and follow the Comment Documentation Guide at
[`docs/COMMENT_DOCUMENTATION_GUIDE.md`](./docs/COMMENT_DOCUMENTATION_GUIDE.md).

Inspect the implementation, related infrastructure, tests, configuration, and the
current branch diff against the integration base. Improve structure, names, and
explicit type declarations before adding comments.

Hard rules:

- Do not use C# `var`; use explicit local and iteration types. This covers pattern
  positions (`is var x`, `case var x`) — match the real type instead. The one
  exception is an anonymous type with no nameable type; keep anonymous types inline.
- Do not write the word `var` in C# prose (comments, XML docs, string literals).
  Write "implicitly typed local".
- Do not use C# `dynamic` for locals, fields, parameters, return types, or type
  arguments. "Dynamic" is allowed in prose for domain terms (e.g. risk-engine threshold).
- Do not use TypeScript/JavaScript `var`; use `const`, or `let` when reassigned.
  TypeScript `strict` and `@typescript-eslint/no-explicit-any` already guard inferred types.
- These rules are **pinned** in toolchain config:
  - **C#:** `.editorconfig` (`csharp_style_var_* = false:error`) and
    `Directory.Build.props` (`EnforceCodeStyleInBuild` → `IDE0008` on build).
  - **JS/TS:** `frontend/eslint.config.js` and root `eslint.config.mjs` set
    `no-var` and `prefer-const` to `error`; `npm run lint:ci` uses
    `--no-inline-config` so blanket disables cannot silence them.
  - **Backstop:** `scripts/check-no-var.sh` greps for pattern `var`, `dynamic`,
    suppressions, and nested config overrides CI analyzers miss.
  Reviewers treat any `var` as a blocking finding regardless of a green build.
- Prefer pattern matching over large `if` / `else if` chains for closed-set decisions.
- Prefer **fail-first** control flow: validate and return/throw early; keep the happy path
  unindented at the end of the function.
- Prefer speakable names. Abbreviations that cannot be spoken clearly as words or standard
  domain terms must be renamed (for example prefer `roomId` over `rid`, `eligibleUsers`
  over `eus`). Conventional short forms such as `ct` for `CancellationToken` and small
  loop indices remain acceptable.
- Prefer collection transforms over hand-written loops when clearer: TypeScript/Python-style
  `map` / `filter` / `reduce`, and the C# LINQ equivalents `Select()` / `Where()` /
  `Aggregate()` (also `Sum()` / `ToDictionary()` / `ToHashSet()` where they fit). When a
  `foreach` should run only on a subset, filter with `.Where(...)` first (CodeQL
  `cs/linq/missed-where`). Use an explicit loop for multi-step side effects on an
  already-filtered sequence, search-and-return, or performance-critical inner kernels.
- Prefer `!flag` over `flag == false` and `flag` over `flag == true` (CodeQL
  `cs/simplifiable-boolean-expression`).
- Comments must explain project-specific intent, constraints, trust boundaries, state
  ownership, lifecycle behavior, or non-obvious implementation decisions.
- Comments must not be self-referential and must not mention an AI agent, prompt,
  conversation, authoring process, or temporary branch state.
- Prefer updating an authoritative existing Markdown document over creating a duplicate.
- Functions with high cognitive or cyclomatic complexity, excessive nesting, or a
  structural readability score below the accepted threshold must be split into cohesive,
  precisely named subfunctions unless an approved exception applies.

## Optional local tooling

When CodeGraph / Graphify are installed (see [`SETUP.md`](./SETUP.md)):

- Prefer `codegraph search <term>` over broad directory reads.
- Do not stage generated local directories (`.codegraph/`, `.code-review-graph/`,
  `claude-mem/`, `node_modules/`, `.codeql-db-csharp/`, `.codeql-db-javascript/`,
  `.codeql-db-rust/`, `.cursor/thoughts/`).
- Do not commit local CodeQL SARIF dumps (`codeql-*.sarif`).
- Reviewer, Security, and QA **process output** never lands on the committed timeline
  (review threads, Push JSON, triage/repro notes, probe files, CodeQL databases/SARIF).
  Product, pipeline, infra, test code, and durable `docs/` updates do land as
  reviewer-approved keep-commits.
- Probe in a throwaway clone (`git clone --no-hardlinks . /tmp/probe`) when possible.
  In this worktree use a reserved lower-case name (`_scratch/` or `.scratch` infix) and
  delete before reporting. Undo a probe that edited a tracked file with
  `git checkout -- <exact path>` only — never `git checkout -- .`, `git restore :/`,
  or `git stash`. Use lower-case reserved names; `.gitignore` is not case-portable.
- `scripts/check-clean-timeline.sh` is the CI backstop for reserved scratch names,
  thoughts, review write-ups, CodeQL artifacts, and nested `.gitignore` files. CI passes
  `--history <merge-base>`; see the script header for range-scan semantics.
- Confirm destructive actions (deletes, force-pushes, hard resets) with the user.

## UI and styling work

**Before touching any color, animation, spacing, or component style in `frontend/`, read
[`design.md`](./design.md).** It is the source of truth for the design system: color
tokens, typography, motion/animation rules, and the rationale behind them. Every visual
value in `frontend/src/index.css` should trace back to a token defined there.

Do not hardcode a hex color, shadow, or transition timing in a component or a new CSS rule
— reuse or extend the tokens in `frontend/src/index.css`'s `:root` / `:root[data-theme='dark']`
blocks, and update `design.md` if you add a genuinely new token.

Key implementation entry points:
- `frontend/src/index.css` — all design tokens (light + dark) and every component style.
- `frontend/src/context/ThemeContext.tsx` — light/dark theme state, persisted to
  `localStorage`, toggled via `<ThemeToggle />`.
- `index.html` — inline anti-flash script that applies the persisted/preferred theme
  before first paint.

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
- **Opening or updating a PR while on a non-`main` branch:** first check whether an
  open PR already exists for the current branch (or for the integration branch the
  active work is targeting). If one exists, push commits there and update that PR.
  Do **not** open a new PR unless a human explicitly asks for a separate PR.
- Prefer landing related follow-up work on the existing integration PR (for example
  `feature/ticket-rooms` / PR #58) instead of stacking new `cursor/*` PRs.
- **Related follow-ups and side sprints:** stay on the current branch. Do not cut a
  new `feature/*` for each increment of the same workstream.
- **DevOps multi-agent publish:** after Security Clear and QA PASS, compress to one
  push per the DevOps skill (`thoughts-layout.md` One-push). Keep reviewer-approved
  product/pipeline/infra/test commits; fold process-only commits.

## Security, validation, and publish

This repository uses C#, TypeScript/JavaScript, and Rust. GitHub Actions runs CodeQL
for all three.

**Only QA may give the OK to push.** Anyone who changes code must run applicable
CodeQL on those changes; QA re-checks and marks the publish gate PASS. Compilation,
tests, and linters do not substitute for required CodeQL.

Full validation commands, target selection, finding handling, pre-publish checklist,
and definition of done:
[`.cursor/skills/devops-multi-agent-team/references/codeql-validation-publish-policy.md`](.cursor/skills/devops-multi-agent-team/references/codeql-validation-publish-policy.md).
