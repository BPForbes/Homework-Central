# Thought-process files

Agent working notes are **transient** and **outside Git**. Do not put
research dumps, review threads, `/goal` logs, `/repro` notes, role
goals, or Push JSON in `docs/` or in a commit.

Committing `non-finalized/` so a concept can survive multiple pushes
stores those blobs in every earlier commit. Moving the files to
`finalized/` later only deletes them from the tip. Every clone still
downloads the history. That is neither machine-local after QA nor a
way to keep the repository small.

## Directories

| Path | Git | When |
|------|-----|------|
| `.cursor/thoughts/non-finalized/` | **Gitignored** (keepfile only) | Concept is still open |
| `.cursor/thoughts/finalized/` | **Gitignored** | Concept is done (local archive after QA) |

Keep `.cursor/thoughts/non-finalized/.gitkeep` so the directory exists
in a fresh clone. Do not `git add` any other file under
`.cursor/thoughts/`.

`.gitignore`:

```
.cursor/thoughts/non-finalized/**
!.cursor/thoughts/non-finalized/.gitkeep
.cursor/thoughts/finalized/**
```

Leftover `.cursor/reviews/` is obsolete. Do not `git add` it.

Coder/Reviewer **Push JSON** lives in `non-finalized/` with the other
thoughts (also ignored). Schema: [push-json.md](push-json.md).

## What goes where

**non-finalized** (while the concept is open, local only):

- Review threads: `review-<topic>.md`
- Orchestrator / role goals: `goal-<topic>.md`, `goal-<role>-<topic>.md`
- Research briefs that are not operator docs: `<topic>-research.md`
- Repro notes: `repro-<topic>.md`
- Handoff / sent-back notes: `handoff-<from>-<to>-<topic>.md`
- Push JSON: `push-<topic>.json`
- QA triage items: `triage-<id>.md` (template:
  [triage-template.md](triage-template.md))

**finalized** (after QA PASS, still local only):

- Move the matching files from `non-finalized/` to `finalized/` so the
  working tree stays small on this machine.
- Do not leave a copy in `docs/` or `non-finalized/`.
- Do not `git add` the move. Git never tracked those files.

## Durable history vs transient thoughts

If a note must survive clones and pushes, it is **not** a thought
file. Put shipped behavior in `docs/` (`tickets.md`, `identity.md`,
`chat.md`, `COMMENT_DOCUMENTATION_GUIDE.md`) or skill policy in
`.cursor/skills/devops-multi-agent-team/references/` (this file,
[push-json.md](push-json.md), [role-identity.md](role-identity.md)).

Do not commit a thought “so the next push can see it.” The next
agent on this machine reads the local `non-finalized/` tree. A new
clone starts empty except `.gitkeep`.

Do **not** commit “Record reviewer Satisfied”, “Record Security
Clear”, or “Record QA PASS” notes. Those subjects are process
status, not a product diff. A history that looks like “Record
reviewer-1 Satisfied…”, “Record Security Review…”, “Record QA…”
was the old workflow writing sign-off into Git. If
`git status` / `git diff` is empty, there is nothing to commit:
Satisfied, Security Clear, QA notes, and Q&A answers stay on the
review thread or `triage-<id>.md` (and Push JSON `qa`). A
Q&A-only bounce may use `"files": {}`. Older branches may still
show those process commits; this skill no longer creates them.
After QA PASS they disappear from the rewritten tip: they are
folded away. Reviewer-approved Coder commits stay.

## Redact anyway

Thoughts are gitignored, but a mistaken `git add -f` would publish
them. Same rule as
[devops-communicator.md](../../../agents/devops-communicator.md):

- Redact tokens, passwords, JWTs, connection strings, and `.env`
  values before they enter any thought file.
- Prefer exit codes, step names, and one-line failure reasons.
- Full CI logs stay local; do not paste them into thoughts.

## QA move rule

When QA marks the publish gate PASS, the Orchestrator moves that
concept’s `non-finalized/` files (including `triage-*.md`) to
`finalized/` on this machine. That is local hygiene. It is not a
git add.

## One push (after PASS)

While this skill is in use, keep commits **local**. Do not push
each Coder rewrite. After Reviewers are Satisfied, Security is
Clear, applicable CodeQL is satisfied, **and QA marks PASS**:

