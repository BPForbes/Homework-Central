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
| **Frontend** | http://localhost:5173/login | React app (Vite dev server) |
| **API** | http://localhost:5000 | ASP.NET Core backend |
| **Health check** | http://localhost:5000/healthz | API readiness probe |
| **Postgres** | `localhost:5434` (default) | Docker container; port configurable via `.env` |
| **FCaptcha** | `localhost:3010` (default) | Self-hosted captcha service (Docker) |

On **Windows**, the API and frontend each open in a **separate terminal window**. On **Linux/macOS**, both run in the current terminal (use `Ctrl+C` to stop).

The browser opens automatically when servers are ready.

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
- **Skip Docker** — If you already have Postgres on localhost: `.\scripts\run-dev.ps1 -SkipDocker` (Windows) or `./scripts/run-dev.sh --skip-docker` (Unix).

---

## License

See the repository for license details.
