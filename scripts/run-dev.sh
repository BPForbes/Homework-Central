#!/usr/bin/env bash
# Start the full Homework Central local dev stack (Postgres, API, frontend).
#
# Usage:
#   scripts/run-dev.sh              # build + run everything
#   scripts/run-dev.sh --build-only # compile only (no servers)
#   scripts/run-dev.sh --help
#
# Environment:
#   HC_SKIP_DOTNET_BUILD=1  Skip dotnet build only (set by IDE after a fresh compile)
#   HC_SKIP_DOCKER=1        Skip starting Postgres via Docker (use existing DB)
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
API_PROJECT="$REPO_ROOT/backend/HomeworkCentral.Api/HomeworkCentral.Api.csproj"
FRONTEND_DIR="$REPO_ROOT/frontend"
ENV_FILE="$REPO_ROOT/.env"
DEV_POSTGRES_USER="postgres"
DEV_POSTGRES_PASSWORD="postgres"
DEV_POSTGRES_HOST_PORT="5433"
BUILD_ONLY=false
SKIP_DOCKER=false
JWT_SECRET=""
POSTGRES_PASSWORD=""
POSTGRES_HOST_PORT="5433"

usage() {
  cat <<'EOF'
Homework Central - local dev stack

Usage:
  scripts/run-dev.sh [options]

Options:
  --build-only   Compile the API and install frontend deps; do not start servers
  --skip-docker  Do not start Postgres via Docker (expects DB on localhost)
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
    # URL-safe base64 avoids special characters breaking connection strings and shells.
    openssl rand -base64 48 | tr '+/' '-_' | tr -d '=\n'
  else
    fail "openssl is required to generate secrets for a new .env file"
  fi
}

set_compose_env() {
  export POSTGRES_PASSWORD="$DEV_POSTGRES_PASSWORD"
  export POSTGRES_HOST_PORT
}

invoke_postgres_admin_sql() {
  docker compose -f "$REPO_ROOT/docker-compose.yml" --env-file "$ENV_FILE" exec -T postgres \
    sh -c "PGPASSWORD='$DEV_POSTGRES_PASSWORD' psql -h 127.0.0.1 -U $DEV_POSTGRES_USER -d postgres -v ON_ERROR_STOP=1 -c \"$1\""
}

test_postgres_from_host() {
  docker compose -f "$REPO_ROOT/docker-compose.yml" --env-file "$ENV_FILE" exec -T postgres \
    sh -c "PGPASSWORD='$DEV_POSTGRES_PASSWORD' psql -h host.docker.internal -p $POSTGRES_HOST_PORT -U $DEV_POSTGRES_USER -d homework_central -tAc 'SELECT 1'" >/dev/null 2>&1
}

test_postgres_auth() {
  local database="${1:-postgres}"
  docker compose -f "$REPO_ROOT/docker-compose.yml" --env-file "$ENV_FILE" exec -T postgres \
    sh -c "PGPASSWORD='$DEV_POSTGRES_PASSWORD' psql -h 127.0.0.1 -p 5432 -U $DEV_POSTGRES_USER -d $database -tAc 'SELECT 1'" >/dev/null 2>&1
}

repair_postgres_collation() {
  log "Refreshing Postgres collation versions (fixes stale Docker volumes)"
  invoke_postgres_admin_sql "ALTER DATABASE template1 REFRESH COLLATION VERSION;" || return 1
  invoke_postgres_admin_sql "ALTER DATABASE postgres REFRESH COLLATION VERSION;" || return 1
  if [[ "$(docker compose -f "$REPO_ROOT/docker-compose.yml" --env-file "$ENV_FILE" exec -T postgres \
    sh -c "PGPASSWORD='$DEV_POSTGRES_PASSWORD' psql -h 127.0.0.1 -U $DEV_POSTGRES_USER -d postgres -tAc \"SELECT 1 FROM pg_database WHERE datname = 'homework_central'\"" \
    | tr -d '\r\n')" == "1" ]]; then
    invoke_postgres_admin_sql "ALTER DATABASE homework_central REFRESH COLLATION VERSION;" || return 1
  fi
}

ensure_homework_central_database() {
  local exists

  if ! repair_postgres_collation; then
    return 1
  fi

  exists="$(docker compose -f "$REPO_ROOT/docker-compose.yml" --env-file "$ENV_FILE" exec -T postgres \
    sh -c "PGPASSWORD='$DEV_POSTGRES_PASSWORD' psql -h 127.0.0.1 -U $DEV_POSTGRES_USER -d postgres -tAc \"SELECT 1 FROM pg_database WHERE datname = 'homework_central'\"" \
    | tr -d '\r\n')"
  if [[ "$exists" != "1" ]]; then
    log "Creating homework_central database"
    if ! invoke_postgres_admin_sql "CREATE DATABASE homework_central;"; then
      return 1
    fi
    invoke_postgres_admin_sql "ALTER DATABASE homework_central REFRESH COLLATION VERSION;"
  fi

  if ! test_postgres_auth homework_central; then
    return 1
  fi

  if test_postgres_from_host; then
    return 0
  fi

  log "Docker Postgres is not reachable at localhost:${POSTGRES_HOST_PORT} from the host (another PostgreSQL may own that port)"
  return 1
}

