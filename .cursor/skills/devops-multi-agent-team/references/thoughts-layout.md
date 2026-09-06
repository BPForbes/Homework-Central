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

### Step 3a — stripping a path from a keep-commit

If the step 3 scan reports a path inside a keep-commit, replaying that
commit verbatim re-introduces the blob and the scan still reports it.
The path has to come out of every commit in the range *before* replaying.

**Do the rewrite in a throwaway clone.** The shared worktree is
routinely not clean at this point, and it is not supposed to be: the
pre-publish gate that produces QA's PASS asks only for a status clean
*of files you created*, so another role's tracked edit can legitimately
still be sitting there. A rewrite hard-resets the working tree and takes
that edit with it. Cloning sidesteps the whole question instead of
asking the Orchestrator to arbitrate someone else's uncommitted work:

```
git clone --no-hardlinks . /tmp/scrub && cd /tmp/scrub
git remote set-url origin <the real remote>
git fetch origin
```

Record the old tip (`old=$(git rev-parse HEAD)`) so the content-equality
check at the end is possible, and take a backup ref **now**, before
either command below — the rewrite is not reversible from the reflog
once the old objects are gone:

```
git branch -f backup/pre-scrub HEAD
```

If you rewrite in the shared worktree anyway, `git status --short` must
be empty first and you must not stash to get there; wait for the owning
role to commit or revert. Neither tool below will reliably stop you.

```
git filter-repo --force --path <path> --invert-paths --refs <integration-base>..HEAD
```

`git filter-repo` is the supported tool
([git-filter-branch warns and points at it](https://git-scm.com/docs/git-filter-branch)).
Two things about it are easy to get wrong:

- `--force` is **required** here. Without it the command aborts with
  "Refusing to destructively overwrite repo history… (expected freshly
  packed repo)", because a working checkout is not a fresh clone.
- `--force` also disables its dirty-worktree guard. Verified: with an
  uncommitted edit present it exits 0, hard-resets, and the edit is
  gone with no prompt. That is why the status check above comes first,
  and why "just add `--force`" is not a safe reflex.

Where `filter-repo` is not installed:

```
git filter-branch -f --index-filter \
  'git rm -r --cached --ignore-unmatch <path>' \
  --prune-empty <integration-base>..HEAD
```

`filter-branch` refuses on a dirty worktree (`Cannot rewrite branches:
You have unstaged changes`), which makes it the more forgiving of the
two, but it is deprecated.

Then re-run the step 3 scan, and confirm the rewrite changed history
only and not content:

```
git diff $old HEAD    # must be empty
git log --diff-filter=A --name-only --format='' HEAD | grep <path>   # no hits
git rev-list --count <integration-base>..HEAD   # unchanged
```

Scope that middle check to `HEAD`, not `--all`. `--all` includes the
stale remote-tracking ref, which still holds the pre-rewrite history, so
it reports hits after a completely successful scrub. The commit count
should be unchanged: `filter-repo` prunes a commit that becomes empty,
so a changed count means the path was the commit's only content and the
commit was not a keep-commit after all.

Then push with a lease against **what the remote actually has**, which
is not `$old`:

```
remote_tip=$(git ls-remote origin <branch> | cut -f1)
git push --force-with-lease=refs/heads/<branch>:$remote_tip origin <branch>
```

`--force-with-lease=<ref>:$old` looks right and is wrong here. `$old` is
the local tip before the rewrite, and under One push the remote never
held it, so the lease names a value the remote never had and git rejects
the push as `stale info` (verified against a bare remote).

The bare `--force-with-lease` is not broken, though — it reads the
remote-tracking ref, and step 1 of this procedure already prescribes a
`git fetch origin`, which refreshes it. Measured against a bare remote:
after that fetch the bare form **succeeds**; only if you skip the fetch
does it fail as stale. So prefer the `ls-remote` form for being explicit
rather than for being the only one that works — it states the expected
value at the point of use instead of depending on when the tracking ref
was last refreshed, which matters most when the push is retried after an
interruption. Either way the lease is real: both refuse if someone else
pushed in the interval.

Once the push succeeds and a fresh `git clone` of the remote passes the
step 3 scan, delete the backup ref and any other local ref that still
reaches the stripped blob (`git branch -D backup/pre-scrub`). Leaving it
keeps the blob alive in the local repo, and a later `git push --all`
would republish it. Check with
`git log --diff-filter=A --name-only --format='' --all | grep <path>`;
the answer has to be empty, not "only reachable from a backup".

### What the scrub does not do

**A scrub after the branch has been pushed does not unpublish anything.**
This is the single most important thing to know before relying on it, and
it was learned the hard way on this branch.

Measured against the real remote after a completely successful scrub and
force-push, with a fresh clone reporting the branch clean:

```
gh api repos/<owner>/<repo>/git/blobs/<blob-sha>   -> 200, 13496 bytes
gh api repos/<owner>/<repo>/commits/<commit-sha>   -> 200
```

The blob and the commit that carried it are still served by the forge, to
anyone, indefinitely. A rewrite makes objects unreachable from any ref; it
does not delete them, and a hosted forge keeps unreachable objects and
answers for them by sha. The shas are not secret either — a force-push
writes `head_ref_force_pushed` events into the pull request's own
timeline, so the pre-rewrite sha is published next to the branch.

So the scrub is a **pre-publication** control:

- Before the first push, it works completely. Run step 3 before *every*
  push, not only before the final one.
- After a push, treat the content as disclosed. Removing it from the
  branch stops it spreading to new clones and keeps it out of the merge,
  which is worth doing, but it is containment, not removal.
- If the content actually matters — a credential, personal data, anything
  with a disclosure obligation — the rewrite is not the remedy. Rotate
  the secret, and ask the forge's support to garbage-collect unreachable
  objects; nothing you can run locally reaches them.

Say which of these applies in the Push JSON, rather than reporting
"scrubbed" and letting the reader assume the strong version.

4. Push once. If the remote still has the pre-rewrite history,
   `git push --force-with-lease=refs/heads/<branch>:$(git ls-remote origin <branch> | cut -f1)`
   (safer than `--force`). The Client authorizes this rewrite for
   this skill’s final publish only.

   "Once" is the intent, not a literal count that survives contact with
   a platform that requires a push per iteration loop. Where the two
   conflict, the platform wins and the rule degrades to: the *published
   history* is compressed once, at the end. Intermediate pushes of
   working commits are expected; they are not licence to publish a
   commit whose history still holds a stripped blob, which is what step
   3 is checked against before every push, not just the last one.

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

One consequence of the reserved names being gitignored: `check-no-var.sh`
enumerates with `git ls-files`, so it does not see a probe living under
one of them. A `var` inside `Probe.scratch.cs` is therefore *unreported*
rather than *clean*, and the same is true of the C# gate's own probe
files. Do not read a green `check-no-var.sh` as evidence about anything
sitting at a reserved name — check it directly, or in a throwaway clone
where it can be tracked.

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
