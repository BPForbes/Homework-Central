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

Before adding or modifying source comments, XML documentation, Markdown files, naming,
or function structure, read and follow the Comment Documentation Guide at
[`docs/COMMENT_DOCUMENTATION_GUIDE.md`](./docs/COMMENT_DOCUMENTATION_GUIDE.md).

Inspect the implementation, related infrastructure, tests, configuration, and the
current branch diff against the integration base. Improve structure, names, and
explicit type declarations before adding comments.

Hard rules:

- Do not use C# `var`; use explicit local and iteration types. This also covers
  pattern positions (`is var x`, `case var x`) — match the real type instead.
  The one unavoidable exception is an anonymous type assigned to a local, which
  has no nameable type; prefer keeping anonymous types inline (as every current
  call site does) so the exception does not arise.
- Do not write the word `var` in C# **prose** either — comments, XML docs and
  string literals included. Write "implicitly typed local". The gate matches the
  bare word, which is what lets it be simple and complete (see below); the word
  occurs nowhere in the current C# sources, so this costs nothing today.
- Do not use C# `dynamic` for locals, fields, parameters, return types or type
  arguments. `var` is statically typed and merely inferred; `dynamic` defers
  binding to runtime, so it is the one construct genuinely less typed than
  `var`. Unlike `var`, "dynamic" **is** allowed in prose — the risk engine's
  "dynamic threshold" is a real domain term — so the gate skips comment lines
  for it.
- Do not use TypeScript/JavaScript `var` either; use `const`, or `let` when the
  binding is reassigned. TypeScript does **not** additionally require explicit
  annotations on inferred locals or return types, and the C# rule should not be
  read that way: `strict` in `frontend/tsconfig.app.json` and
  `tsconfig.node.json` already rejects implicit `any` (`TS7006`) and
  `@typescript-eslint/no-explicit-any` rejects the explicit form, so inference
  there is already fully checked. Requiring annotations would add churn without
  adding type safety.
- These rules are enforced, not advisory. Three gates split the work by what
  each can actually parse, which matters more than it sounds: two review rounds
  spent trading regex false positives against regex bypasses one line at a time
  before the work was divided this way.
  - **Roslyn owns every C# declaration.** `csharp_style_var_*` is `error` in
    `.editorconfig` and `EnforceCodeStyleInBuild` is on in
    `Directory.Build.props`, so an implicitly typed local fails `dotnet build`
    and CI as `IDE0008`. CI compiles all four `csproj`, which between them
    cover every tracked `.cs` file, so this is complete rather than partial.
    (`IDE0007` is the inverse rule and cannot fire while those settings are
    `false`.)
  - **eslint owns web files it can parse.** `no-var` and `prefer-const` fail
    `npm run lint` for `.ts`, `.tsx`, `.mts`, `.cts`, `.js`, `.cjs`, `.mjs`,
    `.jsx`, and — through `eslint-plugin-html` — inline `<script>` in `.html`,
    `.htm` and `.xhtml`. The HTML processor hands eslint the real script text,
    so it is parsed, not pattern-matched. That distinction is the whole reason
    the plugin is a dependency: a grep over HTML cannot tell a `var` in code
    from one inside a comment or a string, and every filter added to teach it
    the difference became a way to hide a `var` from it.
    Two web surfaces are **not** covered, both currently empty: script inside
    `.svg`, and an inline event attribute such as `onclick="var x=1"`, which the
    HTML processor does not extract. Adding either means adding a gate.
  - **`npm run lint:ci` re-runs eslint with `--no-inline-config`.** A blanket
    `/* eslint-disable */` or a bare `// eslint-disable-next-line` names no rule,
    so it silences `no-var` while a scan looking for the rule name beside the
    directive sees nothing. `--no-inline-config` ignores every directive, which
    is why it is a separate script: the plain `npm run lint` keeps the one
    legitimate warn-level `react-hooks/exhaustive-deps` directive working.
  - **`scripts/check-no-var-config.sh` asserts the analyzers are on at all.**
    The three gates above all assume that, and nothing checked it. Two verified
    bypasses were one word deep: `'no-var': 'error'` to `'off'` in
    `frontend/eslint.config.js`, and `<!-- vendored --><NoWarn>$(NoWarn);IDE0008
    </NoWarn>` in the root `Directory.Build.props` — that second one compiled a
    real `var z = 1` with "Build succeeded" while every gate stayed green.
    This script does not grep. It asks MSBuild to evaluate
    `EnforceCodeStyleInBuild` and `NoWarn` per project, parses the root
    `.editorconfig` severities (a bare `= false` falls back to *suggestion*,
    which cannot fail a build), and asks eslint to resolve the config for one
    file per config block, asserting severity `error`. An evaluated property and
    a resolved rule severity cannot be spoofed by a comment, a string, odd
    whitespace, or an unfamiliar extension, because the tool that will act on
    the config is the one reporting it. It runs with `STRICT=1` in CI, so a
    missing toolchain fails instead of skipping.
  - **`scripts/check-no-var.sh` owns only what none of the above can see**: the
    word `var` in a C# pattern position, `dynamic`, and *per-file* suppressions
    (`#pragma warning disable`, `SuppressMessage`) — which no evaluated property
    can show, because they are scoped to a file rather than to the build. It
    also rejects any non-root `.editorconfig` and any non-root
    `Directory.Build.props`/`.targets`: MSBuild takes the *nearest* such file and
    does not merge, so a nested copy that merely **omits**
    `EnforceCodeStyleInBuild` silently disables `IDE0008` for that subtree with
    no suppression syntax to grep for. It pins `LC_ALL=C`, because grep word
    boundaries are locale-sensitive and a gate whose verdict depends on the
    machine is not a gate. It never discards grep's stderr: a single file named
    `-dash.cs` made grep parse a path as flags, abort, and leave every file
    unscanned while the gate printed "passed", so the script now separates "no
    matches" (stdout empty) from "could not run" (stderr non-empty, exit 2).
  Everything build-wide moved to the config probe on purpose. The text scan for
  it produced a false positive, a fix, and then a new bypass in three
  consecutive review rounds; the comment filter that caused the last one is gone
  rather than given a fourth branch. What remains here is safe to scan blind:
  C# requires `#pragma` to be the first token on its line, so the mid-line
  `/* */` trick is a compile error, and the words "pragma warning" and
  "SuppressMessage" appear in zero tracked `.cs` files.
  The gates are still not claimed to be complete — a `grep` cannot lex C#, and a
  `dynamic` after a `/* */` that closes mid-line will slip past the one
  remaining comment filter. Reviewers treat any `var` as a blocking finding,
  must read `var`-shaped lines themselves rather than trusting a green CI, and
  must not mark a change Satisfied while one remains.
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
  `.codeql-db-rust/`, `.cursor/thoughts/`).
