# CodeQL, Validation, and Publish Policy

This repo has .NET/C#, TypeScript/JavaScript, and Rust. GitHub Actions
runs CodeQL for all three. Treat CodeQL as a required gate before
publish.

## Core rule

**Only QA may give the OK to push.** Anyone who changes product,
pipeline, or infra code must run applicable CodeQL. That developer
run does not authorize a push. Review Satisfied, Security Clear,
compilation, tests, and linters do not substitute for CodeQL and do
not authorize a push.

“CodeQL is satisfied” means: the applicable database was created;
analysis completed; SARIF was inspected; no unresolved finding
introduced or materially affected by this change remains; no query,
path, or check was weakened to obtain a pass.

If CodeQL cannot run when required: do not claim it passed, do not
claim the change is CodeQL-clean, and do not automatically publish.

Do not suppress queries or weaken `.github/codeql/codeql-config.yml`
/ `.github/workflows/codeql.yml` to pass. Fix the code.

## Fast validation (before full CodeQL)

Use repo-defined scripts only. Do not invent targets.

**.NET:** `dotnet restore` · `dotnet format --verify-no-changes` ·
`dotnet build --warnaserror` · `dotnet test` (use the repo `.sln`).

**TypeScript:** `npm ci` · `npm run lint` · `npm run typecheck` ·
`npm test` (or the repo’s package manager).

**Rust:** `cargo check --workspace --all-targets` ·
`cargo test --workspace` (workspace path `rust/` here).

## Local CodeQL (match GitHub Actions)

Match the workflow query suite (`default` / `security-extended` /
`security-and-quality` or repo custom queries). Report if local
parity is impossible. Prefer all three languages before publish
when the environment supports it.

**C#**

```
rm -rf .codeql-db-csharp
codeql database create .codeql-db-csharp --language=csharp --command="dotnet build"
codeql database analyze .codeql-db-csharp codeql/csharp-queries \
  --format=sarifv2.1.0 --output=codeql-csharp.sarif
```

**JavaScript / TypeScript** (`--language=javascript` covers `.ts`)

```
rm -rf .codeql-db-javascript
codeql database create .codeql-db-javascript --language=javascript
codeql database analyze .codeql-db-javascript codeql/javascript-queries \
  --format=sarifv2.1.0 --output=codeql-javascript.sarif
```

**Rust** (`--build-mode=none`; do not use `--command` / autobuild)

```
rm -rf .codeql-db-rust
codeql database create .codeql-db-rust --language=rust --build-mode=none
codeql database analyze .codeql-db-rust \
  codeql/rust-queries:codeql-suites/rust-security-and-quality.qls \
  --format=sarifv2.1.0 --output=codeql-rust.sarif
```

Inspect each SARIF. Language-only changes run that language’s
validation + CodeQL. Cross-stack (API contracts, auth, DTOs,
generated clients, trust boundaries) runs all three. Unaffected
targets may be `NOT APPLICABLE` unless CI requires all three.

CodeQL is mandatory for auth, secrets, crypto, HTTP, SQL/ORM,
uploads, command execution, serialization, XSS/SSRF-sensitive
work, and security configuration.

## Findings

For each finding introduced or materially affected: identify rule,
file, and line; fix the underlying issue; re-run build/tests and
CodeQL; inspect the new SARIF. Classify **NEW** (must resolve),
**EXISTING** (report; do not rewrite unrelated code), or
**MODIFIED / RE-EXPOSED** (resolve when this change contributes).

Do not rebuild full databases after every minor edit. Run full
CodeQL after a security-sensitive or substantial change set, and
always before the publish gate.

## Publish

**Prohibited** when any applicable state is FAIL, NOT RUN when
required, analysis INCOMPLETE, SARIF not reviewed, a new finding
unresolved, or required build/tests FAIL.

**Allowed** (full-stack): .NET / TypeScript / Rust validation and
tests PASS; C# / TypeScript / Rust CodeQL PASS; SARIF reviewed;
new unresolved findings = 0.

Also before PASS: `scripts/check-clean-timeline.sh --history
<integration-base>` passes, **or** every reported path is recorded
for the Orchestrator to strip at One-push step 3a (a keep-commit
finding is not a Coder send-back). `git status --short` clean of
files you created. Diff paths stay under `backend/`, `frontend/`,
`rust/`, `scripts/`, `tools/`, `llm-service/`, `docs/`, `deploy/`,
`.github/`, `.vscode/`, `.cursor/`, and tracked root config.

## Definition of done

Report: .NET / TypeScript / Rust validation and tests, plus C# /
TypeScript / Rust CodeQL, each as PASS / FAIL / FINDINGS / NOT RUN
/ NOT APPLICABLE; new unresolved findings N; publish gate PASS /
BLOCKED. Never call the change CodeQL-clean unless analysis ran
and SARIF was reviewed. Never publish while BLOCKED.
