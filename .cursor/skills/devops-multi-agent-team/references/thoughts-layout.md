# Thought-process files

Agent working notes are **not** durable operator docs. Do not put
research dumps, review threads, `/goal` logs, `/repro` notes, or
role goals in `docs/`.

## Directories

| Path | Git | When |
|------|-----|------|
| `.cursor/thoughts/non-finalized/` | **Commit** (needed across multiple pushes) | Concept is still open |
| `.cursor/thoughts/finalized/` | **Gitignored** | Concept is done (QA signed off on the related change) |

`.cursor/thoughts/finalized/*` replaces the old `.cursor/reviews/*`
gitignore rule. Leftover `.cursor/reviews/` files are obsolete: do
**not** `git add` them. Move any still-useful notes into
`non-finalized/` or `finalized/`, then leave the old directory untracked.

Keep `.cursor/thoughts/non-finalized/.gitkeep` so the open-thoughts
directory still exists after a QA move empties it.

## What goes where

**non-finalized** (while the concept is open):

- Review threads: `review-<topic>.md`
- Orchestrator / role goals: `goal-<topic>.md`, `goal-<role>-<topic>.md`
- Research briefs that are not yet operator docs: `<topic>-research.md`
- Repro notes: `repro-<topic>.md`
- Handoff / sent-back notes: `handoff-<from>-<to>-<topic>.md`

**finalized** (after QA marks the publish gate PASS for that concept):

- Move the matching Markdown from `non-finalized/` to `finalized/`.
- Do not leave a copy in `docs/` or `non-finalized/`.
- The next commit/push must not re-add the file (finalized is gitignored).

## Durable `docs/` vs thoughts

Keep **feature-level operator docs** in `docs/` (`tickets.md`,
`identity.md`, `chat.md`, `COMMENT_DOCUMENTATION_GUIDE.md`). Those
describe shipped behavior.

Thought-process research (for example the former
`docs/dev-postgres-host-port.md`, `docs/nn-training-db-relief-research.md`,
`docs/nn-training-heap-spill-research.md`) belongs in thoughts. After QA
signed off, those files live only under `finalized/` on the machine that
ran the loop.

## QA move rule

When QA marks the publish gate PASS for a change set, the Orchestrator
moves every `non-finalized` Markdown whose concept that push closes into
`finalized/` **before** the authorized push (or in the same QA-approved
revision) so thought-process files do not bloat the repository.
