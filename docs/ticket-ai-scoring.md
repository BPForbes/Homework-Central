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
| **Moderation cascade** (`…-moderation-evidence-v8` + concept router) | Default for conduct / filter tickets | Stage-1 router `30 → 24 → 8` concept/family context; stage-2 `86 → 48 → 72 → 64 → 56 → 103` (100 fine concepts + catch-all) |
| **Tutoring cascade** (`…-tutoring-evidence-v8` + subject router) | Tutor-application tickets | Stage-1 router `30 → 24 → 8` subject context; stage-2 `86 → 40 → 56 → 48 → 40 → 16` |

Inputs are 86 dense features: hashed text, community/prior metadata, **applied-subject
multi-hot + count**, **channel-subject multi-hot**, exact/related/cross-subject
relatedness (tutoring), and an 8-d **cascade stage-1 embedding** (slots 78–85) for both
monitors.

Both monitors use a **neural cascade** composed as \(g(f(x))\) with **chain-rule** training:
\(\frac{\partial C}{\partial \theta_f} = \frac{\partial C}{\partial f}\frac{\partial f}{\partial \theta_f}\),
where \(\partial C/\partial f\) is backprop into cascade slots 78–85.

### Moderation cascade

1. **\(f\)** — concept-context router embeds the reported hypothesis + family/related concepts
   (e.g. `payment-solicitation` with `tip-pressure`, `off-platform-payment`, …)
2. **\(g\)** — evidence scorer predicts evidence, relevance, and one of **100 fine-grained
   moderation concepts** (+ `moderation-general`)

Softmax vocabulary covers ten families: financial/commercial abuse, fraud/impersonation,
privacy, sexual misconduct, minor safety, physical safety/coercion, cybersecurity,
platform manipulation, discrimination/coercion, and dangerous misinformation. Legacy broad
labels (`spam`, `profanity`, `threat`, `harassment`, `evasion`) normalize onto this set.

A report should select a hypothesis to monitor (not establish guilt), optionally with
`relatedConcepts` for near-miss / escalation context. Training should include positives,
ordinary negatives, hard negatives (e.g. voluntary tip offers), multi-message escalation,
and explicit exclusions.

### Tutoring cascade

1. **\(f\)** — subject-context router (`30 → 24 → 8`) embeds multi-subject application ↔ channel
2. **\(g\)** — evidence scorer predicts evidence, relevance, and subject category from text
   **plus** \(f(x)\)

Reward policy still scales ticket relevance by relatedness tier (exact + cross-subject
support > related-only > unrelated), matching “monitor applied strictly, related
mildly, unrelated reward-only.”

### Tutor subject text processing

Tutor intake (`tutor-subjects`) is normalized by a deterministic processor
(`TutorSubjectTextProcessor`) — not a separate neural cascade:

- Lowercases input (so `Rust` / `RUst` / … all match)
- Maps expertise aliases onto Mask-C generals (`biology` → Science, `rust` → ComputerScience)
- Spell-checks against the known subject/expertise vocabulary (Levenshtein)
- Rejects unverified tokens and asks the applicant to re-enter
- Stores the canonical Mask-C display list (e.g. `Science, Computer Science`)

Custom rooms/categories are out of scope for now.

The network follows 3Blue1Brown-aligned learning:

- **Hidden layers:** leaky ReLU with He/Kaiming initialization (scorer); tanh router
- **Evidence / relevance:** independent sigmoids + binary cross-entropy
- **Category:** softmax + categorical CE (moderation: 100 concepts + catch-all; tutoring: Mask-C + competency)
- **Cost:** mini-batch average \(C = \frac{1}{n}\sum C_x\) with momentum SGD on \(-\nabla C\)
  (cascade: joint update of \(\theta_g\) and \(\theta_f\) each epoch)

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
lineage (`RuntimeKind = HashedMlpV8`).

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