1. Move closed thoughts to `finalized/` (local).
2. Compress the skill workstream on the current branch. **Keep
   reviewer-approved Coder commits in the rewritten history.**
   - A **keep-commit** is a commit since `<integration-base>`
     whose diff includes product, pipeline, infra, or test code
     (or durable operator docs for that change) that Reviewers
     marked Satisfied. Replay those commits in order, with their
     original messages.
   - **Fold** the rest: process-status notes, superseded
     unapproved drafts, “Record Satisfied / Security / QA”
     subjects, and skill-loop-only Markdown. Fold into the
     fewest leftover commits needed so the tip tree matches the
     approved working tree (usually one fold commit).
   - `git reset --soft <integration-base>` then one
     `git commit` (https://git-scm.com/docs/git-reset) is
     allowed **only** when there are no keep-commits (a
     skill-only run). Using it when keep-commits exist erases
     approved Coder history and is forbidden.
   - When keep-commits exist: replay them onto
     `<integration-base>` (cherry-pick or rebase, dropping
     fold-only commits), then commit any remaining tip-tree
     delta as the fold commit.
3. **Verify no non-Coder output is in the range.** Run

   ```
   scripts/check-clean-timeline.sh --history <integration-base>
   ```

   Use the `--history` form, not the tip check and not
   `git diff <integration-base>...HEAD --name-only`. Both of those are
   blind to the case that actually happened here: a path added in one
   commit and deleted in a later one has a net delta of zero, so it
   appears in neither, while its blob still ships to every clone.
   `.cursor/reviews/rust-optimization.md` reached this branch exactly
   that way.

   Also confirm `git status --short` is clean of files you created, so
   no probe is swept into the fold commit.

3a. **If the scan reports a path inside a keep-commit**, replaying that
   commit verbatim would re-introduce the blob and step 3 would still
   report clean. Strip the path from every commit in the range *before*
   replaying:

   ```
   git filter-repo --path <path> --invert-paths --refs <integration-base>..HEAD
   ```

   `git filter-repo` is the supported tool
   ([git-filter-branch warns and points at it](https://git-scm.com/docs/git-filter-branch)).
   Where it is not installed:

   ```
   git filter-branch -f --index-filter \
     'git rm -r --cached --ignore-unmatch <path>' \
     --prune-empty <integration-base>..HEAD
   ```

   Both refuse to run with a dirty worktree (`Cannot rewrite branches:
   You have unstaged changes`), so commit or stash first.

   Then re-run the step 3 scan, and confirm `git diff` between the old
   and new tip is **empty** — the rewrite must change history only, not
   content.

4. Push once. If the remote still has the pre-rewrite history,
   `git push --force-with-lease` (safer than `--force`). The
   Client authorizes this rewrite for this skill’s final
   publish only.

That is the only remote push for the skill run. Reviewers still
compare each Coder rewrite’s Push JSON to the real local
`git diff <integration-base>...HEAD` before that rewrite.

## Scratch files (Reviewer, Security, QA)

The rule is about the **class of output**, not who typed it. Review
threads, Push JSON, triage and repro notes, probe files, CodeQL
databases and SARIF dumps never land. Product, pipeline, infra, test
code and durable `docs/` updates do land as keep-commits, whichever
role drafted them.

**Prefer a throwaway clone**: `git clone --no-hardlinks . /tmp/probe`.
The shared worktree is untouched, other roles' in-flight work is safe,
and it is the only way to test a filename the convention forbids. Use
a reserved name only when the probe must sit in this worktree — for
instance when it needs the existing build artifacts.

Two reserved names are gitignored **anywhere** in the tree:

| Form | Example |
|------|---------|
| `_scratch/` directory | `backend/HomeworkCentral.Api/_scratch/Probe.cs` |
| `.scratch` infix or suffix | `backend/HomeworkCentral.Api/Probe.scratch.cs` |

Write them **lower-case**. `.gitignore` cannot case-fold portably, and
`core.ignorecase` defaults to true on macOS and Windows, so `_Scratch/`
would be silently ignored there and tracked on Linux and CI.
`scripts/check-clean-timeline.sh` matches case-insensitively, so any
casing fails the build rather than behaving differently per platform.

Delete a probe before reporting. A probe that **edited a tracked file**
is undone with `git checkout -- <exact path>` — never
`git checkout -- .`, `git restore :/` or `git stash`, which destroy
other roles' uncommitted work. Finish with `git status --short` clean
**of files you created**; list anything else by path and leave it.

`.gitignore` stops an accidental `git add`;
`scripts/check-clean-timeline.sh` is the CI backstop for a `git add -f`,
and with `--history` it also catches a blob that was added and later
deleted within the branch.

### Fixed-name probes

Some probes cannot take a reserved name because the tool only reads a
fixed filename: `.editorconfig`, `Directory.Build.props`/`.targets`,
`.gitignore`, `global.json`, `eslint.config.js`/`.eslintrc*`,
`.gitattributes`, and anything under `frontend/public/` that must keep
a servable name. Delete these immediately after the probe and say so.

Three are independently rejected so a leftover cannot go unnoticed: a
non-root `.editorconfig` and a non-root `Directory.Build.props`/
`.targets` by `scripts/check-no-var.sh`, and a non-root `.gitignore` by
`scripts/check-clean-timeline.sh`. That last one matters most — a
nested `.gitignore` containing `!*.scratch.*` re-includes both reserved
names for its entire subtree and defeats the first layer outright.
