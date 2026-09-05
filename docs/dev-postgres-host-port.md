# Dev Postgres host port (Windows Docker Desktop)

## Symptom

After `scripts/reset-dev-db.ps1 -Yes` then `scripts/run-dev.ps1`, Docker Postgres starts and
`homework_central_master` is created **inside** the container, then the host check fails:

```
Cannot connect to homework_central_master on localhost:5434 from the host
The operation has timed out
Port 5434 is bound on 127.0.0.1 by another PostgreSQL install
Pick a free port in .env (for example POSTGRES_HOST_PORT=5434)
```

The example port in that message was the same port that just failed.

## What is actually happening

`Wait-ForPostgres` / `pg_isready` only prove the server is healthy **inside** the container.
`PostgresHostCheck` and the API use Npgsql on the **published host port**.

On Windows, `Host=localhost` is dual-stack. The OS prefers IPv6 (`::1`). Docker Desktop
typically publishes the compose port on IPv4 (`127.0.0.1`) only. Npgsql's `Timeout=5` then
expires on the unanswered `::1` attempt before IPv4 fallback. That matches the logged
"The operation has timed out".

After `docker compose up`, Docker's own userspace proxy is the process listening on
`127.0.0.1:5434`. Treating that listener as "another PostgreSQL install" is a false
positive. A real foreign install is a listener that is **not** this compose project's
published port, including binds on `0.0.0.0` that the old loopback-only check missed.

## External research

| Source | Takeaway |
|--------|----------|
| [The localhost trap (Windows IPv6)](https://dev.to/skucherenko/the-localhost-trap-a-10-second-database-connection-on-windows-3le7) | `localhost` → `::1` first; Docker IPv4-only publish; connect hangs then times out. Use `127.0.0.1`. |
| [Npgsql connection string parameters](https://www.npgsql.org/doc/connection-string-parameters.html) | `Timeout` is the connect budget (seconds). A short timeout on `::1` does not leave time to try IPv4. |
| [Docker Desktop published ports](https://docs.docker.com/engine/network/) | Host publish is a proxy, not the container's own listen address. In-container `psql` success does not imply host reachability. |

## Script contract

1. Connect from the host with `Host=127.0.0.1` (same path as the API).
2. Before `compose up`, remap when **any** non-Docker listener owns the port (`0.0.0.0`, `127.0.0.1`, `::`, `::1`).
3. Do not remap away from a container this project already published.
4. If the host still cannot open `homework_central_master` after in-container create: recreate the container once, then pick the next free port in `5434–5450`, write `.env`, and recreate again.
5. Failure hints must suggest a **different** free port than the one that failed.
