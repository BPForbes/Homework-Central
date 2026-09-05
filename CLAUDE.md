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

- Do not use C# `var`; use explicit local and iteration types.
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
  `foreach` should run only on a subset, filter with `.Where(...)` first — do not write
  `if (!condition) continue;` or wrap the body in `if (condition)` as the filter
  (CodeQL `cs/linq/missed-where`). Use an explicit loop for multi-step side effects on an
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
  `.codeql-db-rust/`, `.cursor/thoughts/finalized/`).
- Do not commit local CodeQL SARIF dumps (`codeql-*.sarif`).
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

## CodeQL, Validation, and Publish Policy

This repository contains .NET/C#, TypeScript/JavaScript, and Rust code.

GitHub Actions performs CodeQL analysis for:

* C# / .NET
* JavaScript / TypeScript
* Rust

Agents must treat CodeQL as a required security gate before publishing code.

### Core Rule

**Never push until CodeQL is satisfied.** Review Satisfied, Security
Clear, compilation, tests, and developer CodeQL do not authorize a push
by themselves.

**Only QA may give the OK to push.** Anyone who changes code (Coder /
primary developers) must run applicable CodeQL on those changes. QA
re-checks CodeQL and is the only role that may mark the publish gate
PASS.

DO NOT PUSH, PUBLISH, OPEN OR UPDATE A PULL REQUEST, MERGE, OR OTHERWISE SUBMIT CODE UNTIL QA MARKS THE PUBLISH GATE PASS.

“CodeQL is satisfied” means:

* the applicable CodeQL database was created successfully;
* CodeQL analysis completed successfully;
* the generated SARIF results were inspected;
* no unresolved CodeQL finding introduced or materially affected by the current change remains;
* no CodeQL rule, query, path, or security check was disabled, suppressed, excluded, or weakened merely to obtain a passing result.

If CodeQL cannot be executed when required, the change must not be automatically published.

Compilation, tests, linters, formatters, Roslyn analyzers, ESLint, TypeScript type checking, Clippy, rustfmt, and cargo check do not substitute for required CodeQL analysis.

---

### Development Validation

During active development, prefer fast validation tools before repeatedly running full CodeQL analysis.

#### .NET / C#

Run the repository-appropriate equivalents of:

```
dotnet restore
dotnet format --verify-no-changes
dotnet build --warnaserror
dotnet test
```

If the repository contains a specific solution or project file, use it explicitly when appropriate:

```
dotnet build ProjectName.sln --warnaserror
dotnet test ProjectName.sln
```

#### TypeScript / JavaScript

Run the repository-defined equivalents of:

```
npm ci
npm run lint
npm run typecheck
npm test
```

If the repository uses another package manager such as pnpm or yarn, use the repository’s existing package manager and scripts instead.

Do not invent scripts that are not defined by the repository.

---

#### Rust

Run the repository-defined equivalents of:

```
cargo check --workspace --all-targets
cargo test --workspace
```

If the repository pins a workspace path, use it (`rust/` in this repository).
Do not invent cargo features, targets, or scripts that are not defined by the repository.

---

### CodeQL Analysis

Run local CodeQL analysis before publishing substantial changes, security-sensitive changes, and any changes that will be evaluated by the repository’s GitHub CodeQL workflow.

The local configuration should match the GitHub Actions CodeQL configuration as closely as possible, including query suites and custom queries.

---

### C# / .NET CodeQL

Create a fresh C# CodeQL database:

```
rm -rf .codeql-db-csharp
codeql database create .codeql-db-csharp \
  --language=csharp \
  --command="dotnet build"
```

If the repository contains a specific solution, prefer building that solution:

```
codeql database create .codeql-db-csharp \
  --language=csharp \
  --command="dotnet build ProjectName.sln"
```

Analyze the database:

```
codeql database analyze .codeql-db-csharp \
  codeql/csharp-queries \
  --format=sarifv2.1.0 \
  --output=codeql-csharp.sarif
```

Inspect:

```
codeql-csharp.sarif
```

---

### TypeScript / JavaScript CodeQL

CodeQL analyzes TypeScript using the javascript language target.

Create a fresh JavaScript/TypeScript database:

```
rm -rf .codeql-db-javascript
codeql database create .codeql-db-javascript \
  --language=javascript
```

Analyze the database:

```
codeql database analyze .codeql-db-javascript \
  codeql/javascript-queries \
  --format=sarifv2.1.0 \
  --output=codeql-javascript.sarif
```

Inspect:

```
codeql-javascript.sarif
```

TypeScript source files are included in the JavaScript CodeQL database.

---

### Rust CodeQL

CodeQL analyzes Rust with language identifier `rust` and build-mode `none`.
A `Cargo.toml` or `rust-project.json` must be present. rustup and cargo must
be installed. Do not use `--command` / `build-mode: manual` / autobuild;
Rust extraction ignores a traced cargo build.

Create a fresh Rust CodeQL database:

```
rm -rf .codeql-db-rust
codeql database create .codeql-db-rust \
  --language=rust \
  --build-mode=none
```

