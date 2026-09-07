---
name: repro
description: Reproduce a failure with exact commands before declaring a cause. Notes stay in .cursor/thoughts/non-finalized/.
---

# /repro

Also: `reproduce`, `write a repro`.

Reproduce the failure before declaring a cause.

1. Recreate it with exact commands, inputs, and exit codes.
2. Write the repro to `.cursor/thoughts/non-finalized/repro-<topic>.md`.
   Redact secrets first ([thoughts-layout.md](../skills/devops-multi-agent-team/references/thoughts-layout.md)).
3. Do not claim a root cause until the repro ran (or the environment cannot run it — then say so).
4. A successful repro or fix still does **not** authorize a push.
   **Only QA may give the OK to push.** Coders who change code must
   still run applicable CodeQL.
5. Spawn helpers with `/create-subagent` asynchronously when parallel repros help.
6. Use any installed `/` skill that fits (`/browser-automation`, `/buildkite-*`).

Catalog: `.cursor/skills/devops-multi-agent-team/references/agent-commands.md`.
