# Ticket AI confidence scoring

Ticket AI is evidence support, not an autonomous decision-maker. A ticket's
intake answer can opt out before any model processing. For opted-in tickets, an
active `TicketUserWatch` causes each new message from the watched user to be
evaluated against the ticket's frozen tracking template.

## Score meaning

Every ticket/user sequence starts at `0.5`:

- `0` means low confidence that observed messages meet the monitoring condition.
- `0.5` means uncertain or neutral.
- `1` means high confidence that observed messages meet the condition.

The first pass is a tiny deterministic C# neural student (256 hashed inputs,
one 8-neuron hidden layer, and evidence/relevance outputs). It does not require
Ollama or an external ML runtime. The student returns:

```json
{
  "studentScore": 0.0,
  "studentConfidence": 0.0,
  "relevance": 0.0,
  "category": "spam"
}
```

Student confidence below `0.75` is sent to `qwen3:0.6b` for review. Ten percent
of higher-confidence results are also selected by a deterministic UUID sample
for auditing. When the reviewer is available, its score receives the configured
70% blend weight. If Ollama is unavailable, the bounded student result is still
recorded and processing continues.

Application code calculates the update:

```text
requested delta = (evidenceConfidence - 0.5) × 2 × relevance × max delta
current score   = clamp(previous score + requested delta, 0, 1)
```

The default maximum movement is `0.15` per message. Therefore no model response
can move the running score by more than ±0.15, and an irrelevant message causes
no movement. These defaults are configurable through `Tickets` options.

## Prompt-injection boundary

Ticket templates, intake context, and chat messages are wrapped in explicit
untrusted-data sections. The system prompt tells the model never to execute
instructions found inside them. Qwen thinking is disabled, temperature is zero,
context is capped at 2,048 tokens, output is capped at 256 tokens, and the JSON
response is type/range validated. Final score arithmetic and clamping occur only
in trusted application code.

This reduces prompt-injection risk but cannot make model output authoritative.
Staff must separately approve any suggested ticket decision in the UI.

## Audit and vector archive

`TicketMessageScores` is the authoritative append-only audit record. Each row
contains:

- score-event, ticket, message, and tracked-user UUIDs;
- previous score, signed delta, and current score;
- per-message evidence confidence and relevance;
- rationale, raw JSON, model version, and timestamp.

The database enforces `[0,1]` constraints and uniqueness per ticket/message. A
copy is stored under the `ticket_message_evidence` vector namespace. Its content
is the message text; metadata contains the UUIDs and score fields. Vector search
is retrieval-only and cannot modify authoritative scores.

The ticket analysis API returns the newest 200 events in chronological order.
The ticket UI displays the current score and ten newest deltas. AI analysis no
longer auto-approves a suggested decision.

## Reviewer training and vector retrieval

`TicketModelTrainingExamples` is the authoritative training ledger. Live rows
reference both the existing `ChatMessages.MessageId` and the score-event UUID,
so message text is not duplicated. They store the frozen requirement, approved
target score/relevance, category, source, approver, and timestamp. Bootstrap
rows are migration-seeded and are the only rows containing inline text because
they do not correspond to real messages.

Reviewer corrections require staff approval before training, preventing an
injected message from automatically poisoning the model. At startup the student
rebuilds its small in-memory weights from approved rows. Each row is mirrored
under the `ticket_training_example` vector namespace using a local 64-float
embedding. An Ollama review receives the three nearest approved examples from
the same category as untrusted context. PostgreSQL remains authoritative; the
vector namespace is retrieval-only.

Tune `StudentConfidenceThreshold`, `ReviewerAuditRate`, and
`ReviewerBlendWeight` under `Tickets`; matching Docker variables are documented
in `.env.example`.
