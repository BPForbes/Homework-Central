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

The first pass uses one of two isolated CPU hashed-MLP chat monitors. Neither
requires Ollama or an external ML runtime:

| Monitor | When selected | Topology |
|---|---|---|
| **Moderation** (`hc-chat-monitoring-moderation-v5`) | Default for conduct / filter tickets | `48 → 20 → 30 → 24 → 18 → 8` (2 sigmoid + 6 softmax categories) |
| **Tutoring** (`hc-chat-monitoring-tutoring-v5`) | Tutor-application style tickets | `48 → 36 → 52 → 44 → 36 → 16` (2 sigmoid + 13 general subjects + competency; wider hidden stack) |

Inputs are 48 dense features (44 hashed unigram/bigram bins from requirement,
thread context, and message, plus community vote, channel relevance, thread
continuity, and prior score). The network follows 3Blue1Brown-aligned learning:

- **Hidden layers:** leaky ReLU with He/Kaiming initialization
- **Evidence / relevance:** independent sigmoids + binary cross-entropy (not softmax —
  these are not mutually exclusive classes)
- **Category:** softmax + categorical cross-entropy. Moderation uses conduct classes;
  Tutoring uses every Mask-C general subject tag
  (Mathematics, Science, Computer Science, Languages, History, Business, Art, Music,
  Engineering, Medicine, Finance, Economics, Education) plus `tutoring-competency`.
  Tutoring hidden widths are enlarged so the larger subject head has enough capacity.
- **Cost:** mini-batch average \(C = \frac{1}{n}\sum C_x\) with momentum SGD on \(-\nabla C\)

Confidence blends evidence separation, support-set cosine similarity, and the
softmax category peak so live scoring can run **without an LLM** when
`Tickets:NeuralOnlyScoring` is enabled (or `OllamaEnabled` is false). Category
and reasoning come from the network itself once trained.

The selected monitor returns:

```json
{
  "studentScore": 0.0,
  "studentConfidence": 0.0,
  "relevance": 0.0,
  "category": "profanity"
}
```

Student confidence below `0.75` is sent to `qwen3:0.6b` for review. Ten percent
of higher-confidence results are also selected by a deterministic UUID sample
for auditing. When the reviewer is available, its score receives the configured
70% blend weight. If Ollama is unavailable, the bounded chat-monitor result is
still recorded and processing continues.

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

## Dual-model training and vector retrieval

`TicketModelTrainingExamples` is the authoritative training ledger and stores a
`ChatMonitoringKind` (`Moderation` or `Tutoring`) so each example trains only
its matching lineage. Live rows reference both the existing
`ChatMessages.MessageId` and the score-event UUID, so message text is not
duplicated. They store the frozen requirement, approved target score/relevance,
category, source, approver, and timestamp. Bootstrap rows are migration-seeded
and are the only rows containing inline text because they do not correspond to
real messages.

Synthetic admin training sessions accept mode `Both` (default), `Moderation`,
or `Tutoring`. `Both` creates concurrent per-kind `ChatMonitoringNeuralModelRun`
rows, each with its own worker/promotion replay and canonical checkpoint
lineage (`RuntimeKind = HashedMlpV5`).

Training efficiency (synthetic sessions):

1. **Teacher labels once per message** — LLM 1 returns `expectedScore` /
   `expectedRelevance` with the scenario when possible; otherwise one LLM-2
   label call fills missing targets. Those fixed labels are reused by both
   monitors and across local epochs (no per-pass LLM grading). Category targets
   map onto the softmax taxonomy for CE training.
2. **Local average-cost stop** — mini-batch momentum SGD against teacher targets
   until evidence/relevance tolerances and total average cost
   `LossStopThreshold` are met. Session `ReportJson` records per-stage timings
   and **average cost** (mean final mini-batch \(C\)).
3. **Domain batching** — in `Both` mode each monitor trains matching-domain
   tickets plus a small configurable cross-domain sample (default 15%) as
   negative controls.
4. **Batched persistence** — training examples and vector upserts flush in
   bounded batches (default 50) under a shared gate.
5. **Compact replay** — bulk runs record loss / gradient-norm traces;
   full parameter-delta traces are sampled (default 2%).
6. **LLM concurrency cap** — `Llm:MaxConcurrentRequests` (default 2) limits
   Ollama chat/embed parallelism.

### Path to LLM-free operation

| Role today | NN replacement |
|---|---|
| Live Ollama reviewer | `Tickets:NeuralOnlyScoring=true` — NN evidence/relevance/category/confidence only |
| LLM-2 teacher labels | Staff-approved training examples + synthetic labels during bootstrap |
| Keyword category strings | Softmax category head over `ChatMonitoringCategoryTaxonomy` |
| Heuristic “good enough” stop | Average cost \(C\) + \(\nabla C\) tolerances |

Independent LLM-2 audits remain optional at `NeuralNetTraining:AuditSampleRate`
for quality monitoring only; they do not drive the cost function.

Reviewer corrections require staff approval before training, preventing an
injected message from automatically poisoning the model. At startup each
chat-monitor lineage rebuilds its in-memory weights from approved rows for that
kind. Each row is mirrored under the `ticket_training_example` vector namespace
using the shared 48-float hashed embedding and a lineage `PositionId`
(`chat-monitoring-moderation` or `chat-monitoring-tutoring`). Live Ollama
reviews retrieve the three nearest approved examples for that same lineage
position. PostgreSQL remains authoritative; the vector namespace is retrieval-only.

Tune `StudentConfidenceThreshold`, `ReviewerAuditRate`, and
`ReviewerBlendWeight` under `Tickets`; matching Docker variables are documented
in `.env.example`. Synthetic training knobs live under `NeuralNetTraining` in
`appsettings.json`.
