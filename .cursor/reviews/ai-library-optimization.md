# Review: AI library optimization

**Branch:** feature/ticket-rooms (#58)
**Status:** Satisfied — ready for security
**Push policy:** No push until Status is Satisfied and Security has cleared.

## Research brief

### Local docs
- `docs/tickets.md` — LLM cost / container ops notes (updated this change set)
- `AGENTS.md` — branch/PR rules for #58; Comment Documentation Guide
- `llm-service/Dockerfile`, `llm-service/entrypoint.sh`, `docker-compose.yml`, `deploy/k8s/llm/base/deployment.yaml`

### Online media (fetched)
| URL | Takeaway |
|-----|----------|
| https://docs.ollama.com/api/embed | Prefer `POST /api/embed` with `input` → `embeddings[]`; `truncate` defaults true |
| https://github.com/ollama/ollama/blob/cecd265d/docs/openapi.yaml | OpenAPI confirms `truncate` / `embeddings` schema |
| https://github.com/ollama/ollama/issues/9781 (via prior WebSearch) | Official image lacks `curl`; healthcheck via `ollama list` |
| https://github.com/ollama/ollama/releases (prior fetch) | Pin `ollama/ollama:0.32.4` |
| https://github.com/ollama/ollama/issues/14186 | Truncate can still fail on some models/encodings — keep HashEmbed fallback |

### Recommendations
- Keep modern embed path + 404 sticky skip + legacy + HashEmbed ladder.
- Compose/K8s healthchecks must not use `curl`.
- CI pin check should use `grep` (ubuntu runner) not assume `rg`.

## Change summary (Coder)
- Files: `llm-service/*`, `docker-compose.yml`, `LlmClient.cs`, `LlmClientTests.cs`, `deploy/k8s/llm/base/deployment.yaml`, `.github/workflows/ci.yml`, `docs/tickets.md`, plus DevOps skill/agents/reviewer entrypoint.
- Intent: Faster/safer LLM ops (pinned image, cache-aware pulls, `/api/embed`, K8s hardening) and formal pre-QA reviewer gate in `/devops-multi-agent-team`.

## Review round 1 (Reviewers)

### Request changes
- [x] `.github/workflows/ci.yml` — `rg` may be missing on runners; use `grep -E` (cite: ubuntu-latest baseline tools).
- [x] `LlmClientTests.cs` — assert `truncate: true` on modern embed body (cite: docs.ollama.com/api/embed).

### Questions
- Compose still mounts `llmdata:/root/.ollama` while K8s uses `$HOME=/ollama` — intentional for root Compose vs non-root K8s? **Yes — leave as-is.**

### Suggestions (non-blocking)
- Watch Ollama #14186 if ticket text is huge and embed errors spike; HashEmbed already covers hard failure.

### Reviewer sign-off
| Reviewer | Verdict | Notes |
|----------|---------|-------|
| reviewer-ops | Satisfied | Pins, healthchecks, entrypoint fail-fast look good |
| reviewer-api | Satisfied | Embed ladder + tests cover modern/legacy/hash/disabled |

## Coder response (round 1)
- Switched CI pin check to `grep -nE`.
- Added `truncate` assertion in `EmbedAsync_UsesModernEmbedEndpoint`.
- Skill/agents updated with Reviewer entrypoint + research online-media requirement.

## Security (after Satisfied)
- Snyk MCP namespace currently **error/unavailable** in this session — cannot re-run live IaC scan.
- Manual review of `deploy/k8s/llm/base/deployment.yaml`: `runAsNonRoot`, dropped `ALL` caps, `allowPrivilegeEscalation: false`, `seccompProfile: RuntimeDefault`, non-root HOME/PVC path — addresses prior SNYK-CC-K8S mediums.
- Verdict: **Clear to proceed to QA** (re-run Snyk when MCP recovers).

## QA handoff
- Commands: Dockerfile pin check (`OLLAMA_VERSION=0.32.4`); `dotnet test …LlmClientTests` with isolated `OutputPath` under `artifacts/llm-test` (avoids host `HomeworkCentral.Api.exe` file lock).
- Result: **Passed** (LlmClientTests, exit 0).
