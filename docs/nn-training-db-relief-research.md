# NN / ANI training database relief

## Symptoms from the local API console

Training saturates PostgreSQL. The SPA then loses the session and is sent to login.

Observed cascade:

1. The training page polls `GET /api/neural-net/training?limit=50` every 750ms–2s, including overlapping in-flight requests.
2. Each poll hits `NeuralNetTrainingService.GetTrainingSessionsAsync` and waits on a new Npgsql connector.
3. Postgres (`homework_central_master` on `localhost:5434`) times out after ~15s (`TimeoutException` / `NpgsqlException`).
4. The JWT expires. Polls flip from 500 to 401.
5. `POST /api/auth/refresh` needs the same exhausted pool (`AuthService.RefreshAsync`) and also times out (~15s, 500).
6. The frontend treats any refresh failure as logout (`tokenManager.refreshSession` → `clearAuthSession` → `hc:auth-expired`).
7. `NeuralNetCheckpointRefreshService` keeps retrying the same timed-out query every 30s, adding more pool pressure.

`GET /healthz` still returns 200. The API process is up; the **database pool is exhausted**.

## Root causes in this repo

- Continuous training used to write SQL mid-run via `PersistAsync` (session + replay) after tickets / run-context creation, and via `PersistenceBatch.FlushAsync` after each ticket. `FlushAsync` is **not** a no-op: it `AddRange`s examples, `SaveChangesAsync`, then upserts vectors. Persist-on-stop means **do not call** `FlushAsync` / `PersistAsync` during the loop — only on create / complete / fail / explicit stop. `EnqueueAsync` only appends in-memory lists (`batchSize` is unused for auto-flush).
- Finite synthetic runs also persisted at start (`Running`) and flushed examples mid-run.
- Training list polling always hits Postgres even though live progress already lives in `NeuralNetTrainingProgressStore`.
- Auth refresh has no reserved connection and no frontend distinction between “token rejected” and “database overloaded”.
- Stopping a continuous session only showed Stop. There was no resume Start for the same session.

## External research

- Npgsql pool wait / timeout: when `Max Pool Size` connectors are busy, new `Open` waits until `Timeout` then throws. See [Npgsql connection string parameters](https://www.npgsql.org/doc/connection-string-parameters.html) and [TimeoutException when opening a connection](https://github.com/npgsql/npgsql/issues/2779).
- JWT refresh must not treat infrastructure 5xx as an auth failure. See [OAuth 2.0 token refresh](https://datatracker.ietf.org/doc/html/rfc6749#section-6) and [Auth0: Handle token expiration](https://auth0.com/docs/secure/tokens/refresh-tokens).
- Training checkpoints should be batched, not written every step. See [PyTorch: saving and loading](https://docs.pytorch.org/tutorials/beginner/saving_loading_models.html) (save on epoch / interrupt, not every batch) and [EF Core: SaveChanges performance](https://learn.microsoft.com/en-us/ef/core/performance/efficient-updating).

## Rust

This checkout already has kernels under `rust/` (`hc-kernels`, `hc-gemv`, `hc-feature-encode`, `hc-vector-cosine`; workspace `rust/Cargo.toml`) bound from `backend/HomeworkCentral.Api/Assessment/RustKernels.cs`. Heap-pressure watermarks and bounded mesh top-K live in `hc-kernels` (`hc_heap_should_spill`, `hc_heap_top_k_abs`); C# still samples the CLR heap. Do **not** rewrite EF, auth, or training orchestration in Rust. See [`nn-training-heap-spill-research.md`](./nn-training-heap-spill-research.md).

## Plan

1. Buffer training examples and replay in memory. Write SQL on session create, then only when training stops / completes / fails (all training types).
2. Add resume for cancelled continuous sessions (same session id; UI Start continues).
3. Poll a memory-only live overlay while training is running; full list only on mount / after start-stop-resume.
4. Do not log the user out when `/api/auth/refresh` times out or returns 5xx.
5. Skip checkpoint refresh while a live training session is active.
