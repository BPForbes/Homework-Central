# NN training heap-pressure spill

## Symptoms from the local API console (2026-09-05)

Continuous training on Windows (`scripts/run-dev.ps1`) reached ~169 tickets,
then `System.OutOfMemoryException` on session
`d9908cb9-f58b-44cf-a41f-82bc9ec9240f`. The loop logged
“failed; continuing until stop” for steps 170–187. A second continuous
session (`b03f5067-…`) started and also OOM’d. `/api/neural-net/training/live`
and stop/resume then 500’d while JSON-serializing, and Kestrel reported
heartbeat starvation.

First-run `__EF*MigrationsHistory` failures, `/healthz` 200, DevAdmin login,
and `localhost:11434` connection refused (no Ollama) are not this bug.

## Root causes in this repo

1. `NeuralNetwork.BuildForwardTrace` appends a `SparseValue` for **every**
   weight and bias whenever `captureTrace: true`. Continuous training called
   `PredictWithTrace` on every synthetic message.
2. `NeuralMeshFrameExtractor` used `Parallel.ForEach` + `ConcurrentBag` +
   `OrderByDescending` over that entire dense bag. The live mesh only keeps
   480 nodes / 1200 edges; the sort allocated the whole parameter list.
3. `ReplayBuilder` retained every forward/backprop payload for the session.
4. After OOM, `RunContinuousSyntheticSessionAsync` caught `Exception` and
   retried the same allocating step. `ReplayBuilder.Build` then called
   `GetParameterSnapshot` (full flatten + base64) while the heap was already
   dead.

Persist-on-stop (`docs/nn-training-db-relief-research.md`) is still the
default. This work adds **one** mid-run SQL exception: spill when the heap
is about to fill.

## Honesty about Rust

Rust cannot see the CLR GC heap. `hc_heap_should_spill` only compares
numbers C# already sampled (`GC.GetGCMemoryInfo()`,
`Process.WorkingSet64`). `hc_heap_top_k_abs` is a bounded min-heap so mesh
highlights do not sort the full dense bag. EF, session orchestration, and
checkpoint JSON stay in C#. When `libhc_kernels` is missing, C# uses the
same watermark and a `PriorityQueue` top-K.

## External research

| URL | Takeaway |
|-----|----------|
| [GCMemoryInfo](https://learn.microsoft.com/en-us/dotnet/api/system.gcmemoryinfo) | `HeapSizeBytes`, `TotalAvailableMemoryBytes`, `MemoryLoadBytes`, and `HighMemoryLoadThresholdBytes` are the supported CLR pressure signals. |
| [.NET runtime metrics](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/built-in-metrics-runtime) | Heap and committed-size meters match `GC.GetTotalAllocatedBytes` / `TotalCommittedBytes`; they do not replace a pre-OOM spill. |
| [runtime#58974](https://github.com/dotnet/runtime/issues/58974) | `OutOfMemoryException` is the GC saying the managed heap is full — catch-and-retry without releasing objects keeps failing. |
| [Rust BinaryHeap](https://doc.rust-lang.org/stable/std/collections/struct.BinaryHeap.html) | Bounded min-heap top-K is O(n log k) and O(k) memory. |

## Plan

1. Sample CLR + RSS; Rust decides spill at 70% of available memory; skip
   traces at 55%.
2. On spill: **empty traces first**, then persist compact weights/bias
   (`spill-checkpoint-v1` only — no example/vector flush). Same-process
   continue keeps the live net (no `LoadParameterSnapshot`). Resume or
   process restart reloads the session checkpoint before `ReplayBuilder`
   captures `initial`. Finite complete/fail keep the spill row and do not
   overwrite it with V2 replay.
3. On `OutOfMemoryException`: spill once; if spill fails, stop. Do not
   retry the allocating step. After a successful spill, wait until the
   heap falls below the 55% skip-trace line before another mid-run SQL.
4. Refuse starting a second session while a run is live **and** the heap is
   already elevated.
5. Do not publish a canonical checkpoint on spill (promotion stays outside
   this path).
