# Thought-process files

Working notes are **transient** and **outside Git**. Do not commit research
dumps, review threads, goals, Push JSON, or triage items.

Committing `non-finalized/` stores blobs in every earlier commit. Moving to
`finalized/` later only deletes the tip — clones still download history.

## Directories

| Path | Git | When |
|------|-----|------|
| `.cursor/thoughts/non-finalized/` | **Gitignored** (`.gitkeep` only) | Concept open |
| `.cursor/thoughts/finalized/` | **Gitignored** | After QA PASS (local archive) |

Keep `.cursor/thoughts/non-finalized/.gitkeep`. Do not `git add` other
thought files. Obsolete: `.cursor/reviews/`.

Push JSON schema: [push-json.md](push-json.md).

## What goes where

**non-finalized:** `review-<topic>.md`, `goal-<role>-<topic>.md`,
`<topic>-research.md`, `repro-<topic>.md`, `handoff-*.md`, `push-<topic>.json`,
`triage-<id>.md`.

**finalized:** move closed files from `non-finalized/` after QA PASS. Do not
copy to `docs/`. Do not `git add` the move.

Durable history → `docs/` or skill `references/`, not thoughts.

Do not commit “Record Satisfied / Security / QA” notes. Sign-off stays in
thoughts or triage Q&A. Q&A-only bounce may use `"files": {}`.

## Redact

Redact tokens, passwords, JWTs, connection strings, `.env` values before
writing thoughts. Prefer exit codes and one-line failure reasons. Full CI
logs stay local.

## One push (after QA PASS)

Keep commits **local** until PASS. Then:

1. Move closed thoughts to `finalized/` (local).
2. **Compress** on the current branch. **Keep** reviewer-approved Coder
   commits (product/pipeline/infra/test or durable docs). **Fold** process
   commits and skill-loop Markdown into the fewest fold commits so the tip
   matches the approved tree.
   - No keep-commits: `git reset --soft <integration-base>` + one commit OK.
   - With keep-commits: replay them onto `<integration-base>`, then fold
     the rest — never soft-reset away approved Coder history.
3. Run `scripts/check-clean-timeline.sh --history <integration-base>` (not
   tip-only; not `git diff` alone — deleted mid-branch paths still ship).
4. `git status --short` clean of files you created.
5. One push (`--force-with-lease` if needed). **Only QA authorized PASS.**

### Scrub a path from a keep-commit

If step 3 reports a path inside a keep-commit, strip it from every commit in
the range **before** replay. Prefer a throwaway clone (`git clone --no-hardlinks
. /tmp/scrub`) — rewrites hard-reset the shared worktree.

Use `git filter-repo --force --path <path> --invert-paths --refs <base>..HEAD`
(filter-repo docs) or deprecated `git filter-branch` if filter-repo missing.
Backup ref first. Verify `git diff $old HEAD` empty and path absent from
`HEAD` history. Push with lease against remote tip.

## QA move rule

When QA marks PASS, Orchestrator moves that concept’s `non-finalized/` files
(including `triage-*.md`) to `finalized/`. Local hygiene only — not a git add.
