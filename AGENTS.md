# AGENTS.md

## Cursor Cloud specific instructions

Homework Central is a full-stack app made of these services:

- **Backend API** — .NET 10 ASP.NET Core (`backend/HomeworkCentral.Api`), served on `http://localhost:5000`.
- **Frontend** — React + Vite + TypeScript (`frontend/`), dev server on `http://localhost:5173` (proxies `/api`, `/hubs`, `/healthz` to the API).
- **Postgres** — multi-tenant registry + per-persona tenant databases, run via Docker (`docker-compose.yml`), published on host port `5434` for the dev stack.
- **FCaptcha** — self-hosted captcha service run via Docker, published on host port `3010`.

The update script already runs `dotnet restore` and `npm ci`, so dependencies are refreshed on startup. The notes below cover non-obvious startup/run caveats not handled automatically.

### Docker must be started manually each session
Docker is installed but the daemon is **not** auto-started. Before running the app or backend DB tests:

```bash
sudo dockerd > /tmp/dockerd.log 2>&1 &   # start the daemon (already backgrounded)
sudo chmod 666 /var/run/docker.sock       # allow non-sudo docker access
```

Docker is configured with the `fuse-overlayfs` storage driver and `containerd-snapshotter` disabled (`/etc/docker/daemon.json`); this is required for Docker Engine 29 to work in this VM. `iptables` is set to the legacy backend.

### .NET SDK location
The .NET 10 SDK (10.0.301, per `global.json`) is at `/usr/local/dotnet` and symlinked to `/usr/local/bin/dotnet` (on `PATH`).

### Running the full dev stack
`scripts/run-dev.sh` starts everything (Docker Postgres + FCaptcha, API, frontend) and runs in the foreground supervising all children. Run it in a persistent tmux session. Caveats:
- Requires Docker running (see above).
- On first run it builds the FCaptcha image from `github.com/WebDecoy/FCaptcha` (needs network) and auto-generates `.env` (gitignored) with random `JWT_SECRET` / `FCAPTCHA_SECRET`.
- First API startup provisions ~70 persona tenant databases in the background (takes ~1 min). `GET /healthz` reports `personasReady` and `personasProvisioned`/`personasTotal`.
- The script tries to open browser tabs via `xdg-open`; those failures are harmless in a headless VM.
- The API root `GET /` intentionally returns a styled `403` in dev; the app UI lives at `http://localhost:5173`.
- `HC_DEV_BYPASS`/`VITE_HC_DEV_BYPASS` are enabled by the script, which exposes the `/devlogin` route and dev seed data.

### Tests
- Backend: `dotnet test HomeworkCentral.sln` (CI uses `--configuration Release --no-build`). Database-backed tests are `[SkippableFact]` and silently skip when Postgres is unreachable. To run them all, start a Postgres on `localhost:5432` (user/password `postgres`) and create three databases, then set the env vars:
  - `TEST_DATABASE_URL` → `homework_central_test`
  - `TEST_CHAT_DATABASE_URL` → `homework_central_test_chat`
  - `TEST_INFRA_DATABASE_URL` → `homework_central_test_infra`
  This test Postgres (host port 5432) is separate from the dev-stack Postgres (host port 5434) and they can run at the same time.
- Frontend: `npm run lint` and `npm run build` in `frontend/` (there is no frontend unit-test suite). `npm run lint` currently emits one pre-existing `react-refresh/only-export-components` warning (0 errors).

Standard build/lint/test commands are defined in `.github/workflows/ci.yml`, `frontend/package.json`, and the `scripts/` directory — refer to those rather than duplicating them.
