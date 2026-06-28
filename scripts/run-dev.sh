#!/usr/bin/env bash
# Start the full Homework Central local dev stack (Postgres, API, frontend).
#
# Usage:
#   scripts/run-dev.sh              # build + run everything
#   scripts/run-dev.sh --build-only # compile only (no servers)
#   scripts/run-dev.sh --help
#
# Environment:
#   HC_SKIP_BUILD=1    Skip dotnet/npm builds (set by IDE after a fresh compile)
#   HC_SKIP_DOCKER=1   Skip starting Postgres via Docker (use existing DB)
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
API_PROJECT="$REPO_ROOT/backend/HomeworkCentral.Api/HomeworkCentral.Api.csproj"
FRONTEND_DIR="$REPO_ROOT/frontend"
ENV_FILE="$REPO_ROOT/.env"
BUILD_ONLY=false
SKIP_DOCKER=false

usage() {
  cat <<'EOF'
Homework Central — local dev stack

Usage:
  scripts/run-dev.sh [options]

Options:
  --build-only   Compile the API and install frontend deps; do not start servers
  --skip-docker  Do not start Postgres via Docker (expects DB on localhost:5432)
  --help         Show this help

After startup:
  Frontend  http://localhost:5173
  API       http://localhost:5000
  Health    http://localhost:5000/healthz

Requires: Docker (for Postgres), .NET 8 SDK, Node.js 18+
EOF
}

log() {
  printf '==> %s\n' "$*"
}

fail() {
  printf 'error: %s\n' "$*" >&2
  exit 1
}

require_cmd() {
  command -v "$1" >/dev/null 2>&1 || fail "Missing required command: $1"
}

parse_args() {
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --build-only)
        BUILD_ONLY=true
        shift
        ;;
      --skip-docker)
        SKIP_DOCKER=true
        shift
        ;;
      --help|-h)
        usage
        exit 0
        ;;
      *)
        fail "Unknown option: $1 (try --help)"
        ;;
    esac
  done

  if [[ "${HC_SKIP_DOCKER:-}" == "1" ]]; then
    SKIP_DOCKER=true
  fi
}

generate_secret() {
  if command -v openssl >/dev/null 2>&1; then
    openssl rand -base64 48 | tr -d '\n'
  else
    fail "openssl is required to generate secrets for a new .env file"
  fi
}

ensure_env_file() {
  if [[ ! -f "$ENV_FILE" ]]; then
    log "Creating $ENV_FILE from .env.example"
    cp "$REPO_ROOT/.env.example" "$ENV_FILE"
  fi

  # shellcheck disable=SC1090
  set -a
  source "$ENV_FILE"
  set +a

  local updated=false

  if [[ "${JWT_SECRET:-}" == "replace-with-a-long-random-secret" || -z "${JWT_SECRET:-}" ]]; then
    JWT_SECRET="$(generate_secret)"
    if grep -q '^JWT_SECRET=' "$ENV_FILE"; then
      sed -i "s|^JWT_SECRET=.*|JWT_SECRET=${JWT_SECRET}|" "$ENV_FILE"
    else
      printf '\nJWT_SECRET=%s\n' "$JWT_SECRET" >>"$ENV_FILE"
    fi
    updated=true
  fi

  if [[ "${POSTGRES_PASSWORD:-}" == "replace-with-a-strong-password" || -z "${POSTGRES_PASSWORD:-}" ]]; then
    POSTGRES_PASSWORD="$(generate_secret)"
    if grep -q '^POSTGRES_PASSWORD=' "$ENV_FILE"; then
      sed -i "s|^POSTGRES_PASSWORD=.*|POSTGRES_PASSWORD=${POSTGRES_PASSWORD}|" "$ENV_FILE"
    else
      printf 'POSTGRES_PASSWORD=%s\n' "$POSTGRES_PASSWORD" >>"$ENV_FILE"
    fi
    updated=true
  fi

  if [[ "$updated" == true ]]; then
  # shellcheck disable=SC1090
    set -a
    source "$ENV_FILE"
    set +a
    log "Generated secrets in .env (local only, not committed)"
  fi

  [[ -n "${JWT_SECRET:-}" ]] || fail "JWT_SECRET is not set in .env"
  [[ ${#JWT_SECRET} -ge 32 ]] || fail "JWT_SECRET must be at least 32 characters"
  [[ -n "${POSTGRES_PASSWORD:-}" ]] || fail "POSTGRES_PASSWORD is not set in .env"
}

wait_for_postgres() {
  local attempts=30
  local i
  for ((i = 1; i <= attempts; i++)); do
    if docker compose -f "$REPO_ROOT/docker-compose.yml" exec -T postgres \
      pg_isready -U postgres -d homework_central >/dev/null 2>&1; then
      return 0
    fi
    sleep 1
  done
  fail "Postgres did not become ready within ${attempts}s"
}

start_postgres() {
  require_cmd docker
  if ! docker info >/dev/null 2>&1; then
    fail "Docker is not running. Start Docker Desktop (or the Docker daemon) and retry."
  fi

  log "Starting Postgres (Docker)"
  docker compose -f "$REPO_ROOT/docker-compose.yml" up -d postgres
  log "Waiting for Postgres to accept connections"
  wait_for_postgres
}

build_projects() {
  if [[ "${HC_SKIP_BUILD:-}" == "1" ]]; then
    log "Skipping build (HC_SKIP_BUILD=1)"
    return 0
  fi

  require_cmd dotnet
  log "Building API"
  dotnet build "$API_PROJECT" -c Debug

  require_cmd npm
  if [[ ! -d "$FRONTEND_DIR/node_modules" ]]; then
    log "Installing frontend dependencies"
    npm ci --prefix "$FRONTEND_DIR"
  else
    log "Frontend dependencies already installed"
  fi
}

export_backend_env() {
  export ASPNETCORE_ENVIRONMENT=Development
  export ASPNETCORE_URLS=http://localhost:5000
  export Jwt__Secret="$JWT_SECRET"
  export ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=homework_central;Username=postgres;Password=${POSTGRES_PASSWORD}"
}

run_stack() {
  export_backend_env

  log "Starting API on http://localhost:5000"
  dotnet run --project "$API_PROJECT" --no-build --urls http://localhost:5000 &
  BACKEND_PID=$!

  log "Starting frontend on http://localhost:5173"
  npm run dev --prefix "$FRONTEND_DIR" &
  FRONTEND_PID=$!

  cleanup() {
    log "Stopping dev servers"
    kill "$BACKEND_PID" "$FRONTEND_PID" 2>/dev/null || true
    wait "$BACKEND_PID" "$FRONTEND_PID" 2>/dev/null || true
  }
  trap cleanup EXIT INT TERM

  log "Dev stack is running"
  log "  Frontend: http://localhost:5173"
  log "  API:      http://localhost:5000"
  log "Press Ctrl+C to stop"

  wait "$BACKEND_PID" 2>/dev/null || true
  wait "$FRONTEND_PID" 2>/dev/null || true
}

main() {
  parse_args "$@"
  cd "$REPO_ROOT"

  require_cmd dotnet
  require_cmd npm

  ensure_env_file

  if [[ "$SKIP_DOCKER" == false ]]; then
    start_postgres
  else
    log "Skipping Docker Postgres (HC_SKIP_DOCKER / --skip-docker)"
  fi

  build_projects

  if [[ "$BUILD_ONLY" == true ]]; then
    log "Build complete (--build-only)"
    exit 0
  fi

  run_stack
}

main "$@"
