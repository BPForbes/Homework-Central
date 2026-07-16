# Tickets, media, and candidate assessment

## Default portals

Seeded per database:

| Display name | Filter name | Room title pattern |
|---|---|---|
| Apply for Tutor Positions | Tutor | `Ticket - Tutor - 0001` |
| Notify Mods | Mod-Mail | `Ticket - Mod-Mail - 0001` |

Portal **purpose** is the human label; **filterName** is used in ticket room titles.

## Trial Tutor

Cosmetic, mentionable platform role (`TrialTutor`, bit 20). Mutually exclusive with `Tutor`. Granted when a human approves an Approve/Trial decision on a tutor-application ticket. No Staff inheritance.

## Message votes

Reddit-style up/down on chat bubbles. Score = upvotes − downvotes. Hover reveals vote/reply/copy/report; score hides while actions are shown. Report opens Notify Mods with sender + forwarded message prefilled.

## Assessment ownership

- LLM returns structured eligibility + rubric components only.
- Deterministic code owns \(q_i\), evidence weights, Beta updates, thresholds, and decisions.
- PostgreSQL is authoritative for scores/events.
- Vector namespaces (`scoring_reference`, `candidate_evidence`, `assessment_ticket_memory`) are retrieval-only; never feed running \(\mu_k\) into the LLM prompt.

## LLM deploy

- Local: `llm` service in docker-compose (Ollama-compatible).
- Cluster: `deploy/k8s/llm` Kustomize base + overlays (`local`, `development`, `staging`, `production`).
