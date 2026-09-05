---
name: code-review
description: Look at the change; do not edit. Write findings to .cursor/thoughts/non-finalized/. Primary owner is QA.
---

# /code-review

Also: `/review-bugbot`, `review the diff`, `look but don't edit`.

**Look at the change. Do not edit.**

Primary owner: **QA** (`.cursor/agents/devops-quality-engineer.md`). Reviewers may use the same inspect-only bar.

1. Confirm the Coder Push JSON exists (required before the first
   review). Read it as an index, then **always** read the real
   `git diff <integration-base>...HEAD`, tests, logs, and SARIF.
   Compare the JSON `files` and `delta`s to that three-dot name
   list and `--numstat`, not to `git show HEAD` alone. An omitted
   or wrong hunk is a finding. Do not mark the review done from
   the JSON alone.
2. Write findings into `.cursor/thoughts/non-finalized/review-<topic>.md`.
   Line-level feedback may also go in an uncommitted `push-<topic>.json`.
3. **Do not edit** product code, workflows, or docs to "fix" findings while acting as `/code-review`. Hand remediations to the Coder.
4. Do not push. **Only QA may give the OK to push.** Review findings
   and passing tests do not authorize a push.
5. Use any installed `/` skill that fits (`/review-bugbot`, `/review-security`, `/sonar-analyze`).

Catalog: `.cursor/skills/devops-multi-agent-team/references/agent-commands.md`.
