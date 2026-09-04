# Homework Central

<p align="center">
  <img src="https://img.shields.io/badge/.NET-10-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET 10" />
  <img src="https://img.shields.io/badge/React-18-61DAFB?style=for-the-badge&logo=react&logoColor=black" alt="React 18" />
  <img src="https://img.shields.io/badge/PostgreSQL-16-4169E1?style=for-the-badge&logo=postgresql&logoColor=white" alt="PostgreSQL 16" />
  <img src="https://img.shields.io/badge/Docker-Compose-2496ED?style=for-the-badge&logo=docker&logoColor=white" alt="Docker" />
  <img src="https://img.shields.io/badge/Node.js-18+-339933?style=for-the-badge&logo=node.js&logoColor=white" alt="Node.js" />
</p>

Homework Central is a full-stack web application with a **React + Vite** frontend, an **ASP.NET Core** API, and **PostgreSQL** for persistence. Local development is orchestrated by scripts in `scripts/` that start Docker services (Postgres and FCaptcha), build the stack, and launch the API and frontend.

---

## Prerequisites

Install the following before running the project locally.

| Requirement | Version | Notes |
|-------------|---------|-------|
| **Docker** | Latest | [Docker Desktop](https://www.docker.com/products/docker-desktop/) on Windows/macOS, or the Docker Engine on Linux. Must be running before you start the dev stack. |
| **.NET SDK** | 10.x | See [`global.json`](global.json) for the pinned SDK version. [Download .NET](https://dotnet.microsoft.com/download). |
| **Node.js** | 18+ | Includes **npm**, used for frontend dependencies. [Download Node.js](https://nodejs.org/). |
| **PowerShell** | 7+ (`pwsh`) | **Windows only** — required by the `.ps1` scripts. [Install PowerShell](https://learn.microsoft.com/powershell/scripting/install/installing-powershell). |
| **Bash** | Any recent shell | **Linux / macOS** — used by the `.sh` scripts. |
| **Rust** | stable | Required by the core compile scripts (`scripts/run-dev.*`, `scripts/start-api-dev.*`, `scripts/build-rust.*`). They run `cargo build --workspace` in `rust/` (`hc-feature-encode`, `hc-vector-cosine`, `hc-kernels`). Install with [rustup](https://rustup.rs/). Set `HC_SKIP_RUST_BUILD=1` to skip. |

<p align="left">
  <img src="https://cdn.jsdelivr.net/gh/devicons/devicon/icons/docker/docker-original.svg" width="40" height="40" alt="Docker" />
  <img src="https://cdn.jsdelivr.net/gh/devicons/devicon/icons/dotnetcore/dotnetcore-original.svg" width="40" height="40" alt=".NET" />
  <img src="https://cdn.jsdelivr.net/gh/devicons/devicon/icons/nodejs/nodejs-original-wordmark.svg" width="88" height="40" alt="Node.js" />
  <img src="https://cdn.jsdelivr.net/gh/devicons/devicon/icons/postgresql/postgresql-original.svg" width="40" height="40" alt="PostgreSQL" />
  <img src="https://cdn.jsdelivr.net/gh/devicons/devicon/icons/react/react-original.svg" width="40" height="40" alt="React" />
</p>

> **First-time setup:** Clone the repository, ensure Docker is running, then use one of the run commands below. The scripts create a `.env` file automatically with generated secrets.

---

## Install Rust

The lexical encoder and store cosine live in [`rust/`](rust/) (`hc-feature-encode`, `hc-vector-cosine`, `hc-kernels`). After `cargo build --workspace`, the API loads `libhc_kernels` for those two kernels only. The rest of the C# API and the TypeScript app stay as they are. Managed C# implementations run when the native library is absent (Docker publish, C# CI). Install rustup from [rustup.rs](https://rustup.rs/) or [rust-lang.org/tools/install](https://www.rust-lang.org/tools/install).

**Where rustup installs**

| Platform | Installer | Default directories |
|----------|-----------|---------------------|
| Linux / macOS / WSL | `https://sh.rustup.rs` | `~/.cargo` + `~/.rustup`; `cargo` on `PATH` via `~/.cargo/bin` (`source ~/.cargo/env`) |
| Windows | [rustup-init.exe (x64)](https://win.rustup.rs/x86_64) or [Arm](https://win.rustup.rs/aarch64) | `%USERPROFILE%\.cargo` (`bin` on `PATH`) and `%USERPROFILE%\.rustup` |

### Linux / macOS / WSL

```bash
curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh
```

Restart the shell, or `source "$HOME/.cargo/env"`. Then pin stable and confirm:

```bash
rustup default stable
rustc -V
cargo -V
```

### Windows

1. Download [rustup-init.exe](https://win.rustup.rs/x86_64) (or the [Arm build](https://win.rustup.rs/aarch64)).
2. Run the installer (default host is `*-pc-windows-msvc`; Visual C++ Build Tools if prompted).
3. Open a new PowerShell 7+ window and run:

```powershell
rustup default stable
rustc -V
cargo -V
```

### Compile the workspace

From the repository root, the same command the compile scripts run:

```bash
./scripts/build-rust.sh
```

```powershell
.\scripts\build-rust.ps1
```

Equivalent manual invoke:

```bash
cd rust && cargo build --workspace
```

`scripts/run-dev.sh`, `scripts/run-dev.ps1`, `scripts/start-api-dev.sh`, and `scripts/start-api-dev.ps1` call that `cargo build --workspace` during the normal compile path. Skip with `HC_SKIP_RUST_BUILD=1`.

---

## Quick start

### Windows (PowerShell)

From the repository root in **PowerShell 7+**:

```powershell
# Start the full dev environment (Postgres, FCaptcha, API, frontend)
.\scripts\run-dev.ps1
```

To **wipe the local database** (removes all registered accounts and seed data) and start fresh:

```powershell
.\scripts\reset-dev-db.ps1 -Yes && .\scripts\run-dev.ps1
```

### Linux / macOS (Bash)

From the repository root:

```bash
# Start the full dev environment
./scripts/run-dev.sh
```

To **reset the database** and start fresh:

```bash
./scripts/reset-dev-db.sh --yes && ./scripts/run-dev.sh
```

> On Unix, make scripts executable if needed: `chmod +x scripts/*.sh`

---

## What the dev stack starts

After a successful run, these services are available:

| Service | URL | Description |
|---------|-----|-------------|
| **Frontend** | http://localhost:5173/login | React app (Vite HMR) |
| **API** | http://localhost:5000 | ASP.NET Core (`dotnet watch` by default; `HC_API_WATCH=0` to disable) |
| **Health check** | http://localhost:5000/healthz | Listen probe (`starting` while migrate/seed runs, then `healthy`) |
| **Postgres** | `localhost:5434` (default) | Docker container; port configurable via `.env` |
| **FCaptcha** | `localhost:3010` (default) | Self-hosted captcha service (Docker) |

On **Windows**, the API and frontend each open in a **separate terminal window**. On **Linux/macOS**, both run in the current terminal (use `Ctrl+C` to stop).

The browser opens automatically when servers are ready.

The API uses **`dotnet watch`** by default so a `git pull` (or local edits) rebuilds/restarts without closing the terminal. That is process restart / .NET Hot Reload, not Vite-style HMR. Set `HC_API_WATCH=0` for a one-shot `dotnet run`. After pulling migrations, unset `HC_SKIP_DEV_WARMUP` (or restart once) so schema updates apply.

---

## Common commands

### Run only (keep existing data)

| Platform | Command |
|----------|---------|
| Windows | `.\scripts\run-dev.ps1` |
| Linux / macOS | `./scripts/run-dev.sh` |

### Reset database, then run

| Platform | Command |
|----------|---------|
| Windows | `.\scripts\reset-dev-db.ps1 -Yes && .\scripts\run-dev.ps1` |
| Linux / macOS | `./scripts/reset-dev-db.sh --yes && ./scripts/run-dev.sh` |

### Stop the dev stack

| Platform | Command |
|----------|---------|
| Windows | `.\scripts\stop-dev.ps1` (close API/frontend terminals manually) |
| Linux / macOS | `./scripts/stop-dev.sh` or `Ctrl+C` in the run terminal |

### Release Docker resources (Windows)

Stopping the stack releases container CPU and RAM immediately; the `pgdata` volume is retained:

```powershell
.\scripts\stop-dev.ps1
docker compose down
```

If Docker Desktop's WSL 2 VM still holds memory after the containers stop, quit Docker Desktop
and run `wsl --shutdown`, then start Docker Desktop again. To reclaim unused build cache and
images without deleting the database volume, run `docker system prune -af`. Do not add
`--volumes` unless you intentionally want to delete local Postgres data.

### Docker Compose profiles (8 GiB workstation)

`docker-compose.yml` keeps the lightweight core below a 1.25 GiB container ceiling.
Heavy services are opt-in profiles:

| Service | Profile | CPU ceiling | RAM ceiling |
|---|---|---:|---:|
| PostgreSQL, FCaptcha, Redis, API, nginx | default | ~1.75 total | ~1.25 GiB total |
| ClamAV | `antivirus` | 0.75 | 2,560 MiB |
| Ollama | `ai` | 1.50 | 1,536 MiB |
| MinIO (S3 API) | `object-storage` | 0.50 | 256 MiB |

```powershell
docker compose up -d                          # core only
docker compose --profile antivirus up -d      # + attachment malware scanning
docker compose --profile ai up -d             # + local AI reviewer
docker compose --profile object-storage up -d # + free S3-compatible MinIO
```

Attachment bytes default to the local `uploads` volume (`Uploads:Backend=Local`).
To use MinIO with the Compose API container, set `UPLOADS_BACKEND=S3` in `.env`
(and start the `object-storage` profile). Downloads still go through the API so
auth and malware caution gates are unchanged. The MinIO console is on port 9001.

Do not enable `antivirus` and `ai` together on an 8 GiB machine. CPU values are
ceilings rather than reservations. The default development scripts run the API and
frontend on the host, so Docker limits do not cap those two host processes.
Override individual ceilings with `POSTGRES_MEMORY_LIMIT`, `CLAMAV_MEMORY_LIMIT`,
`LLM_MEMORY_LIMIT`, and related keys in `.env` when `docker stats` shows a need.

**Connections are bounded client-side, not by `max_connections`.** Each tenant is a distinct
`Database=` value, so Npgsql keys one pool *per tenant* — capping a pool's width does not cap
how many pools exist. `TenantConnectionResolver` therefore also expires idle tenant connections
after 60s, and one-shot provisioning (create/migrate/seed) runs unpooled so walking all 70 dev
personas does not retain a server slot per persona. Tune with `Tenancy:MaxPoolSizePerTenant`
and `Tenancy:ConnectionIdleLifetimeSeconds`.

**Disk is bounded too, not just memory.** Container logs use Docker's `json-file` driver, which
is unbounded by default — every line any service writes stays on disk until the container is
removed. Each service is capped at 10 MiB x 3 files, bounding the whole stack at roughly 240 MiB
of logs. Postgres runs with `wal_compression=on` and `max_wal_size=512MB`, which cuts the bytes
written to the SSD per checkpoint cycle and bounds `pg_wal`. Canonical neural checkpoints are
trimmed to the newest `NeuralNetCheckpointStore.RetainedGenerations` (10) per lineage: only the
newest is ever read, and each row holds a full base64-packed parameter snapshot, so an unbounded
lineage turned every training publish into permanent disk.

**FCaptcha rebuilds only when its image is missing.** It is pinned to upstream v1.12.0, so the
dev scripts no longer wake BuildKit for a no-op rebuild on every start; set
`HC_FCAPTCHA_REBUILD=1` to force one.

**One container per service type.** Compose already defines a single `fcaptcha`,
`postgres`, `redis`, `backend`, `frontend`, and (with profiles) `llm` / `minio` /
`clamav`. Reuse that Ollama container for both ticket review and neural synthetic
training (`Tickets:OllamaBaseUrl` and `Llm:BaseUrl` point at the same host). Do
**not** also `docker compose up` the `backend`/`frontend` services while
`scripts/run-dev*` is serving them on the host — that doubles RAM/CPU for the
same roles. There is no separate API gateway or load-balancer container in
Compose; frontend nginx is the only reverse proxy. Packaged nginx caches hashed
`/assets/*` for a year (`Cache-Control: public, immutable`) while `index.html`
stays `no-cache`. The API compresses large JSON responses (Brotli/gzip) and
accepts compressed request bodies; neural-net session/feedback lists are cursor-
paginated (`beforeUtc` + `limit`).

### Running services outside Docker

Docker is a convenience for these services, not a requirement. Anything moved to the host stops
counting against the Docker VM's memory entirely.

**Ollama is the one worth moving.** It is the largest service in the stack at 1,536 MiB, and a
host install also gets real GPU acceleration — Metal on macOS, CUDA elsewhere — which Docker
Desktop cannot pass through on macOS at all, so the container is both the biggest and the slowest
option. Install Ollama natively, `ollama pull qwen3:0.6b && ollama pull nomic-embed-text`, and
then just never start the `ai` profile:

- `scripts/run-dev.*` needs no configuration at all: `Llm:BaseUrl` and `Tickets:OllamaBaseUrl`
  already default to `http://localhost:11434` in `appsettings.json`.
- For the Compose API container, set `LLM_BASE_URL=http://host.docker.internal:11434` in `.env`.

The API degrades gracefully when no Ollama is reachable — embeddings fall back to a local hash
embedding and ticket review falls back to the reviewer — so this is safe to try.

**Postgres** can already be skipped with `./scripts/run-dev.sh --skip-docker` (`-SkipDocker` on
Windows) when you have a local server on `localhost`.

**Redis is optional.** With no `ConnectionStrings:Redis` the API registers an in-process
distributed cache instead, which is all a single instance needs — Redis matters when several API
instances have to share cache state. `scripts/run-dev.*` never starts it. To drop it from the
Compose stack too, set `REDIS_CONNECTION=` (empty) in `.env`.

**Keep FCaptcha and MinIO in Docker.** FCaptcha has no released binary and is built from an
upstream tag, so a container is the simpler distribution; MinIO is profile-gated and off by
default, since uploads default to the local `uploads` volume. For ClamAV see the notes below —
a native `clamd` moves its signature memory out of the WSL cap but does not shrink it.

---

### WSL caps (Windows Docker Desktop)

Compose limits do not include the Linux kernel, Docker daemon, or filesystem cache.
Copy [`deploy/windows/.wslconfig.example`](deploy/windows/.wslconfig.example) to
`%USERPROFILE%\.wslconfig` (4 GiB memory, 4 processors, 2 GiB swap), run
`wsl --shutdown`, and restart Docker Desktop. Sustained swapping means the active
profile is too large; avoid running other WSL distributions alongside a heavy profile.

### ClamAV notes

ClamAV is profile-gated because the signature engine dominates RAM. The local image
disables concurrent database reloads so scans pause briefly during signature update
instead of holding two engines. Enable it in the script path with
`HC_ENABLE_CLAMAV=1` before `scripts/run-dev.sh`, or use
`docker compose --profile antivirus up -d`. An npm ClamAV package is only a client
wrapper; the backend already streams to `clamd` via `nClam`. A native Windows
`clamd` on port `3310` moves signature RAM outside the WSL cap but does not shrink
it — the Docker antivirus profile remains the safer default on the documented
workstation. Upload scan statuses and fail-open behavior are in
[`docs/chat.md`](docs/chat.md).

Ticket AI uses bounded per-message confidence changes with an auditable vector
archive; see [`docs/tickets.md`](docs/tickets.md#neural-monitors-and-ollama-blend).

### Documentation

Architecture, trust boundaries, and engineering standards live under
[`docs/`](docs/README.md):

| Document | Topic |
|----------|-------|
| [`docs/COMMENT_DOCUMENTATION_GUIDE.md`](docs/COMMENT_DOCUMENTATION_GUIDE.md) | Comment Documentation Guide (comments, naming, readability; CodeQL CI) |
| [`docs/identity.md`](docs/identity.md) | Authentication, sessions, account classes, tenant visibility |
| [`docs/chat.md`](docs/chat.md) | Chat categories, rooms, messages, attachments |
| [`docs/tickets.md`](docs/tickets.md) | Ticket portals, Trial Tutor, votes, AI scoring |
| [`design.md`](design.md) | UI design tokens and motion |
| [`deploy/kubernetes/README.md`](deploy/kubernetes/README.md) | Kubernetes deployment |

### Fast repeat starts

`run-dev` builds the API once and passes `HC_SKIP_DOTNET_BUILD=1` to its API child, so Kestrel
can bind without a duplicate build. It also starts the frontend before the API. The API exposes
`/healthz` as soon as Kestrel listens (`status: starting` during migrate/seed, then `healthy`),
so the Vite BackendGate can wait without flooding the proxy with connection-refused errors.

After one successful initialization of the local database, you can skip development migrations
and seed warmup on repeat starts:

```powershell
$env:HC_SKIP_DEV_WARMUP = '1'
.\scripts\run-dev.ps1
```

Unset this variable and start normally after pulling migrations or authorization/seed changes, or
after resetting the local database. Never use it for a fresh database.

### Build without starting servers

| Platform | Command |
|----------|---------|
| Windows | `.\scripts\run-dev.ps1 -BuildOnly` |
| Linux / macOS | `./scripts/run-dev.sh --build-only` |

### Help

| Platform | Command |
|----------|---------|
| Windows | `.\scripts\run-dev.ps1 -Help` |
| Linux / macOS | `./scripts/run-dev.sh --help` |

---

## Project layout

```
Homework-Central/
├── backend/HomeworkCentral.Api/   # ASP.NET Core API
├── frontend/                      # React + Vite SPA
├── docs/                          # Architecture and engineering standards
├── deploy/                        # Kubernetes and Windows Docker helpers
├── scripts/                       # Dev orchestration (.ps1 / .sh)
├── docker-compose.yml             # Core services; ClamAV / Ollama / MinIO behind profiles
├── HomeworkCentral.sln            # .NET solution
└── global.json                    # Pinned .NET SDK version
```

---

## Troubleshooting

- **Docker not running** — Start Docker Desktop (or the Docker daemon) and retry.
- **Port already in use** — Another Postgres install may own `5434` on localhost. The run scripts try to pick a free port and update `.env`; you can also set `POSTGRES_HOST_PORT` manually.
- **Stale database volume** — Run the reset command above (`reset-dev-db` with `-Yes` / `--yes`), then `run-dev` again.
- **`Network homework-central_default Resource is still in use`** — Harmless during reset. The `pgdata` volume was still removed; `run-dev` reuses the leftover Docker network. Optional: `docker network inspect homework-central_default` to see what is attached.
- **Skip Docker** — If you already have Postgres on localhost: `.\scripts\run-dev.ps1 -SkipDocker` (Windows) or `./scripts/run-dev.sh --skip-docker` (Unix).

---

## License

See the repository for license details.
