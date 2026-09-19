# Purge process output (authorized push only)

Research briefs, review threads, Push JSON, triage, goals, side
notes, handoffs, and other thought Markdown are **working
records**. They exist so Reviewers, Security, and QA can do
their jobs. They are not product.

## When they may be deleted

Delete them **only** as the last local step before the
**one authorized push**, and only when **both** are true:

1. Security recorded **Clear** on the change.
2. QA marked the publish gate **PASS**.

Review Satisfied, developer CodeQL, a green build, and the
Orchestrator do **not** authorize this purge. Do not delete
these files mid-review, on a failed QA run, or to tidy a
worktree.

## How

```sh
scripts/purge-process-output.sh --security-clear --qa-pass
```

The script refuses unless both flags are present. Do not pass
a flag for a gate that did not happen. `--dry-run` prints
paths and deletes nothing.

What it removes (keeps `non-finalized/.gitkeep`):

- `.cursor/thoughts/` process files, including `finalized/`
- `.ai/thoughts/` process files, if that tree exists
- `.cursor/reviews/`
- reserved scratch (`_scratch/`, `*.scratch`, `*.scratch.*`)

It does not commit or push. After it succeeds, the Orchestrator
runs `scripts/check-clean-timeline.sh --history <integration-base>`
and then the single push.

## Do not

- Move files to `finalized/` and leave them on disk. That
  directory is also purged; it is not an archive.
- Commit research, handoff, or review Markdown “for history.”
- Run this script to satisfy a tidy instinct before Security
  Clear and QA PASS.
