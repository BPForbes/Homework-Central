# Tickets, media, and candidate assessment

Ticket workflows combine portal intake, a private chat room per case, optional
AI watches, and inbox notifications for staff. Confidence scoring, neural
monitors, and audit mechanics are documented in
[ticket-ai-scoring.md](ticket-ai-scoring.md); this file covers portals,
lifecycle, votes, and assessment ownership boundaries.

## Ticket open lifecycle

Current flow when a user opens a ticket:

1. **Portal** — The user submits intake answers on a ticket portal room
   (`CustomRoomType.Ticket`). Portal configuration (purpose, filter name,
   intake schema, staff access rules) is seeded or edited per channel. See
   [chat-room-access.md](chat-room-access.md) for how room access and account
   scope gate who can reach a portal.
2. **Private chat channel** — The backend allocates a new private chat room
   (`CustomRoomType.Chat`) with a display name such as `Ticket - Tutor - 0001`.
   Staff access rules from the portal and an opener rule are applied before
   the room appears in navigation.
3. **Watches** — Unless the intake opts out of AI tracking, `TicketUserWatch`
   rows are created from intake answers that identify users to monitor. Each
   new message from a watched user in the ticket room can enqueue assessment
   work (see [ticket-ai-scoring.md](ticket-ai-scoring.md)).
4. **Inbox** — Opening a ticket creates inbox notifications for configured
   staff. Decision and status changes can create further inbox entries.

Authentication, account class, and `tenant_db` scope for who may open or view
a ticket follow [auth-and-sessions.md](auth-and-sessions.md) and
[tenancy-isolation.md](tenancy-isolation.md). Attachments in ticket chat use
the same upload and scan pipeline as other rooms; see
[uploads-and-scanning.md](uploads-and-scanning.md).

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

Score movement, neural monitors, reviewer blend, and vector archive detail live in
[ticket-ai-scoring.md](ticket-ai-scoring.md). Backend assessment queue sizing
and service tradeoffs are in [system-efficiency.md](system-efficiency.md).

## LLM deploy

- Local: `llm` service in docker-compose (Ollama-compatible).
- Cluster: `deploy/k8s/llm` Kustomize base + overlays (`local`, `development`, `staging`, `production`).

## Related documentation

- [ticket-ai-scoring.md](ticket-ai-scoring.md) — neural monitors, confidence updates, audit archive
- [uploads-and-scanning.md](uploads-and-scanning.md) — chat attachments and ClamAV pipeline
- [chat-room-access.md](chat-room-access.md) — room categories, access bits, portal gating
- [auth-and-sessions.md](auth-and-sessions.md) — JWT, account class, developer login
- [tenancy-isolation.md](tenancy-isolation.md) — account-class visibility and content safety baselines
- [docs/README.md](README.md) — documentation index
