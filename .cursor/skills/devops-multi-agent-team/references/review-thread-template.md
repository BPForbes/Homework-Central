# Review thread template

Copy to `.cursor/thoughts/non-finalized/review-<topic>.md`. Local only —
[thoughts-layout.md](thoughts-layout.md).

```markdown
# Review: <topic>

**Branch:** <current checkout>
**Status:** In review | Changes requested | Satisfied — ready for security | Closed
**Push policy:** [codeql-validation-publish-policy.md](codeql-validation-publish-policy.md)

## Research brief
### Local docs
- …
### Online media
| URL | Takeaway |
|-----|----------|
| … | … |
### Recommendations
- …

## Push JSON
- Latest: `.cursor/thoughts/non-finalized/push-<topic>.json`
- Compare to `git diff <integration-base>...HEAD`

## Change summary (Coder)
- Files / Intent / Notify (Handoff + JSON path)

## Review round N
### Request changes
- [ ] `path` — finding (cite doc/URL; reuse map if duplicate)
### Questions
- …
### Suggestions
- …
### Reviewer sign-off
| Reviewer | Verdict | Notes |
|----------|---------|-------|

## Q&A
| Id | From | To | Ask | Answer | Status |

## Coder response (round N)
- Replies / answers (q-ids)

## Handoff
- From / To / Pass-along / Sent back because / Ask

## Security (after Satisfied)
- Results / Verdict: Clear | Blocked

## QA handoff
- Commands / build-test-CodeQL rows / publish gate / triage ids / finalize list
```
