# QA triage item

QA copies this to `.cursor/thoughts/non-finalized/triage-<id>.md`
when a command, VM check, or quality/bug standard fails.
Same Markdown shape as the review thread: Handoff plus **Q&A**.
Either side may add a `## Q&A` row. Copy the same ids into the
Push JSON `qa` array. Do not invent a third channel. A triage
Q&A bounce does **not** need a tree change: `files` may be `{}`.
Do not `git add` this file. After the item closes and QA PASS,
move it to `finalized/` with the other thoughts. See
[thoughts-layout.md](thoughts-layout.md).

An **active** item restarts **research → coder → reviewer → QA**
for that id. Security still runs after Reviewers are Satisfied
on the fix.

```markdown
# Triage: <id>

**State:** active | closed
**Opened by:** QA
**Discovered during:** <command, `/repro`, VM check, CodeQL, CI job>
**Branch:** <current checkout; do not invent a new name>
**Review thread:** `.cursor/thoughts/non-finalized/review-<topic>.md`

## What went wrong
- …

## Expected
- …

## Actual
- …

## Environment
- VM / host:
- Commands:
- Exit code:

## Active loop
- [ ] Research brief updated
- [ ] Coder rewrite + Push JSON
- [ ] Reviewers compared JSON to `git diff <integration-base>...HEAD`
- [ ] Security (after Satisfied)
- [ ] QA re-check on the VM

## Q&A (same exchange as the review thread)
Either side may add a row (QA, Coder, Reviewer, Researcher).
Answer in the same row and in Push JSON `qa` (same id). Status
is `open`, `answered`, or `withdrawn`. This table is the Q&A
even when there is no git diff.

| Id | From | To | Ask | Answer | Status |
|----|------|----|-----|--------|--------|
| t1 | QA | Coder | … | | open |

## Handoff
- From: QA
- To: Coder
- Pass-along:
- Sent back because: <quality / bug standard, or "n/a">
- Ask: n/a
```
