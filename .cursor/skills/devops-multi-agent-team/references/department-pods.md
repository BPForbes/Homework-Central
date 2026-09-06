# Department pods

Letters are **departments / subject matters**, not people. Teams work
**dynamically**: research can run with coding; coding can run with
review. Do not run roles as a linear 1→N queue.

Read this file. Do not paste it into agent prompts.

| Dept | Owns | Coder seat |
|------|------|------------|
| **A** | skill `SKILL.md`, `references/*`, `.cursor/agents/*` | Coder A |
| **B** | `AGENTS.md`, `CLAUDE.md`, PR description text | Coder B |

## Research → Coder

- Research **A** done → hand off to Coder **A**. Research A **joins
  Coder A** (same department, same change surface).
- Research **B** not done → **do not** run Coder B.
- The same rule for every letter: no Coder until that department’s
  research brief exists (or the Orchestrator already knows the surface).

## Reviewer primary

Coder B submits → Reviewer A and Reviewer B may both review it.
If Coder A then submits, Reviewer A **prioritizes** Coder A (A is
Reviewer A’s primary). If Coder B is still writing and Coder A has
not submitted, Reviewer A reviews other ready code.

Cross-review until a coder of the **same department** submits.
Each reviewer has a primary item. If they cannot review that goal,
they review other ready code.

## Finish the line, then pass notes

Mid-review, if a primary item arrives: finish the current **line of
a file**, then pass notes to the reviewer whose primary it is.

Example: Reviewer A primary = C#; Reviewer C primary = Rust. Both
were on Rust. Coder A submits C# → Reviewer A sends Rust notes to
Reviewer C, then takes the C#.

The Orchestrator records the handoff on the review thread. Do not
swap silently.

## QA primary (same idea)

Several QA pods may test one item. If nothing matches a QA’s
primary, they test other ready items.

Mid-test, if their primary becomes ready: finish the current
assessment, pass notes, then switch.

**Client example (follow literally):** QA A primary C#, QA C primary
Rust. Rust is not reviewer-OK yet, C# is → both assess C#. When
Rust becomes ready, QA A sends C# notes to QA C and begins Rust;
QA C takes C# (QA C’s priority is C# in this example). The
Orchestrator records that swap on the review/QA thread.

## Send-back (review)

**a.** One reviewer on their primary → coder rewrites. That reviewer
joins other topics and **receives context + existing feedback**
before commenting.

**b.** Several reviewers in one session → **label which reviewer**
left each finding. On send-back, priority may shift with goals.

## Send-back (QA)

Same idea. Prioritize the assigned item when it is available. Pass
findings when helping on another item. When the primary becomes
available, finish the current test, pass notes, then switch.

QA send-back to the Coder still uses a **VM** review and a Handoff
`To: Coder` with **Sent back because**. See
[role-identity.md](role-identity.md).

## Orchestrator duties

- Spawn `/create-subagent` (`Task`, `run_in_background: true`) so
  pods overlap. Do not poll background subagents.
- Do not start Coder *N* before Research *N* is done.
- Record primary assignments, finish-the-line handoffs, and QA A/C
  swaps on the review thread.
- Gates still apply: no push while review is open; Security before
  publish; **only QA may give the OK to push.**
