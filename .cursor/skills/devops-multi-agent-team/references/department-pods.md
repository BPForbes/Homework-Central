# Department pods

Letters are **departments / subject matters**, not people. Roles run
**async** (`is_background: true`, `run_in_background: true`). Do not
queue pods linearly. Spawn a pod together; synthesize when it completes.

## Departments

| Dept | Owns | Coder seat |
|------|------|------------|
| **A** | `.cursor/skills/devops-multi-agent-team/**`, `.cursor/agents/devops-*.md` | Coder A |
| **B** | `AGENTS.md`, `CLAUDE.md`, PR description text | Coder B |

Research and review/QA seats align with the department they support.

## Research → Coder

- **Research A done** → hand off to **Coder A**. Research A **joins** Coder A.
- **Research B not done** → **do not** run Coder B.

## Coder → Review

- **Coder B submits** → Reviewer A and Reviewer B may both review it.
- **Coder A submits** → Reviewer A **prioritizes** Coder A (A is Reviewer A’s primary).
- If Coder B is still writing and Coder A has not submitted, Reviewer A reviews other **ready** code.

## Cross-review (Reviewers)

Review other ready diffs until a coder of the **same department** submits.
Each reviewer has a **primary** item. If they cannot review that goal yet,
they review other ready code.

**Mid-review swap:** finish the current **line of a file**, pass notes to
the reviewer whose primary just became ready, then take that primary.

Example: Reviewer A primary = C#; Reviewer C primary = Rust. Both were
on Rust. Coder A submits C# → Reviewer A sends Rust notes to Reviewer C,
then takes the C# review.

## QA pods

Several QA pods may test one item. Same primary / cross pattern as reviewers.

If nothing matches a QA’s primary, they test other ready items.

**Mid-test swap:** finish the current assessment, pass notes, then switch.

Client example (record swaps in the review/QA thread — not silent):

- QA A primary **C#**; QA C primary **Rust**.
- Rust not reviewer-OK yet; C# is → both assess C#.
- Rust then becomes ready → **QA A** sends C# notes to **QA C** and begins
  Rust; **QA C** takes C# (QA C’s priority is C# in this example).

The Orchestrator logs the swap in the thread.

## Send-back (review)

**(a)** One reviewer on their primary → coder rewrites. That reviewer joins
other topics and **receives context + existing feedback** before commenting.

**(b)** Several reviewers in one session → **label which reviewer** left each
finding. On send-back, priority may shift with goals.

## Send-back (QA)

Prioritize the assigned item when available. Pass findings when helping on
another item. When the primary becomes available, finish the current test,
pass notes, then switch.

## Gates (unchanged)

No push while review is open. Security after Satisfied. Coder runs CodeQL on
code changes. **Only QA may give the OK to push.** After PASS, Orchestrator
compresses (keep approved Coder commits) and one push — see
[thoughts-layout.md](thoughts-layout.md).

## Pod spawn (Orchestrator)

| Pod | Roles | Starts when |
|-----|-------|-------------|
| research | Planner, Researcher, Ticket Lead | Immediately |
| implement | Coder | Research brief exists or surface known |
| review | Reviewers | Coder has local diff + Push JSON |
| security | Security | Reviewers Satisfied |
| qa | QA, CI Engineer, Verifier | Security clear |
| docs | Documentation, Communicator | Stable enough to document |

Details: [devops-loop.md](devops-loop.md).
