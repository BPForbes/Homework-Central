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

<p align="left">
  <img src="https://cdn.jsdelivr.net/gh/devicons/devicon/icons/docker/docker-original.svg" width="40" height="40" alt="Docker" />
  <img src="https://cdn.jsdelivr.net/gh/devicons/devicon/icons/dotnetcore/dotnetcore-original.svg" width="40" height="40" alt=".NET" />
  <img src="https://cdn.jsdelivr.net/gh/devicons/devicon/icons/nodejs/nodejs-original-wordmark.svg" width="88" height="40" alt="Node.js" />
  <img src="https://cdn.jsdelivr.net/gh/devicons/devicon/icons/postgresql/postgresql-original.svg" width="40" height="40" alt="PostgreSQL" />
  <img src="https://cdn.jsdelivr.net/gh/devicons/devicon/icons/react/react-original.svg" width="40" height="40" alt="React" />
</p>

> **First-time setup:** Clone the repository, ensure Docker is running, then use one of the run commands below. The scripts create a `.env` file automatically with generated secrets.

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
| **Health check** | http://localhost:5000/healthz | API readiness probe |
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

```powershell
docker compose up -d                          # core only
docker compose --profile antivirus up -d      # + attachment malware scanning
docker compose --profile ai up -d             # + local AI reviewer
```

Do not enable `antivirus` and `ai` together on an 8 GiB machine. CPU values are
ceilings rather than reservations. The default development scripts run the API and
frontend on the host, so Docker limits do not cap those two host processes.
Override individual ceilings with `POSTGRES_MEMORY_LIMIT`, `CLAMAV_MEMORY_LIMIT`,
`LLM_MEMORY_LIMIT`, and related keys in `.env` when `docker stats` shows a need.

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
| [`docs/COMMENT_DOCUMENTATION_GUIDE.md`](docs/COMMENT_DOCUMENTATION_GUIDE.md) | Comment Documentation Guide (comments, naming, readability) |
| [`docs/identity.md`](docs/identity.md) | Authentication, sessions, account classes, tenant visibility |
| [`docs/chat.md`](docs/chat.md) | Chat categories, rooms, messages, attachments |
| [`docs/tickets.md`](docs/tickets.md) | Ticket portals, Trial Tutor, votes, AI scoring |
| [`design.md`](design.md) | UI design tokens and motion |
| [`deploy/kubernetes/README.md`](deploy/kubernetes/README.md) | Kubernetes deployment |

### Fast repeat starts

`run-dev` builds the API once and passes `HC_SKIP_DOTNET_BUILD=1` to its API child, so Kestrel
can bind without a duplicate build. It also starts the frontend before the API, allowing Vite to
bind to port 5173 while the API completes its startup work.

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
├── docker-compose.yml             # Postgres, FCaptcha, and production-style services
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