start_postgres_container() {
  docker compose -f "$REPO_ROOT/docker-compose.yml" --env-file "$ENV_FILE" up -d postgres
}

reset_postgres_volume() {
  log "Recreating Postgres Docker volume (reset to postgres/postgres credentials)"
  docker compose -f "$REPO_ROOT/docker-compose.yml" --env-file "$ENV_FILE" down -v --remove-orphans >/dev/null
}

ensure_postgres_ready() {
  set_compose_env

  start_postgres_container
  log "Waiting for Postgres to accept connections"
  wait_for_postgres

  if ! test_postgres_auth postgres; then
    log "Postgres rejected postgres/postgres (stale Docker volume with a different password)"
    reset_postgres_volume
    start_postgres_container
    log "Waiting for Postgres to accept connections"
    wait_for_postgres
    if ! test_postgres_auth postgres; then
      fail "Postgres password verification failed after recreating the Docker volume"
    fi
  fi

  if ensure_homework_central_database; then
    return 0
  fi

  log "Postgres volume is unhealthy (collation mismatch); recreating"
  reset_postgres_volume
  start_postgres_container
  log "Waiting for Postgres to accept connections"
  wait_for_postgres

  if ! ensure_homework_central_database; then
    fail "Failed to prepare homework_central on localhost:${POSTGRES_HOST_PORT}. If port 5432 is in use by another PostgreSQL install, set POSTGRES_HOST_PORT=5433 in .env and run: docker compose down -v"
  fi
}

trim_whitespace() {
  local value="$1"
  value="${value#"${value%%[![:space:]]*}"}"
  value="${value%"${value##*[![:space:]]}"}"
  printf '%s' "$value"
}

read_env_file() {
  JWT_SECRET=""
  POSTGRES_PASSWORD=""
  POSTGRES_HOST_PORT="5433"

  while IFS= read -r line || [[ -n "$line" ]]; do
    case "$line" in
      ''|\#*) continue ;;
    esac

    local key="${line%%=*}"
    local value="${line#*=}"
    key="$(trim_whitespace "$key")"

    case "$key" in
      JWT_SECRET) JWT_SECRET="$value" ;;
      POSTGRES_PASSWORD) POSTGRES_PASSWORD="$value" ;;
      POSTGRES_HOST_PORT) POSTGRES_HOST_PORT="$value" ;;
    esac
  done <"$ENV_FILE"

  POSTGRES_HOST_PORT="$(trim_whitespace "$POSTGRES_HOST_PORT")"
  [[ -n "$POSTGRES_HOST_PORT" ]] || POSTGRES_HOST_PORT="5433"
}

set_env_var() {
  local key="$1"
  local value="$2"
  local tmp replaced=false
  tmp="$(mktemp)"

  while IFS= read -r line || [[ -n "$line" ]]; do
    if [[ "$line" == "${key}="* ]]; then
      printf '%s=%s\n' "$key" "$value" >>"$tmp"
      replaced=true
    else
      printf '%s\n' "$line" >>"$tmp"
    fi
  done <"$ENV_FILE"

  if [[ "$replaced" == false ]]; then
    printf '%s=%s\n' "$key" "$value" >>"$tmp"
  fi

  mv "$tmp" "$ENV_FILE"
}

