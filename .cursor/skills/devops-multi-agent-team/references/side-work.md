# Side-work, comments, and CodeRabbit

Coders do not put commits on the shared checkout until **QA PASS**.
The Orchestrator then makes the keep-commit(s) and one push
([thoughts-layout.md](thoughts-layout.md)).

## Side-branch (skill-tracked, not a git branch)

A **side-branch** is a name the skill tracks. It is **not**
`git checkout -b` and must not be pushed.

1. Write `.cursor/thoughts/non-finalized/side-<dept>.md`: `name`
   (`side/<dept>-<topic>`), `clone` (optional `/tmp/side-<dept>-<topic>`),
   `base` (current shared branch), files owned.
2. Optional: `git clone --no-hardlinks . /tmp/side-<dept>-<topic>`
   or a local worktree. Edit there. Stay on the same real branch
   name as the shared checkout.
3. If a tool needs a commit, commit **only** in that clone. Never
   `git push` it. Never commit on the shared checkout.

## Run the change

In the side clone, use a VM, `computerUse`, `dotnet` / `npm` /
`cargo`, and `/repro` as needed. The shared worktree stays free
for other departments. Probe undo:
[thoughts-layout.md](thoughts-layout.md).

## Coder → Reviewer comments

Coders may open review-thread `## Q&A` rows and Push JSON `qa`
(`from`: `Coder`, `to`: `Reviewer`) to ask for clarification
before or during review. Same ids. Open rows block Satisfied.

## CodeRabbit (`cr` CLI)

Use the [CodeRabbit CLI](https://docs.coderabbit.ai/cli) (`cr` is
the alias for `coderabbit`). Auth: `cr auth login` or
`cr auth login --api-key` (headless). `cr doctor` if it fails.

On product, pipeline, infra, or test diffs, **before the first
review**, in the side clone:

```text
cr review --agent --uncommitted --include-untracked --base <integration-base>
```

Write findings to `.cursor/thoughts/non-finalized/cr-<topic>.md`
(id, path, line, summary, `status` open|fixed|wontfix, `why` if
wontfix, `by` Coder|Reviewer). `cr review findings` replays the
last run. If `cr` is missing, install or record **NOT RUN**.

Reviewers re-run or read that file. **Block Satisfied** (and QA
**blocks PASS**) if any finding is `open`, or if CR is **NOT RUN**
on a code change. Send CR + review notes to the Coder (Handoff
`To: Coder`). Coder or Reviewer may set `wontfix` + `why` on a
finding; that is a comment, not a silent drop. Re-run `cr` after
fixes. Skill-only Markdown with no product/pipeline/test hunks:
CR is optional; say NOT APPLICABLE.