Analyze the database with the same suite GitHub Actions uses (`security-and-quality`):

```
codeql database analyze .codeql-db-rust \
  codeql/rust-queries:codeql-suites/rust-security-and-quality.qls \
  --format=sarifv2.1.0 \
  --output=codeql-rust.sarif
```

Inspect:

```
codeql-rust.sarif
```


---

### GitHub Actions Parity

The local CodeQL analysis should use the same query suite used by GitHub Actions whenever possible.

If the workflow specifies a query suite such as:

```
default
security-extended
security-and-quality
```

or repository-specific custom queries, the agent must use the equivalent configuration locally.

Do not intentionally run a weaker local CodeQL configuration than the configuration that will run after push.

If the exact CI configuration cannot be reproduced locally, report that limitation explicitly.

---

### Which CodeQL Target to Run

#### C#-Only Change

If the change affects only C#/.NET code and cannot affect generated frontend code, shared contracts, or cross-stack behavior:

Run:
- .NET validation
- C# CodeQL

#### TypeScript-Only Change

If the change affects only TypeScript/JavaScript code:

Run:
- TypeScript validation
- JavaScript/TypeScript CodeQL


#### Rust-Only Change

If the change affects only Rust crates and cannot affect generated frontend code, shared contracts, or cross-stack behavior:

Run:
- Rust validation
- Rust CodeQL

#### Cross-Stack Change

If the change affects both sides of the application or an integration boundary, run both:

Run:
- .NET validation
- TypeScript validation
- Rust validation
- C# CodeQL
- JavaScript/TypeScript CodeQL
- Rust CodeQL

Cross-stack changes include, but are not limited to:

* API contracts;
* request or response models;
* DTOs;
* schemas;
* authentication;
* authorization;
* session behavior;
* generated clients;
* serialization formats;
* shared validation rules;
* frontend/backend trust boundaries.

#### Pre-Publish Rule

Because GitHub Actions analyzes C#, TypeScript, and Rust upon push, the preferred final pre-publish validation is:

C# CodeQL
+
JavaScript / TypeScript CodeQL
+
Rust CodeQL

Run all three before publishing whenever the environment supports doing so.

---

### Required Agent Behavior for Findings

For every CodeQL finding introduced or materially affected by the current change:

1. Identify the CodeQL rule.
2. Identify the affected source file and line.
3. Determine the source, sink, data flow, control flow, or behavior responsible for the finding.
4. Fix the underlying issue rather than hiding the result.
5. Re-run the relevant compiler, build, linter, and tests.
6. Re-run the affected CodeQL analysis.
7. Inspect the new SARIF output.
8. Repeat until the introduced finding is resolved or there is a documented technical reason it cannot safely be resolved.

Do not suppress, dismiss, exclude, or disable a CodeQL finding merely to obtain a passing result.

Do not modify .github/workflows, CodeQL query suites, CodeQL paths, or repository security configuration solely to hide a newly introduced finding unless the task explicitly requires changing the security-analysis policy.

---

### Existing Findings

Do not assume every CodeQL result was caused by the current task.

When possible, classify findings as:

NEW
EXISTING
MODIFIED / RE-EXPOSED

#### NEW

A finding caused by the current changes.

The agent is responsible for resolving it before publication.

#### EXISTING

A finding already present before the current task and unrelated to the modified code.

Report it, but do not automatically rewrite unrelated code unless remediation is within the requested task scope.

#### MODIFIED / RE-EXPOSED

A previously existing problem that becomes reachable, analyzable, or materially affected because of the current changes.

Review it as part of the current task and resolve it when the current changes materially contribute to the issue.

---

### Security-Sensitive Changes

CodeQL analysis is mandatory before completion and publication for changes involving:

* authentication;
* authorization;
* role-based access control;
* permission checks;
* trust boundaries;
* session handling;
* JWTs;
* access tokens;
* refresh tokens;
* API keys;
* passwords;
* secrets;
* cryptographic keys;
* cryptography;
* hashing;
* signing;
* certificate handling;
* HTTP request handling;
* API endpoints;
* user-controlled input;
* database queries;
* ORM queries;
* SQL construction;
* file-system access;
* file uploads;
* file downloads;
* path construction;
* archive handling;
* command execution;
* process spawning;
* shell invocation;
* serialization;
* deserialization;
* XML processing;
* URL construction;
* redirects;
* outbound HTTP requests;
* SSRF-sensitive operations;
* DOM manipulation;
* raw HTML rendering;
* XSS-sensitive operations;
* frontend/backend data validation;
* sensitive logging;
* security configuration.

---

### Iterative Development

Do not rebuild full CodeQL databases after every minor edit when faster validation is sufficient.

During implementation, prefer:

C#:
dotnet build
dotnet test
Roslyn analyzers
TypeScript:
type checking
linting
tests
Rust:
cargo check
cargo test

Run full CodeQL:

* after a logical security-sensitive change set;
* after substantial backend or frontend changes;
* after substantial Rust changes;
* before committing a substantial change when practical;
* before opening or updating a pull request;
* before merging;
* before declaring a publish-ready task complete.

