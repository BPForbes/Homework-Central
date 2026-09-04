---
name: code-review
description: Look at the change; do not edit. Write findings to .cursor/reviews/. Primary owner is QA.
---

# ./code-review

Also: `/code-review`, `/review-bugbot`, `review the diff`, `look but don't edit`.

**Look at the change. Do not edit.**

Primary owner: **QA** (`.cursor/agents/devops-quality-engineer.md`). Reviewers may use the same inspect-only bar.

1. Read the diff, tests, logs, and SARIF.
2. Write findings into `.cursor/reviews/<topic>.md` (gitignored). Do not commit that file.
3. **Do not edit** product code, workflows, or docs to "fix" findings while acting as `./code-review`. Hand remediations to the Coder.
4. Do not push.
5. Use any installed `/` skill that fits (`/review-bugbot`, `/review-security`, `/sonar-analyze`).

Catalog: `.cursor/skills/devops-multi-agent-team/references/agent-commands.md`.
