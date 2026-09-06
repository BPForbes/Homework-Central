# QA triage item

Copy to `.cursor/thoughts/non-finalized/triage-<id>.md`. Same
Handoff + **Q&A** as the review thread. Copy ids into Push JSON
`qa`. `"files"` may be `{}`. Do not `git add`. After QA PASS,
move to `finalized/`. An **active** item restarts
research → coder → reviewer → QA.

```markdown
# Triage: <id>

**State:** active | closed
**Opened by:** QA
**Discovered during:** <command, `/repro`, VM, CodeQL, CI>
**Branch:** <current checkout>
**Review thread:** `.cursor/thoughts/non-finalized/review-<topic>.md`

## What went wrong / Expected / Actual
- …

## Environment
- VM / host; commands; exit code

## Active loop
- [ ] Research brief updated
- [ ] Coder rewrite + Push JSON
- [ ] Reviewers compared JSON to `git diff <integration-base>...HEAD`
- [ ] Security (after Satisfied)
- [ ] QA re-check on the VM

## Q&A
| Id | From | To | Ask | Answer | Status |
|----|------|----|-----|--------|--------|
| t1 | QA | Coder | … | | open |

## Handoff
- From: QA / To: Coder / Pass-along /
  Sent back because: <quality / bug standard, or "n/a">
```
