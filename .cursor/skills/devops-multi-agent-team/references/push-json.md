# Push JSON (notification index, not the review)

Coder and Reviewer bounce a **Push JSON** until both are happy.
This is a **companion** to the Markdown review thread and Handoff
block — do not replace those. The JSON is a notification and an
index of claimed hunks (file keys, line ranges, why). It is not
the change.

Reviewers **always** compare the JSON to the real repository
diff against the integration base
(`git diff <integration-base>...HEAD`, plus the commit range
under review). [code-review.md](../../../commands/code-review.md)
requires reading that diff. An omitted or wrong file or hunk is
a finding. It is not “unclear.” Do not mark Satisfied from the
JSON alone.

GitHub review comments use `path` + `start_line` / `line`
([PR review comments](https://docs.github.com/en/rest/pulls/comments)).
Unified diffs use `-`/`+` hunk ranges
([git-diff](https://git-scm.com/docs/git-diff)).
This file is the local, uncommitted index of those spans.

`files` keys and each `delta` index that same three-dot range, not
`git show HEAD` alone. A later increment still lists every path in
the range, including earlier commits on this branch.

## Git

All of `.cursor/thoughts/non-finalized/` except `.gitkeep` is
**gitignored**. Do not `git add` Push JSON or thought Markdown.

## Paths

| File | Who writes | When |
|------|------------|------|
| `push-<topic>.json` | whoever just acted | Latest mock. Overwrite or rewrite in place. |
| `push-<topic>-r<N>-<role>.json` | optional | Keep a round if the bounce needs history. |

All live under `.cursor/thoughts/non-finalized/`.

## First handoff (required)

The Coder writes `push-<topic>.json` **before the first review**,
with a Handoff `To: Reviewer`. A fresh change has no findings to
close: `closes` may be `[]`. Reviewers do not start until that
file exists. The first read is not optional.

## Notify (Coder → Reviewer)

After that first file exists, the Coder updates it on **every
rewrite** before Reviewers look again (including a change that
should close feedback):

1. Writes or updates the Push JSON (hunks + why + file `delta`).
2. Posts a **Handoff** on the review thread (`From: Coder`, `To: Reviewer`)
   with the JSON path and, when closing feedback, the finding ids.
3. Does **not** wait for every reviewer to finish the previous round.

A silent code change without a Push JSON + Handoff is incomplete.

Reviewers who send line-level feedback write their **own** Push JSON
(`from`: `Reviewer`) and a Handoff back to the Coder. Rounds are not
a queue: either side may Push when they have something new.

## Q&A (Coder ↔ Reviewer)

Reuse the review-thread **Questions** section and Handoff **Ask**.
Carry the same ids in the Push JSON `qa` array so a question can
travel with a mock diff. Either side may ask or answer without
waiting for a linear round.

```json
"qa": [
  {
    "id": "q1",
    "from": "Reviewer",
    "to": "Coder",
    "ask": "Can TrainingHeapPressure reuse Sample() instead of a new helper?",
    "answer": "Yes — imported Sample(); deleted the copy.",
    "status": "answered"
  }
]
```

`status` is `open` or `answered` (or `withdrawn`). Copy each row into
the thread `## Q&A` table **or** the triage item `## Q&A` table.
Satisfied requires every `open` question answered or withdrawn. An
open question does **not** block the other side from Pushing hunks.

A Q&A-only bounce (review or triage) may set `"files": {}`. That
is not a missing index. Do not invent a git commit to “record”
the answer.

## Shape (each file is a field)

Valid JSON. File paths are object keys. Hunks are `{ op, lines, why }`.
Each file ends with a `delta` string `+N/-M`.

```json
{
  "topic": "heap-spill",
  "round": 2,
  "from": "Coder",
  "to": "Reviewer",
  "notifies": "Should close hysteresis and weights-only",
  "closes": ["r1-hysteresis", "r2-weights-only"],
  "qa": [],
  "files": {
    "backend/HomeworkCentral.Api/Assessment/TrainingHeapPressure.cs": {
      "hunks": [
        { "op": "-", "lines": "107-118", "why": "old ShouldAttemptSpill" },
        { "op": "+", "lines": "107-125", "why": "wait until below skip-trace" }
      ],
      "delta": "+20/-4"
    },
    "frontend/src/pages/NeuralNet.tsx": {
      "hunks": [
        { "op": "-", "lines": "511-513", "why": "combined lines" }
      ],
      "delta": "+0/-3"
    }
  }
}
```

`op` is `-` (removed / old), `+` (added / new), or `~` (same span,
behavior change). `lines` is a single line or `start-end`.

## Reuse (all roles)

Same rule as [role-identity.md](role-identity.md) (Reuse existing
structures). Canonical comment/doc rule:
[COMMENT_DOCUMENTATION_GUIDE.md](../../../../docs/COMMENT_DOCUMENTATION_GUIDE.md).
