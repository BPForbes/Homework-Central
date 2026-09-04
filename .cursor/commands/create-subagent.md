---
name: create-subagent
description: Spawn a DevOps role asynchronously from .cursor/agents/. Do not poll background subagents.
---

# ./create-subagent

Also: `/create-subagent`, `create a subagent`, `spawn`, Cursor `Task`.

Spawn specialized roles. They run **asynchronously**.

1. Use Cursor `Task` with the matching prompt in `.cursor/agents/devops-*.md`.
2. Default `run_in_background: true` unless the next step is blocked on that one result and there is no other work.
3. Do not poll a background subagent. Continue other work or end the turn; the completion notification is enough.
4. The Orchestrator synthesizes. Subagents do not push or open PRs.
5. Subagents accept any `./` / `/` command or the same words (`./goal`, `./code-review`, `./repro`, `/buildkite-*`, `/sonar-*`, and so on).
6. Working Markdown goes in `.cursor/reviews/` (gitignored). Do not commit it.

| Role | Agent file |
|------|------------|
| Researcher | `devops-researcher.md` |
| Reviewer | `devops-reviewer.md` |
| CI Engineer | `devops-ci-engineer.md` |
| QA | `devops-quality-engineer.md` |
| Security | `devops-security-engineer.md` |
| Ticket Lead | `devops-ticket-lead.md` |
| Verifier | `devops-verifier.md` |
| Integrator | `devops-integrator.md` |
| Communicator | `devops-communicator.md` |

Catalog: `.cursor/skills/devops-multi-agent-team/references/agent-commands.md`.