---

### CodeQL Failure Handling

If CodeQL reports a newly introduced finding:

STOP.
DO NOT PUSH.
DO NOT PUBLISH.
DO NOT OPEN OR UPDATE THE PR.
DO NOT MERGE.

Fix the issue and rerun CodeQL.

Publication may proceed only after the required findings are resolved.

---

### CodeQL Execution Failure

If CodeQL cannot run because:

* the CodeQL CLI is unavailable;
* a query pack cannot be resolved;
* database creation fails;
* the .NET build cannot be extracted;
* the Rust extractor cannot run (missing rustup/cargo or missing Cargo.toml);
* required dependencies are unavailable;
* the execution environment lacks required tooling;
* analysis terminates unexpectedly;

then:

DO NOT CLAIM THAT CODEQL PASSED.
DO NOT CLAIM THAT THE CHANGE IS CODEQL-CLEAN.
DO NOT AUTOMATICALLY PUBLISH THE CHANGE.

Report exactly what prevented CodeQL from running.

Continue running all other available validation, but do not substitute those checks for CodeQL.

Leave publication blocked unless:

1. CodeQL can subsequently be run successfully; or
2. the user explicitly instructs the agent to proceed despite CodeQL being unavailable.

---

### Publishing Prohibited

The agent must not automatically publish when any applicable state is:

C# CodeQL: FAIL
TypeScript CodeQL: FAIL
Rust CodeQL: FAIL
C# CodeQL: NOT RUN when required
TypeScript CodeQL: NOT RUN when required
Rust CodeQL: NOT RUN when required
CodeQL database creation: FAIL
CodeQL analysis: INCOMPLETE
CodeQL SARIF: NOT REVIEWED
New CodeQL finding: UNRESOLVED
Required build: FAIL
Required tests: FAIL

---

### Publishing Allowed

For a full-stack or repository-wide validation, the desired state is:

.NET Build: PASS
.NET Tests: PASS
TypeScript Validation: PASS
Frontend Tests: PASS
Rust Validation: PASS
Rust Tests: PASS
C# CodeQL: PASS
TypeScript CodeQL: PASS
Rust CodeQL: PASS
CodeQL SARIF Reviewed: YES
New Unresolved CodeQL Findings: 0

For a language-specific change, an unaffected target may be marked:

NOT APPLICABLE

unless repository policy or the GitHub Actions configuration requires all applicable targets (C#, JavaScript/TypeScript, and Rust) before every publication.

---

### Final Pre-Publish Gate

Immediately before pushing, publishing, opening/updating a PR, or merging, verify:

[ ] .NET restore succeeds
[ ] .NET formatting check succeeds
[ ] .NET build succeeds with required warning policy
[ ] .NET tests succeed
[ ] TypeScript dependencies are valid
[ ] TypeScript type checking succeeds
[ ] frontend linting succeeds
[ ] frontend tests succeed
[ ] C# CodeQL database creation succeeds
[ ] C# CodeQL analysis succeeds
[ ] C# SARIF results are reviewed
[ ] JavaScript/TypeScript CodeQL database creation succeeds
[ ] JavaScript/TypeScript CodeQL analysis succeeds
[ ] JavaScript/TypeScript SARIF results are reviewed
[ ] Rust toolchain is valid
[ ] cargo check succeeds
[ ] cargo test succeeds
[ ] Rust CodeQL database creation succeeds
[ ] Rust CodeQL analysis succeeds
[ ] Rust SARIF results are reviewed
[ ] No unresolved CodeQL finding introduced by the current change remains

If any required item is incomplete or failing:

STOP.
DO NOT PUSH.
DO NOT PUBLISH.
DO NOT OPEN A PULL REQUEST.
DO NOT UPDATE A PULL REQUEST.
DO NOT MERGE.

---

### Definition of Done

A code change is not considered publish-ready until all applicable validation has succeeded.

When reporting completion, include a concise validation summary:

.NET Build: PASS / FAIL / NOT RUN / NOT APPLICABLE
.NET Tests: PASS / FAIL / NOT RUN / NOT APPLICABLE
TypeScript Validation: PASS / FAIL / NOT RUN / NOT APPLICABLE
Frontend Tests: PASS / FAIL / NOT RUN / NOT APPLICABLE
Rust Validation: PASS / FAIL / NOT RUN / NOT APPLICABLE
Rust Tests: PASS / FAIL / NOT RUN / NOT APPLICABLE
C# CodeQL: PASS / FINDINGS / NOT RUN / NOT APPLICABLE
TypeScript CodeQL: PASS / FINDINGS / NOT RUN / NOT APPLICABLE
Rust CodeQL: PASS / FINDINGS / NOT RUN / NOT APPLICABLE
New unresolved CodeQL findings: N
Publish gate: PASS / BLOCKED

Never state that code is CodeQL-clean unless the applicable CodeQL analysis was actually executed successfully and its results were reviewed.

Never publish automatically while the publish gate is BLOCKED.
