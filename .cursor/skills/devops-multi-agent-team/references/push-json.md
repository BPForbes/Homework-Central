# Push JSON (notification index)

Coder and Reviewer bounce `push-<topic>.json` until both are happy. Companion
to the review thread and Handoff — not a substitute. Gitignored under
`.cursor/thoughts/non-finalized/`.

Reviewers **always** compare JSON to `git diff <integration-base>...HEAD`.
Omitted or wrong hunks are findings. Do not mark Satisfied from JSON alone.

## First handoff

Coder writes `push-<topic>.json` **before first review** with Handoff
`To: Reviewer`. Fresh change: `closes` may be `[]`.

## Notify

Coder updates JSON on every rewrite + Handoff (finding ids when closing).
Reviewers may write their own JSON (`from`: `Reviewer`) + Handoff to Coder.
Either side may Push when ready — not a linear queue.

## Q&A

Mirror review thread `## Q&A` and Push JSON `qa` (same ids):

```json
"qa": [{ "id": "q1", "from": "Reviewer", "to": "Coder",
  "ask": "…", "answer": "…", "status": "answered" }]
```

Q&A-only bounce: `"files": {}` is valid.

## Shape

```json
{
  "topic": "heap-spill",
  "round": 2,
  "from": "Coder",
  "to": "Reviewer",
  "notifies": "…",
  "closes": ["r1-hysteresis"],
  "qa": [],
  "files": {
    "path/to/File.cs": {
      "hunks": [{ "op": "-", "lines": "107-118", "why": "…" }],
      "delta": "+20/-4"
    }
  }
}
```

`op`: `-` removed, `+` added, `~` same span behavior change. `lines`: single
line or `start-end`.

Reuse rule: [role-identity.md](role-identity.md). Comment style:
[COMMENT_DOCUMENTATION_GUIDE.md](../../../../docs/COMMENT_DOCUMENTATION_GUIDE.md).
