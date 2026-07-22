# Kubernetes on-call workloads

Kubernetes is optional and is intended for a real multi-node cluster, not Docker Desktop on the same low-memory Windows host. Docker Compose remains the local development stack.

## What scales

- The HTTP API has a CPU HPA (1–3 replicas).
- KEDA creates at most two temporary neural-training Jobs when PostgreSQL reports queued `NeuralNetTrainingSessions`.
- Each Job atomically claims one session, persists its training examples/report, then exits. Kubernetes removes the completed Job and pod after five minutes.

The API rebuilds its small student model from persisted approved examples on startup. This is the convergence point for training work completed by separate pods. Reports record the individual worker's snapshot; they are not a shared mutable in-memory model.

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