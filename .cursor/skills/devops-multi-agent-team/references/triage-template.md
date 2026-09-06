# QA triage item

Copy to `.cursor/thoughts/non-finalized/triage-<id>.md`. Gitignored.
Active item → research → coder → reviewer → QA. [thoughts-layout.md](thoughts-layout.md).

```markdown
# Triage: <id>

**State:** active | closed
**Opened by:** QA
**Discovered during:** …
**Branch:** …
**Review thread:** review-<topic>.md

## What went wrong / Expected / Actual
- …

## Environment
- VM / Commands / Exit code

## Active loop
- [ ] Research → Coder + Push JSON → Reviewers → Security → QA VM re-check

## Q&A
| Id | From | To | Ask | Answer | Status |

## Handoff
- From: QA / To: Coder / Pass-along / Sent back because / Ask
```

Q&A-only bounce: Push JSON `"files": {}` OK.
