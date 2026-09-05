# Research: DevOps thoughts layout

**Role:** Researcher
**Status:** Open until QA PASS on this concept

## Local docs
- `.cursor/skills/devops-multi-agent-team/SKILL.md`
- `.cursor/skills/devops-multi-agent-team/references/thoughts-layout.md`
- `.cursor/skills/devops-multi-agent-team/references/role-identity.md`
- Durable `docs/`: `tickets.md`, `identity.md`, `chat.md`, `COMMENT_DOCUMENTATION_GUIDE.md`

## Online media
| URL | Takeaway |
|-----|----------|
| https://cursor.com/docs/subagents.md | Custom subagent YAML supports `is_background` (boolean, default false). When true, the subagent runs without blocking the parent. Background mode is for parallel workstreams. |
| https://cursor.com/docs/agent/overview | Parent Agent continues while follow-ups queue or steer; `/goal` is a long-lived objective. |

## Recommendations
- Commit `.cursor/thoughts/non-finalized/` while a concept is open.
- Gitignore `.cursor/thoughts/finalized/*` after QA PASS.
- Do not write thought dumps into `docs/`.
- Orchestrator spawn uses `run_in_background: true` to match `is_background: true`.
- Leftover `.cursor/reviews/` is obsolete; do not stage it.

## Handoff
- From: Researcher
- To: Reviewer
- Pass-along: Official Cursor field is `is_background` on custom agents; Task spawn still sets `run_in_background: true`.
- Sent back because: n/a
- Ask: n/a
