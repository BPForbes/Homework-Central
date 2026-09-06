# Review thread template

Copy to `.cursor/thoughts/non-finalized/review-<topic>.md` for each
change set. Coder and Reviewers communicate **only** through this file
for the review gate.

While the concept is open, keep the thread in **non-finalized**
(local; do not commit). After QA PASS, move it to
`.cursor/thoughts/finalized/` (still local). See
[thoughts-layout.md](thoughts-layout.md).

```markdown
# Review: <topic>

**Branch:** <current checkout; do not invent a new name>
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

## Push JSON
- Latest: `.cursor/thoughts/non-finalized/push-<topic>.json` (not committed)
- Coder writes this **before the first review**. Later updates notify
  when a change should close findings.
- Reviewers always compare it to `git diff <integration-base>...HEAD`.

## Change summary (Coder)
- Files:
- Intent:
- Notify: (Handoff + Push JSON path; required on the first handoff)

## Review round N (Reviewers)

### Request changes
- [ ] `path` — finding (cite research/doc/URL; say import/reuse if duplicated)

### Questions
<!-- Also copy into Push JSON `qa` and the ## Q&A table. -->
- …

### Suggestions (non-blocking)
- …

### Reviewer sign-off
| Reviewer | Verdict | Notes |
|----------|---------|-------|
| reviewer-1 | Changes requested / Satisfied | … |

## Q&A (Coder ↔ Reviewer)
| Id | From | To | Ask | Answer | Status |
|----|------|----|-----|--------|--------|
| q1 | Reviewer | Coder | … | | open |

Either side may add a row. Answer in the same row (and in Push JSON
`qa`). Do not invent a third file.

## Coder response (round N)
- Replies + what changed:
- Answers: (q-ids closed in this notify)

## Handoff
- From:
- To:
- Pass-along:
- Sent back because: n/a
- Ask: n/a

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
- Clean timeline (`check-clean-timeline.sh --history <base>`): PASS / FAIL / NOT RUN
- Publish gate: PASS / BLOCKED
- VM review / send-back to Coder:
- Triage items (`triage-<id>.md`):
- Thought files to finalize:
- Result: after PASS, Orchestrator compresses (keeps approved Coder commits) and one push.
```
