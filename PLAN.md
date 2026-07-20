# Neural Replay V2, Independent Community Signals, and Canonical Promotion

This implementation evolves the fixed 256 → 8 → 2 student model into an auditable training system. V2 records actual forward propagation, LLM 2 evaluation, loss, backpropagation, parameter updates, and replay frames. It keeps worker-local candidates separate from the canonical promoted checkpoint.

## Core decisions

- Training modes: Both (default), Moderation, Tutoring.
- LLM 1 generates synthetic ticket/thread scenarios and only proposes community reception.
- LLM 2 evaluates each message blind to the proposal; application code resolves disagreement, samples reproducible votes, and derives bounded evidence adjustment.
- The topology remains fixed. Node/edge/parameter IDs and lifecycle fields make future structural changes representable.
- Reports use lossless indexed hybrid sparse/dense float32 telemetry, initial/final snapshots, pass snapshots, frames, payloads, string tables, and integrity checksums.
- Kubernetes workers create session-local diagnostic candidate reports and approved examples. A leased ordered promoter retrains the canonical checkpoint and writes a separate promotion replay.

## Replay UI

The visualizer reads canonical frames, supports ticket selection, All, detail −/+, step back/forward, and play/pause. Clustered, layer, and full detail views are projections of recorded topology. White indicates nonzero forward contribution; verdict changes the same contribution path to accessible accepted/revision treatment; dashed reverse flow represents recorded gradients.

## Implementation validation

Tests verify float32 delta reconstruction, parameter ordering, LLM 2 single evaluation per pass, deterministic votes, promotion leasing/retries/dedupe, report integrity, and full-detail replay rendering.
## Implementation checklist

- [x] Add initial V2 replay telemetry contracts.
- [x] Capture real forward activations, BCE loss, gradients, deltas, and fixed topology in the student model.
- [x] Add deterministic independent community-signal resolver and seeded binomial vote sampling.
- [x] Integrate threaded LLM 1 scenarios and LLM 2 blind message evaluation into training sessions.
- [x] Persist worker-local V2 reports with frames, payload tables, topology, and packed initial/final parameter snapshots.
- [x] Persist canonical checkpoints, promotion sequence/lease/status, and migrations.
- [x] Add canonical promotion/checkpoint reload service.
- [x] Replace visualizer with V2 frame controls, clustered/full graph, and inspector.
- [x] Add replay/promotion tests and run isolated API/frontend builds.
- [x] Commit the completed implementation; push follows immediately.
