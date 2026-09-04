# Kubernetes on-call workloads

Kubernetes is optional and is intended for a real multi-node cluster, not Docker Desktop on the same low-memory Windows host. Docker Compose remains the local development stack.

## Work split: training vs visualization

| Workload | Role | Scaling |
|---|---|---|
| `homework-central-api` Deployment | HTTP API, live training progress polling, mesh visualization reads | CPU HPA (1–3) |
| `neural-net-training` KEDA ScaledJob | Claims queued synthetic sessions, runs Math.NET training, persists examples/reports | Queue-driven Jobs (up to 4) |

API pods set `KubernetesTraining__DisableInProcessWorker=true` so they do **not** run the in-process training consumer. Training Jobs set `KubernetesTraining__RunOneQueued=true` (and also disable the long-lived worker) so each pod claims one session and exits.

Live mesh frames are published into PostgreSQL-backed session state by the training Job; API replicas only serve that progress to the SPA. That keeps orbit/slice visualization off the training CPU path.

## What scales

- The HTTP API has a CPU HPA (1–3 replicas) for visualization and admin traffic.
- KEDA creates temporary neural-training Jobs when PostgreSQL reports queued `NeuralNetTrainingSessions` (up to four concurrent Jobs).
- Each Job atomically claims one session, persists its training examples/report, then exits. Kubernetes removes the completed Job and pod after five minutes.

The API rebuilds its small student model from persisted approved examples on startup. This is the convergence point for training work completed by separate pods. Reports record the individual worker's snapshot; they are not a shared mutable in-memory model.

## Where a GPU helps

Synthetic training wall-clock is dominated by Ollama calls, not by the stage-2 scorers. Put GPU
capacity behind the language model and leave the .NET workloads on CPU:

- **Ollama** is the only component worth a GPU. Run it on a GPU node (`nvidia.com/gpu` resource plus
  the device plugin) and point `Llm:BaseUrl` at it. Ollama uses CUDA/ROCm/Metal automatically when
  the runtime is visible; no application change is required.
- **Stage-1 routers and stage-2 scorers** are ~11k–22k parameter dense nets. Host-to-device transfer
  costs more than the multiply, so they stay on CPU. `MathNet.Numerics.Control.TryUseNative()` picks
  up an installed MKL/OpenBLAS provider for SIMD; without one the managed provider is used.
- **Training Jobs** need CPU and memory, not accelerators. Raise `resources.requests.cpu` before
  considering a GPU for them.

A training Job that cannot reach Ollama does not fail: generation returns nothing, the loop backs
off, and only an explicit stop ends the session.

## API gateway, load balancer, and task orchestration

| Concern | Status in this repo |
|---|---|
| API gateway (Kong, YARP gateway product, etc.) | **Not deployed** |
| Cluster Ingress / Gateway API / Traefik / Caddy | **Not deployed** |
| HTTP edge | Frontend nginx (Compose image) proxies `/api/` → API; K8s manifests here expose the API Service only |
| Load balancing | API Deployment + CPU HPA (1–3). No external LB object is defined |
| Task orchestration | **KEDA ScaledJob** `neural-net-training` claims queued sessions; API pods disable the in-process worker |

Do not add a second Ollama Deployment beside `deploy/k8s/llm`, and do not run a second API image for training — reuse `homework-central-api:latest` for both the Deployment and ScaledJob.

## What deliberately does not autoscale

PostgreSQL is stateful. Ollama and ClamAV are memory-heavy. Scale them only with independently benchmarked remote nodes and explicit capacity, not HPA on the Windows development machine.

## Cluster prerequisites

1. A container image registry reachable by the cluster; replace `homework-central-api:latest`.
2. PostgreSQL reachable from the cluster and a `homework-central-runtime` Secret containing `POSTGRES_CONNECTION`, the application connection-string settings, JWT/FCaptcha secrets, and LLM endpoint configuration.
3. KEDA installed in the cluster.
4. Run database migrations before applying workers.

Apply with:

```sh
kubectl apply -f deploy/kubernetes/workloads.yaml
```

To stop all on-call workers immediately:

```sh
kubectl -n homework-central delete scaledjob neural-net-training
kubectl -n homework-central delete job -l scaledjob.keda.sh/name=neural-net-training
```
