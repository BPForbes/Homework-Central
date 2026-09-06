# CodeQL, Validation, and Publish Policy

Repository: .NET/C#, TypeScript/JavaScript, Rust. GitHub Actions runs CodeQL
for all three. Agents treat CodeQL as a required security gate before publish.

## Core rule

**Only QA may give the OK to push.** Review Satisfied, Security Clear,
compilation, tests, and developer CodeQL do **not** authorize a push.

Coders / primary developers must run applicable CodeQL on their changes before
handoff to Reviewers. QA re-checks CodeQL and owns the publish verdict.

**CodeQL satisfied** means: database created; analysis completed; SARIF
inspected; no unresolved finding introduced or materially affected by the
change; no query/path/rule disabled merely to pass.

If CodeQL cannot run when required: do not claim pass; do not auto-publish.
Report what blocked execution.

Fast validation (build, test, lint, format, `cargo check`) does **not**
substitute for required CodeQL.

## Development validation (fast)

**.NET:** `dotnet restore`, `dotnet format --verify-no-changes`,
`dotnet build --warnaserror`, `dotnet test` (use repo solution if present).

**TS/JS:** repo package manager + `lint`, `typecheck`, `test` scripts only.

**Rust:** `cargo fmt --check`, `cargo clippy -- -D warnings`,
`cargo test` (repo-defined).

Do not invent scripts the repo lacks.

## When to run full CodeQL

After logical security-sensitive edits; after substantial backend/frontend/Rust
changes; before substantial commit when practical; before PR update/merge;
before declaring publish-ready.

During minor edits, prefer fast validation — not full DB rebuild every time.

## CodeQL commands (local)

Use repo workflows and `.github/codeql/` config. Typical pattern:

```bash
# C# — after successful build
codeql database create codeql-csharp --language=csharp \
  --source-root=. --command="dotnet build <solution> --warnaserror"
codeql database analyze codeql-csharp --format=sarif-latest --output=csharp.sarif

# JavaScript/TypeScript
codeql database create codeql-js --language=javascript-typescript --source-root=.
codeql database analyze codeql-js --format=sarif-latest --output=js.sarif

# Rust
codeql database create codeql-rust --language=rust --source-root=.
codeql database analyze codeql-rust --format=sarif-latest --output=rust.sarif
```

Inspect SARIF for **new** or **modified/re-exposed** findings. Report
**existing** unrelated findings without rewriting unrelated code unless in scope.

Do not suppress findings or weaken `.github/codeql/codeql-config.yml` /
`.github/workflows/codeql.yml` to pass.

## Finding handling

For each introduced/affected finding: identify rule, file, line, root cause;
fix underlying issue; re-run build/tests and affected CodeQL; re-inspect SARIF;
repeat until resolved or documented technical exception.

## Security-sensitive changes

CodeQL mandatory before publish for auth, authorization, RBAC, sessions,
tokens, secrets, crypto, HTTP/API input, SQL/ORM, filesystem, uploads,
command execution, serialization, SSRF/XSS-sensitive paths, security config.

## Publish gate (QA)

**Blocked when any applicable:** CodeQL FAIL or NOT RUN when required; DB
creation FAIL; SARIF not reviewed; new unresolved finding; required build/test FAIL.

**Allowed when:** applicable builds/tests PASS; C#/TS/Rust CodeQL PASS when
required; SARIF reviewed; zero new unresolved findings; QA marks PASS.

On new finding: stop — fix and re-run. On execution failure: report blocker;
do not substitute other checks for CodeQL.

Sonar and smoke are additive. CI diagnosis → `devops-ci-engineer.md`. QA owns
the publish verdict.

Full operator detail for this repo may also appear in `AGENTS.md` (pointer only).
