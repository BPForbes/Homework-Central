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
After QA PASS they disappear from the branch tip when the
workstream is squashed to one product commit.

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
2. Compress the skill workstream to **one commit** on the current
   branch: `git reset --soft <integration-base>` then one
   `git commit` (https://git-scm.com/docs/git-reset).
3. Push that commit once. If the remote still has the pre-squash
   history, `git push --force-with-lease` (safer than `--force`).
   The Client authorizes this rewrite for this skill’s final
   publish only.

That is the only remote push for the skill run. Reviewers still
compare each Coder rewrite’s Push JSON to the real local
`git diff <integration-base>...HEAD` before that squash.
