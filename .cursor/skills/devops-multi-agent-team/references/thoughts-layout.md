# Thought-process files

Agent working notes are **transient** and **outside Git**. Do not put
research dumps, review threads, `/goal` logs, `/repro` notes, role
goals, or Push JSON in `docs/` or in a commit. **Thoughts are
gitignored.** Committing `non-finalized/` so a concept can “survive
multiple pushes” stores those blobs in every earlier commit. Moving
them to `finalized/` later only deletes the tip.

## Directories

| Path | Git | When |
|------|-----|------|
| `.cursor/thoughts/non-finalized/` | **Gitignored** (keepfile only) | Concept is still open |
| `.cursor/thoughts/finalized/` | **Gitignored** | Concept is done (local archive after QA) |

Keep `.cursor/thoughts/non-finalized/.gitkeep`. Do not `git add` any
other file under `.cursor/thoughts/`. Leftover `.cursor/reviews/` is
obsolete. Push JSON lives in `non-finalized/`
([push-json.md](push-json.md)).

**non-finalized** (local): `review-<topic>.md`, `goal-*.md`,
research briefs, `repro-*.md`, handoffs, `push-*.json`,
`triage-<id>.md` ([triage-template.md](triage-template.md)).

**finalized** (after QA PASS, still local): move matching files.
Do not `git add` the move.

If a note must survive clones, it is **not** a thought. Put shipped
behavior in `docs/` or skill policy in `references/`. Do **not**
commit “Record Satisfied / Security / QA” notes. If `git status` /
`git diff` is empty, there is nothing to commit.

Redact tokens, passwords, JWTs, and `.env` values before they enter
any thought file. Prefer exit codes and one-line failure reasons.

## One push (after PASS)

Keep commits **local**. After Satisfied, Security Clear, applicable
CodeQL, **and QA marks PASS**:

1. Move closed thoughts to `finalized/` (local).
2. Compress the skill workstream. **Keep reviewer-approved Coder
   commits in the rewritten history.**
   - A **keep-commit** is a commit since `<integration-base>` whose
     diff includes product, pipeline, infra, or test code (or durable
     operator docs) that Reviewers marked Satisfied. Replay those
     commits in order, with their original messages.
   - **Fold** the rest: process-status notes, superseded drafts,
     “Record Satisfied / Security / QA” subjects, skill-loop-only
     Markdown. Fold into the fewest leftover commits so the tip tree
     matches the approved working tree (usually one fold commit).
   - `git reset --soft <integration-base>` then one `git commit` is
     allowed **only** when there are no keep-commits (a skill-only
     run). Using it when keep-commits exist erases approved Coder
     history and is forbidden.
   - When keep-commits exist: replay them onto `<integration-base>`
     (cherry-pick or rebase, dropping fold-only commits), then commit
     any remaining tip-tree delta as the fold commit.
3. Run `scripts/check-clean-timeline.sh --history <integration-base>`.
   Use `--history`, not the tip check and not
   `git diff <integration-base>...HEAD --name-only`. A path added
   then deleted has a net delta of zero but its blob still ships.
   Confirm `git status --short` is clean of files you created.

### Step 3a — strip a path from a keep-commit

If step 3 reports a path inside a keep-commit, strip it in a
**throwaway clone** (`git clone --no-hardlinks . /tmp/scrub`). Take a
backup ref first. Use `git filter-repo --force --path <path>
--invert-paths --refs <integration-base>..HEAD` (or deprecated
`filter-branch`). Re-run the step 3 scan. `git diff $old HEAD` must
be empty. Then push with
`--force-with-lease=refs/heads/<branch>:$(git ls-remote origin <branch> | cut -f1)`.
A scrub after the branch has already been pushed does **not**
unpublish forge blobs; treat that content as disclosed.

4. Push once. The Client authorizes this rewrite for this skill’s
   final publish only. Reviewers still compare each Coder rewrite’s
   Push JSON to the real local diff before that rewrite.

## Scratch files (Reviewer, Security, QA)

The rule is the **class of output**, not who typed it. Review
threads, Push JSON, triage/repro notes, probe files, CodeQL
databases and SARIF dumps never land. Product, pipeline, infra,
test code and durable `docs/` updates do land as keep-commits.

Prefer `git clone --no-hardlinks . /tmp/probe`. Reserved gitignored
names (lower-case): `_scratch/` or a `.scratch` infix. Delete a
probe before reporting. Undo a tracked-file edit with
`git checkout -- <exact path>` — never `git checkout -- .`,
`git restore :/`, or `git stash`. Finish `git status --short` clean
**of files you created**; list anything else by path and leave it.

Fixed-name probes (`.editorconfig`, `Directory.Build.props` /
`.targets`, nested `.gitignore`, …) cannot take a reserved name:
delete immediately. `check-no-var.sh` does not see gitignored
probes — check those directly.
