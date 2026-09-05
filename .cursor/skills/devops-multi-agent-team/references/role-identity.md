# Role identity, questions, and handoffs

Every DevOps role reads this file. Put the same rules in the agent's
identity so a bounce or a side sprint keeps context.

## Ask paths (main)

Anyone may ask anyone when blocked. Prefer this order:

| Who has a question | Asks first | Then |
|--------------------|------------|------|
| QA | **Coder** (primary) | Reviewer |
| Coder | **Researcher** | Orchestrator |
| Reviewer | **Orchestrator** (Team Lead) | Coder |
| Security / CI / Verifier / Ticket Lead | Orchestrator or the role that opened the task | — |
| Orchestrator (Team Lead) | **The Client** (human) | — |

The Orchestrator is the only role that asks the human unless the human
spoke first.

## Human interrupt (side sprint)

When The Client gives instructions while the skill is running:

1. The Orchestrator pauses the current pod only as needed.
2. Start a **side sprint from research**: how the ask integrates with
   existing docs, then how it integrates in code.
3. Talk to Coder, Reviewers, QA, and Security as that sprint needs —
   do not wait for the original topic to finish.
4. Write a role goal under `.cursor/thoughts/non-finalized/`.
5. Resume the original loop when the side sprint is parked or merged
   into the plan.
6. A human interrupt does **not** authorize a push.

## Role goals

Each role type writes its own goal file while the concept is open:

`.cursor/thoughts/non-finalized/goal-<role>-<topic>.md`

Move it to `.cursor/thoughts/finalized/` when QA signs off on that
concept (see [thoughts-layout.md](thoughts-layout.md)).

## Handoff block (required)

When sending work to another role, or sending it back, append:

```markdown
## Handoff
- From: <role>
- To: <role>
- Pass-along: <what the next role must know>
- Sent back because: <reason, or "n/a">
- Ask: <question, or "n/a">
```

A send-back without **Sent back because** is incomplete. The receiving
role copies that block into its goal or the review thread.