- Do not commit local CodeQL SARIF dumps (`codeql-*.sarif`).
- Reviewer, Security and QA **process output** never lands on the committed
  timeline: review threads, Push JSON, triage and repro notes, probe files,
  CodeQL databases and SARIF dumps. Product, pipeline, infra, test code and
  durable `docs/` updates *do* land, as reviewer-approved keep-commits,
  whichever role drafted them — the rule is about the class of output, not who
  typed it.
- Probe in a throwaway clone (`git clone --no-hardlinks . /tmp/probe`) where
  you can; the shared worktree stays untouched and it is the only way to test a
  filename the convention forbids. When a probe must sit in this worktree, give
  it a reserved lower-case name — a `_scratch/` directory or a `.scratch` infix
  — and delete it before reporting. Undo a probe that *edited* a tracked file
  with `git checkout -- <exact path>`, never `git checkout -- .`,
  `git restore :/` or `git stash`. Write reserved names lower-case:
  `.gitignore` cannot case-fold portably, so `_Scratch/` is silently ignored on
  macOS and tracked on Linux.
- `scripts/check-clean-timeline.sh` is the CI backstop, and it is name-based,
  not intent-based. It rejects a **tracked** file matching a reserved scratch
  name (any casing), a thought file other than `.gitkeep`, a `.cursor/reviews/`
  write-up, a CodeQL database, a `.sarif` dump, `.code-review-graph/`,
  `.codegraph/`, and any non-root `.gitignore` — a nested one can re-include
  the reserved names for its whole subtree. It does **not** detect a probe that
  simply used an ordinary filename, so the naming convention protects only
  reviewers who follow it; the review bar, not the grep, is what catches the
  rest. Its `--history <base>` form additionally scans every commit in a range,
  which the tip check and `git diff <base>...HEAD` both miss: a path added in
  one commit and deleted in a later one has a net delta of zero yet its blob
  ships to every clone. That walk disables rename detection (a `git mv` to a
  reserved name is reported as `R`, not `A`, and slipped past a
  `--diff-filter=A` walk entirely) and passes `-m` (without it `git log
  --name-only` prints nothing at all for a merge commit). It refuses to run in
  a shallow clone rather than under-report, and treats a failing `git log` as
  "cannot verify" with exit 2 rather than as "nothing found" — reporting a
  broken repository as a pass is how a backstop becomes a rubber stamp. CI
  passes it the **merge base**, not `pull_request.base.sha`, which is the base
  branch tip at PR-open time and drags in unrelated commits once the base
  moves.
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
- **Related follow-ups and side sprints:** stay on the current branch. Do not
  cut a new `feature/*` (including `feature/*-3665`) for each increment of the
  same workstream.
- **DevOps multi-agent final publish:** after that skill’s Security Clear and
  QA PASS, compress the skill workstream and push once. Keep every commit
  that contains reviewer-approved Coder product, pipeline, infra, or test
  code. Fold process-status, superseded drafts, and skill-loop-only commits
  so the tip tree matches the approved working tree.
  `git reset --soft <integration-base>` then one `git commit` is allowed
  only when there are no keep-commits. Then `git push --force-with-lease`
  when the remote still has the pre-rewrite history. That rewrite is part
  of the skill, not an extra Client ask.

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
[ ] `scripts/check-clean-timeline.sh --history <integration-base>` passes,
    **or** every path it reports is recorded for the Orchestrator to strip
    at One-push step 3a. The range scan, not the tip check: a net diff
    cannot see a path added in one commit and deleted in a later one, and
    that is exactly how a review write-up reached this branch's history.
    A finding inside a keep-commit is not a Coder send-back — the commit
    is kept and the path is stripped from the range during compression,
    so record it and let the gate pass on that basis. The Orchestrator
    re-runs the same scan after step 3a and must get a clean result
    before pushing
[ ] `git status --short` is clean of files you created; anything else is
    listed by path and left in place
[ ] `git diff <integration-base>...HEAD --name-only` contains no path
    outside backend/, frontend/, rust/, scripts/, tools/, llm-service/,
    docs/, deploy/, .github/, .vscode/, .cursor/ and the tracked root
    config files (this is every tracked top-level path; regenerate with
    `git ls-files | awk -F/ 'NF>1{print $1}' | sort -u` if that changes)

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
