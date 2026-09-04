# Review: Rust kernels + CodeQL/CI

**Branch:** feat/memory-optimization (#70 → feature/ticket-rooms)
**Status:** Satisfied — ready for security
**Push policy:** No push until Status is Satisfied, Security has cleared, and applicable CodeQL is satisfied.

## Research brief

### Local docs
- `docs/tickets.md` — neural monitors, 86-float structural layout, vector retrieval
- `docs/COMMENT_DOCUMENTATION_GUIDE.md` — Analyzer and CI + CodeQL publish policy
- `AGENTS.md` / `CLAUDE.md` — CodeQL, Validation, and Publish Policy
- `.github/workflows/ci.yml`, `.github/workflows/codeql.yml`, `.github/codeql/codeql-config.yml`
- `backend/HomeworkCentral.Api/Assessment/ChatMonitoringFeatureEncoder.cs`
- `backend/HomeworkCentral.Api/Assessment/VectorDocumentStore.cs`

### Online media (fetched)
| URL | Takeaway |
|-----|----------|
| https://docs.github.com/en/code-security/reference/code-scanning/codeql/build-options-for-compiled-languages | CodeQL Rust supports `build-mode: none` only; needs rustup/cargo and Cargo.toml |
| https://github.com/github/codeql/issues/20534 | Rust analysis ignores `--command`; do not use manual/autobuild |
| https://codeql.github.com/codeql-query-help/rust/ | Suites include `security-and-quality` via `codeql/rust-queries` |
| https://github.blog/changelog/2025-10-14-codeql-scanning-rust-and-c-c-without-builds-is-now-generally-available/ | Rust CodeQL is GA |
| https://github.com/dtolnay/rust-toolchain | SHA-pin `master`; set `toolchain: stable` |
| https://github.com/Swatinem/rust-cache | Cache `~/.cargo` + workspace `target` |

### Recommendations
- Incremental crates, not a C#/TS/Ollama/Postgres rewrite.
- CodeQL rust matrix row: `language: rust`, `build-mode: none`.
- CI: `cargo check` + `cargo test` in `rust/` with dtolnay + rust-cache.
- Do not install rustc in the API Docker image (RAM).
- Keep C# as the live encoder/cosine until a later FFI bind.
- Update every CodeQL policy copy so Rust is a third target.

## Change summary (Coder)
- Files: `rust/` workspace (`hc-feature-encode`, `hc-vector-cosine`), `.github/workflows/ci.yml`, `.github/workflows/codeql.yml`, `.github/codeql/codeql-config.yml`, agent/docs CodeQL policy copies, `docs/tickets.md`, `README.md`, encoder/store comments, C# golden test.
- Intent: ship portable Rust twins of the lexical bins and cosine top-k, plus compile + CodeQL gates. Live assessment still runs in C#.

## Review round 1 (Reviewers)

Reviewed the local uncommitted + untracked surface on `feat/memory-optimization` (workflows, policy copies, C# comments/golden, `rust/` workspace). Did not push. Grounded in the research brief, `docs/tickets.md`, and fetched CodeQL / action docs.

What already meets the bar:
- CodeQL rust matrix is `language: rust`, `build-mode: none`. No `--command`, no autobuild, no rust `manual` row. C# build stays gated on `matrix.build-mode == 'manual'`. Matches https://docs.github.com/en/code-security/reference/code-scanning/codeql/build-options-for-compiled-languages and https://github.com/github/codeql/issues/20534 (`--command` is ignored for Rust).
- Actions are SHA-pinned. `dtolnay/rust-toolchain@d1031067263f94b142dd6c0ce24c5eb9d02d52a0` is a real `master` commit; `Swatinem/rust-cache@6323deb102c322ba6fcbdcafc7e3dddab59af2b6` is the `v2.9.2` commit (https://github.com/dtolnay/rust-toolchain, https://github.com/Swatinem/rust-cache). `toolchain: stable` is set. rust-cache `workspaces: rust -> target` is correct.
- `backend/HomeworkCentral.Api/backend.Dockerfile` does not install `rustc` / cargo.
- Incremental crates only; live scoring stays in C#. No C# `var` introduced. Speakable names in the new Rust (`embed_text`, `left_norm`, `HASH_BIN_COUNT`).
- ASCII lexical bins match: `cargo test --workspace` in `rust/` is 5 + 7 green; C# `ChatMonitoringFeatureEncoderTests` is 8 green, including `payment please`.

### Request changes
- [x] `rust/hc-vector-cosine/src/lib.rs` (`cosine`, lines 21–30) — lockstep mismatch with `VectorDocumentStore.Cosine`. C# does `dot += a[i] * b[i]` (and the same for the two norms): the product is `float` then widened to `double`. Rust converts each lane to `f64` first, then multiplies. That is not the same IEEE product. Spot check: `3.1415927f * 2.7182817f` differs by `~3.14e-7` (`f32` product `8.53973388671875` vs `f64` product `8.539734200979865`). `docs/tickets.md` and the crate docs claim these kernels match. Multiply in `f32` then widen (`dot += f64::from(left[index] * right[index])`, same for the norms). Add a unit case whose `f32` product is not equal to the `f64` product so this cannot regress. Cite: `VectorDocumentStore.cs` lines 103–117; research brief “Correctness of Rust vs C# encoder/cosine”.
- [x] `backend/HomeworkCentral.Api.Tests/Assessment/ChatMonitoringFeatureEncoderTests.cs` — C# only locks `"payment please"`. Rust also hardcodes `"anything"` → bin 15, `"Hello, WORLD!! payment-please"` (7 nonzero bins), and `"a "` × 500 clamp to `4.0`. Those C# paths are untested, so the twins can drift independently. Copy the same goldens into the C# test class (explicit `float` / `IReadOnlyList<float>`, no `var`). There is still no C# cosine test at all (`Cosine` is `private`); expose `internal` + `InternalsVisibleTo` or a tiny test hook and lock empty / identical / orthogonal / zero-norm / overlapping-prefix plus the `f32`-product case above. Review bar: tests for behavioral changes; `docs/tickets.md` “They match `ChatMonitoringFeatureEncoder.EmbedText` and `VectorDocumentStore.Cosine`”.
- [x] `docs/COMMENT_DOCUMENTATION_GUIDE.md` lines 1136–1137 — Analyzer section still says “Do not commit local CodeQL databases (`.codeql-db-csharp/`, `.codeql-db-javascript/`)” and omits `.codeql-db-rust/`. `.gitignore` and the policy copies already list the rust database. Add `.codeql-db-rust/`. Review bar: policy copies all gained Rust.
- [x] `.coderabbit.yaml` path_instructions (approx. lines 64–67) — still says “ESLint, and TypeScript type checking do not substitute for required CodeQL analysis” and never names Clippy / rustfmt / `cargo check`. Every other policy copy (`AGENTS.md`, `CLAUDE.md`, `docs/COMMENT_DOCUMENTATION_GUIDE.md`, QA agent, `codeql-validation-publish-policy.md`) gained that rust clause. Align the sentence with those copies. Review bar: policy copies all gained Rust.

### Questions
- Should a later iteration P/Invoke these crates, or keep them as golden twins only? Reviewer preference: keep them as compile-tested twins until a bind has a measured win; do not put `rustc` in the API image.
- Local `codeql database create --language=rust --build-mode=none` from the repo root: confirm the extractor walks to `rust/Cargo.toml` (github/codeql#18500 aggregates nested workspaces). If a dry run fails to see the crates, document `--source-root rust` in the policy create snippet — do not switch rust to `manual` / `--command`.

### Suggestions (non-blocking)
- Vite `manualChunks` for richtext would cut the 761 KB shared JS chunk without Rust.
- Add `rust/hc-feature-encode` and `rust/hc-vector-cosine` rows to the `docs/tickets.md` implementation-files table so “Find the implementation detail to change” lists the twins next to the C# encoder/store.
- Optional CI `cargo fmt --check` / `clippy` later; research only required `cargo check` + `cargo test`, which the rust job already has.
- `to_lowercase()` is Unicode default mapping, not C# `ToLowerInvariant()` on UTF-16 `char`s. ASCII school-chat text matches; if non-ASCII ever hashes, walk UTF-16 units after invariant lowercasing as the crate comment already claims.
- `top_k_cosine` treats `partial_cmp` `None` (NaN) as `Equal`. C# `OrderByDescending` sends NaN to the end. Fine until embeddings can be dirty.

### Reviewer sign-off
| Reviewer | Verdict | Notes |
|----------|---------|-------|
| reviewer-1 | Satisfied | Round 2 verified: rust cosine multiplies `f32` then widens (`f32_product_then_widen_matches_csharp` green); C# goldens cover anything / punctuation / clamp; `VectorDocumentStoreCosineTests` + `InternalsVisibleTo`; `.codeql-db-rust/` listed; CodeRabbit names Clippy / rustfmt / `cargo check`. `cargo test --workspace` 5+8 green; C# encoder/cosine 17 green. Ready for security. |

## Coder response (round 1)
- Initial implementation landed locally for review.

## Coder response (round 2)
- `hc-vector-cosine::cosine` now multiplies `f32` lanes then widens, matching `VectorDocumentStore.Cosine`. Added an `f32`-product case that diverges from widen-first.
- C# goldens now cover `anything`, punctuation, and the 400-token clamp. `VectorDocumentStore.Cosine` is `internal` with `InternalsVisibleTo` and lockstep tests.
- Analyzer section lists `.codeql-db-rust/`. `.coderabbit.yaml` names Clippy / rustfmt / `cargo check`.
- `docs/tickets.md` points at the rust twins next to the C# encoder/store.
- `cargo test --workspace` and the 17 C# encoder/cosine tests passed.

## Security (after Satisfied)

Preliminary review of the Rust kernels and CI/CodeQL surface on `feat/memory-optimization`. Reviewers have not marked Satisfied yet; this section records the security pass so QA can proceed once they do.

### Snyk / review-security results

- Snyk MCP `snyk_code_scan` / `snyk_sca_scan`: **not executed**. `snyk_auth` timed out twice. SCA then reported `/workspace` and `/workspace/rust` as untrusted. `snyk_trust` was not invoked (tool requires an explicit instruction). Snyk CLI is not installed locally (`snyk --version` unavailable).
- Manual review-security of `rust/hc-feature-encode`, `rust/hc-vector-cosine`, `.github/workflows/ci.yml`, `.github/workflows/codeql.yml`, and `.github/codeql/codeql-config.yml` against `origin/feature/ticket-rooms`.

| Check | Result |
|---|---|
| Third-party action SHA pins | Pass |
| CodeQL not weakened | Pass (Rust row added; suite and C#/JS paths unchanged) |
| No new secrets in workflows | Pass |
| Crates have no unexpected deps | Pass (empty graphs) |
| No network / FFI in crates | Pass |
| No Docker `rustc` | Pass |
| XSS / render WASM deferred | Pass (absent; not required) |

**Action pins (verified against GitHub, 2026-09-04):**

- `dtolnay/rust-toolchain@d1031067263f94b142dd6c0ce24c5eb9d02d52a0` — commit on `master` (`Predefine branches up to 1.120`), author dtolnay, GPG verified. CI and CodeQL both pass `toolchain: stable`.
- `Swatinem/rust-cache@6323deb102c322ba6fcbdcafc7e3dddab59af2b6` — annotated tag `v2.9.2` peels to this commit (tag object `63fed3e2…`, GPG verified). Release notes include `credentials.toml` cleanup and `$CARGO_HOME/bin` restore scanning.
- Existing pins reused: `actions/checkout@34e11487…`, `github/codeql-action/*@e4fba868…` (v4.37.3). Checkout keeps `persist-credentials: false`. CI workflow permissions stay `contents: read`.

**CodeQL:**

- Matrix adds `language: rust` / `build-mode: none` only. That is the supported Rust extraction mode (no `--command`, no autobuild). C# stays `manual`; JavaScript/TypeScript stays `none`.
- `queries: security-and-quality` unchanged. No `disable-default-queries`, `query-filters`, or `paths` allow-list that would drop C# or TypeScript.
- `paths-ignore` adds only `**/target/**` (Cargo build output, already gitignored as `rust/target/`). `rust/hc-*/src` remains in scope.
- CodeQL job installs the same SHA-pinned rust-toolchain before `init` so rustup/cargo exist for the extractor. Permissions still `contents: read`, `actions: read`, `security-events: write`.

**Workflows / secrets:**

- No `secrets.*`, tokens, or new credentials in `ci.yml` or `codeql.yml`. The backend job’s `TEST_DATABASE_URL` postgres/postgres string is pre-existing test service config, not introduced by this change.

**Crates:**

- `cargo metadata --no-deps` and `cargo tree --workspace`: `hc-feature-encode` and `hc-vector-cosine` only; `[dependencies]`, `[dev-dependencies]`, `[build-dependencies]`, `build.rs`, `crate-type`, `cdylib`, and `proc-macro` are absent. `Cargo.lock` lists those two packages.
- `publish = false`, `license = UNLICENSED`. Default `rlib` only.
- Source has no `unsafe`, `std::net`, HTTP clients, WASM, or FFI. Encoder emits an 86-float lexical vector; cosine scores already-fetched `f32` slices. Live scoring remains C# (`ChatMonitoringFeatureEncoder`, `VectorDocumentStore`); no `DllImport` / `NativeLibrary` bind.
- `backend.Dockerfile`, `frontend.Dockerfile`, `docker-compose.yml`, and `scripts/` do not install `rustc` / rustup.

**XSS / render WASM:**

- No `wasm`, `wasm-bindgen`, or `wasm32` targets in the branch. Chat HTML sanitization stays in the existing TypeScript path. Deferral is correct; a renderer crate is out of scope and is not a security gap for this change.

Non-blocking: `toolchain: stable` is a floating channel (matches `rust/rust-toolchain.toml` `profile = "minimal"`). Re-run Snyk after auth/trust if a later bind adds crates.io dependencies or a `cdylib`.

### Verdict: Clear

## QA handoff
- Commands run: `dotnet test` encoder/cosine filters; `cargo test --workspace --all-targets`; local CodeQL CLI 2.26.4 `security-and-quality` for csharp, javascript, rust
- .NET Build: PASS
- .NET Tests: PASS (17 encoder/cosine tests)
- TypeScript Validation: NOT APPLICABLE
- Frontend Tests: NOT APPLICABLE
- Rust Validation: PASS
- Rust Tests: PASS (13)
- C# CodeQL: PASS (72 existing; 0 new on change surface)
- TypeScript CodeQL: PASS (0)
- Rust CodeQL: PASS (0; 2 files extracted)
- New unresolved CodeQL findings: 0
- Publish gate: PASS
- Result: QA approve publish
