---
name: goal
description: Do until you achieve X. Persist the objective and keep looping until it is met.
---

# /goal

Also: `set a goal`, `do until X`, `keep going until X`.

Do until you achieve **X**. Do not stop at a plan or a partial implement.

1. Write the objective to `.cursor/reviews/goal-<topic>.md` (acceptance criteria, non-goals, done-when). That directory is gitignored — do not commit it.
2. If this is a long-running human-invoked goal, also use Cursor `CreateGoal` / `UpdateGoal`.
3. The Orchestrator (`/devops-multi-agent-team`) loops the DevOps cycle until X is actually achieved.
4. Spawn roles with `/create-subagent` **asynchronously**. Subagents accept the same `/` command or plain wording.
5. Mark the local goal file (and `UpdateGoal`) complete only when the criteria are met, or the human stops the goal.

Catalog: `.cursor/skills/devops-multi-agent-team/references/agent-commands.md`.