ensure_env_file() {
  if [[ ! -f "$ENV_FILE" ]]; then
    log "Creating $ENV_FILE from .env.example"
    cp "$REPO_ROOT/.env.example" "$ENV_FILE"
  fi

  read_env_file

  local updated=false

  if [[ "$JWT_SECRET" == "replace-with-a-long-random-secret" || -z "$JWT_SECRET" ]]; then
    JWT_SECRET="$(generate_secret)"
    set_env_var "JWT_SECRET" "$JWT_SECRET"
    updated=true
  fi

  if [[ "$POSTGRES_PASSWORD" != "$DEV_POSTGRES_PASSWORD" ]]; then
    POSTGRES_PASSWORD="$DEV_POSTGRES_PASSWORD"
    set_env_var "POSTGRES_PASSWORD" "$POSTGRES_PASSWORD"
    updated=true
  fi

  if [[ "$POSTGRES_HOST_PORT" == "5432" ]]; then
    log "Using POSTGRES_HOST_PORT=5433 (port 5432 is often used by a local PostgreSQL install)"
    POSTGRES_HOST_PORT="$DEV_POSTGRES_HOST_PORT"
    set_env_var "POSTGRES_HOST_PORT" "$POSTGRES_HOST_PORT"
    updated=true
  fi

  if [[ "$updated" == true ]]; then
    read_env_file
    log "Generated secrets in .env (local only, not committed)"
  fi

  [[ -n "$JWT_SECRET" ]] || fail "JWT_SECRET is not set in .env"
  [[ ${#JWT_SECRET} -ge 32 ]] || fail "JWT_SECRET must be at least 32 characters"
  [[ -n "$POSTGRES_PASSWORD" ]] || fail "POSTGRES_PASSWORD is not set in .env"
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

  log "Starting Postgres (Docker) on localhost:${POSTGRES_HOST_PORT}"
  ensure_postgres_ready
}

build_projects() {
  local skip_dotnet=false
  if [[ "${HC_SKIP_DOTNET_BUILD:-}" == "1" || "${HC_SKIP_BUILD:-}" == "1" ]]; then
    log "Skipping API build (HC_SKIP_DOTNET_BUILD=1)"
    skip_dotnet=true
  fi

  if [[ "$skip_dotnet" == false ]]; then
    require_cmd dotnet
    log "Building API"
    dotnet build "$API_PROJECT" -c Debug
  fi

  require_cmd npm
  if [[ ! -d "$FRONTEND_DIR/node_modules" ]]; then
    log "Installing frontend dependencies"
    npm ci --prefix "$FRONTEND_DIR"
  else
    log "Frontend dependencies already installed"
  fi
}

supervise_children() {
  local backend_pid=$1
  local frontend_pid=$2
  local exit_code=0

  while kill -0 "$backend_pid" 2>/dev/null && kill -0 "$frontend_pid" 2>/dev/null; do
    sleep 0.2
  done

  if kill -0 "$backend_pid" 2>/dev/null; then
    kill "$backend_pid" 2>/dev/null || true
    wait "$backend_pid" 2>/dev/null || true
    wait "$frontend_pid" 2>/dev/null || exit_code=$?
  elif kill -0 "$frontend_pid" 2>/dev/null; then
    kill "$frontend_pid" 2>/dev/null || true
    wait "$frontend_pid" 2>/dev/null || true
    wait "$backend_pid" 2>/dev/null || exit_code=$?
  else
    wait "$backend_pid" 2>/dev/null || exit_code=$?
    if [[ $exit_code -eq 0 ]]; then
      wait "$frontend_pid" 2>/dev/null || exit_code=$?
    else
      wait "$frontend_pid" 2>/dev/null || true
    fi
  fi

  trap - EXIT INT TERM
  return "$exit_code"
}

run_stack() {
  log "Starting API on http://localhost:5000"
  "$REPO_ROOT/scripts/start-api-dev.sh" &
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

  supervise_children "$BACKEND_PID" "$FRONTEND_PID"
  local status=$?
  if [[ $status -ne 0 ]]; then
    exit "$status"
  fi
}

main() {
  parse_args "$@"
  cd "$REPO_ROOT"

  require_cmd dotnet
  require_cmd npm

  ensure_env_file
  build_projects

  if [[ "$BUILD_ONLY" == true ]]; then
    log "Build complete (--build-only)"
    exit 0
  fi

  if [[ "$SKIP_DOCKER" == false ]]; then
    start_postgres
  else
    log "Skipping Docker Postgres (HC_SKIP_DOCKER / --skip-docker)"
  fi

  run_stack
}

main "$@"
