# Review thread template

Copy to `.cursor/reviews/<topic>.md` for each change set. Coder and Reviewers
communicate **only** through this file for the review gate.

`.cursor/reviews/*` is gitignored. Do not commit review threads, `/goal`
logs, or `/repro` notes.

```markdown
# Review: <topic>

**Branch:** feature/ticket-rooms (#58)
**Status:** In review | Changes requested | Satisfied — ready for security | Closed
**Push policy:** Only QA may give the OK to push. Coders must run
applicable CodeQL on their own changes. No push, publish, PR
open/update, or merge until Status is Satisfied, Security has cleared,
applicable CodeQL is satisfied, **and QA marks the publish gate PASS**.

## Research brief
<!-- Documentation & Research subagent -->

### Local docs
- …

### Online media (fetched)
| URL | Takeaway |
|-----|----------|
| … | … |

### Recommendations
- …

## Change summary (Coder)
- Files:
- Intent:

## Review round N (Reviewers)

### Request changes
- [ ] `path` — finding (cite research/doc/URL)

### Questions
- …

### Suggestions (non-blocking)
- …

### Reviewer sign-off
| Reviewer | Verdict | Notes |
|----------|---------|-------|
| reviewer-1 | Changes requested / Satisfied | … |

## Coder response (round N)
- Replies + what changed:

## Security (after Satisfied)
- Snyk / review-security results:
- Verdict: Clear / Blocked

## QA handoff
- Commands run:
- .NET Build: PASS / FAIL / NOT RUN / NOT APPLICABLE
- .NET Tests: PASS / FAIL / NOT RUN / NOT APPLICABLE
- TypeScript Validation: PASS / FAIL / NOT RUN / NOT APPLICABLE
- Frontend Tests: PASS / FAIL / NOT RUN / NOT APPLICABLE
- C# CodeQL: PASS / FINDINGS / NOT RUN / NOT APPLICABLE
- TypeScript CodeQL: PASS / FINDINGS / NOT RUN / NOT APPLICABLE
- Rust CodeQL: PASS / FINDINGS / NOT RUN / NOT APPLICABLE
- Rust Validation: PASS / FAIL / NOT RUN / NOT APPLICABLE
- Rust Tests: PASS / FAIL / NOT RUN / NOT APPLICABLE
- New unresolved CodeQL findings: N
- Publish gate: PASS / BLOCKED
- Result:
```
