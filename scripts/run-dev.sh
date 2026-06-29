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
# shellcheck source=scripts/dev-stack-lib.sh
source "$REPO_ROOT/scripts/dev-stack-lib.sh"
API_PROJECT="$REPO_ROOT/backend/HomeworkCentral.Api/HomeworkCentral.Api.csproj"
POSTGRES_HOST_CHECK_PROJECT="$REPO_ROOT/scripts/PostgresHostCheck/PostgresHostCheck.csproj"
POSTGRES_HOST_CHECK_DLL="$REPO_ROOT/scripts/PostgresHostCheck/bin/Debug/net8.0/PostgresHostCheck.dll"
FRONTEND_DIR="$REPO_ROOT/frontend"
ENV_FILE="$REPO_ROOT/.env"
DEV_POSTGRES_USER="postgres"
DEV_POSTGRES_PASSWORD="postgres"
DEV_POSTGRES_HOST_PORT="5434"
DEV_POSTGRES_HOST_PORT_MIN=5434
DEV_POSTGRES_HOST_PORT_MAX=5450
BUILD_ONLY=false
SKIP_DOCKER=false
JWT_SECRET=""
POSTGRES_PASSWORD=""
POSTGRES_HOST_PORT="5434"

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

Stop:
  scripts/stop-dev.sh
  Ctrl+C in this terminal also stops Docker Postgres and frees its port.

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

loopback_port_in_use() {
  local port="$1"

  case "$(uname -s)" in
    MINGW*|MSYS*|CYGWIN*)
      if command -v powershell.exe >/dev/null 2>&1; then
        powershell.exe -NoProfile -Command "
          \$c = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue |
            Where-Object { \$_.LocalAddress -in @('127.0.0.1', '::1') } |
            Select-Object -First 1
          if (\$null -ne \$c) { exit 0 } else { exit 1 }
        " >/dev/null 2>&1
        return $?
      fi
      netstat -ano 2>/dev/null | grep LISTENING | grep -E "127\.0\.0\.1:${port}[[:space:]]" >/dev/null && return 0
      netstat -ano 2>/dev/null | grep LISTENING | grep -E "\[::1\]:${port}[[:space:]]" >/dev/null && return 0
      return 1
      ;;
    *)
      return 1
      ;;
  esac
}

find_free_postgres_host_port() {
  local port
  for ((port = DEV_POSTGRES_HOST_PORT_MIN; port <= DEV_POSTGRES_HOST_PORT_MAX; port++)); do
    if loopback_port_in_use "$port"; then
      continue
    fi
    printf '%s' "$port"
    return 0
  done
  return 1
}

resolve_postgres_host_port() {
  if ! loopback_port_in_use "$POSTGRES_HOST_PORT"; then
    return 0
  fi

  local free_port
  log "Port ${POSTGRES_HOST_PORT} is bound on 127.0.0.1 by another PostgreSQL install (localhost would not reach Docker)"
  free_port="$(find_free_postgres_host_port || true)"
  [[ -n "$free_port" ]] || fail "No free Postgres host port found between ${DEV_POSTGRES_HOST_PORT_MIN} and ${DEV_POSTGRES_HOST_PORT_MAX}"

  log "Using POSTGRES_HOST_PORT=${free_port} instead"
  POSTGRES_HOST_PORT="$free_port"
  set_env_var "POSTGRES_HOST_PORT" "$POSTGRES_HOST_PORT"
}

set_compose_env() {
  export POSTGRES_PASSWORD="$DEV_POSTGRES_PASSWORD"
  export POSTGRES_HOST_PORT
}

invoke_postgres_admin_sql() {
  docker compose -f "$REPO_ROOT/docker-compose.yml" --env-file "$ENV_FILE" exec -T postgres \
    sh -c "PGPASSWORD='$DEV_POSTGRES_PASSWORD' psql -h 127.0.0.1 -U $DEV_POSTGRES_USER -d postgres -v ON_ERROR_STOP=1 -c \"$1\"" >/dev/null 2>&1
}

get_postgres_published_port() {
  local raw
  raw="$(docker compose -f "$REPO_ROOT/docker-compose.yml" --env-file "$ENV_FILE" port postgres 5432 2>/dev/null | tr -d '\r\n')"
  if [[ -z "$raw" ]]; then
    return 1
  fi
  if [[ "$raw" =~ :([0-9]+)$ ]]; then
    printf '%s' "${BASH_REMATCH[1]}"
    return 0
  fi
  return 1
}

test_postgres_host_connection() {
  local published
  local attempt
  local output
  local status

  published="$(get_postgres_published_port || true)"
  if [[ -n "$published" && "$published" != "$POSTGRES_HOST_PORT" ]]; then
    log "Docker Postgres is not published on localhost:${POSTGRES_HOST_PORT} (container maps to ${published})"
    return 1
  fi

  if [[ ! -f "$POSTGRES_HOST_CHECK_DLL" ]]; then
    build_postgres_host_check
  fi

  for ((attempt = 1; attempt <= 10; attempt++)); do
    output="$(dotnet "$POSTGRES_HOST_CHECK_DLL" "$POSTGRES_HOST_PORT" 2>&1)"
    status=$?
    if [[ $status -eq 0 ]]; then
      return 0
    fi
    sleep 1
  done

  log "Cannot connect to homework_central on localhost:${POSTGRES_HOST_PORT} from the host"
  if [[ -n "$output" ]]; then
    printf '       %s\n' "$output"
  fi
  return 1
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

prepare_homework_central_database() {
  local exists

  if ! repair_postgres_collation; then
    return 1
  fi

  exists="$(docker compose -f "$REPO_ROOT/docker-compose.yml" --env-file "$ENV_FILE" exec -T postgres \
    sh -c "PGPASSWORD='$DEV_POSTGRES_PASSWORD' psql -h 127.0.0.1 -U $DEV_POSTGRES_USER -d postgres -tAc \"SELECT 1 FROM pg_database WHERE datname = 'homework_central'\"" \
    2>/dev/null | tr -d '\r\n')"
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

  return 0
}

start_postgres_container() {
  local published
  published="$(get_postgres_published_port || true)"

  if [[ -n "$published" && "$published" != "$POSTGRES_HOST_PORT" ]]; then
    log "Recreating Postgres container for localhost:${POSTGRES_HOST_PORT}"
    docker compose -f "$REPO_ROOT/docker-compose.yml" --env-file "$ENV_FILE" up -d --force-recreate postgres
  else
    docker compose -f "$REPO_ROOT/docker-compose.yml" --env-file "$ENV_FILE" up -d postgres
  fi
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

  if ! prepare_homework_central_database; then
    log "Postgres volume is unhealthy (collation mismatch); recreating"
    reset_postgres_volume
    start_postgres_container
    log "Waiting for Postgres to accept connections"
    wait_for_postgres

    if ! prepare_homework_central_database; then
      fail "Failed to prepare homework_central inside the Docker Postgres container"
    fi
  fi

  if ! test_postgres_host_connection; then
    fail "Failed to reach homework_central on localhost:${POSTGRES_HOST_PORT}. If another PostgreSQL install owns that port, pick a free port in .env (for example POSTGRES_HOST_PORT=5434), then run: docker compose down -v && scripts/run-dev.sh"
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
  POSTGRES_HOST_PORT="5434"

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
  [[ -n "$POSTGRES_HOST_PORT" ]] || POSTGRES_HOST_PORT="5434"
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

  if [[ "$POSTGRES_HOST_PORT" == "5432" || "$POSTGRES_HOST_PORT" == "5433" ]]; then
    log "Using POSTGRES_HOST_PORT=${DEV_POSTGRES_HOST_PORT} (avoids local PostgreSQL on 5432/5433)"
    POSTGRES_HOST_PORT="$DEV_POSTGRES_HOST_PORT"
    set_env_var "POSTGRES_HOST_PORT" "$POSTGRES_HOST_PORT"
    updated=true
  fi

  if [[ "$updated" == true ]]; then
    read_env_file
    log "Generated secrets in .env (local only, not committed)"
  fi

  resolve_postgres_host_port

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

build_postgres_host_check() {
  dotnet build "$POSTGRES_HOST_CHECK_PROJECT" -c Debug -v q >/dev/null
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
    api_build_log="$(mktemp /tmp/hc-api-build-errors-XXXXXX.log)"
    if ! dotnet build "$API_PROJECT" -c Debug 2>&1 | tee "$api_build_log"; then
      "$REPO_ROOT/scripts/open-api-error-page.sh" "API Build Errors" "$api_build_log" || true
      rm -f "$api_build_log"
      fail "dotnet build failed"
    fi
    rm -f "$api_build_log"
  fi

  build_postgres_host_check

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
  if [[ "$SKIP_DOCKER" == false ]]; then
    init_dev_stack_state "$POSTGRES_HOST_PORT" 2
  fi

  log "Starting API on http://localhost:5000"
  if [[ "$SKIP_DOCKER" == true ]]; then
    HC_SKIP_DOCKER=1 HC_DEV_BYPASS=1 "$REPO_ROOT/scripts/start-api-dev.sh" &
  else
    HC_SKIP_DOCKER=0 HC_DEV_STACK_PREREGISTERED=1 HC_DEV_BYPASS=1 "$REPO_ROOT/scripts/start-api-dev.sh" &
  fi
  BACKEND_PID=$!

  log "Starting frontend on http://localhost:5173"
  VITE_HC_DEV_BYPASS=true npm run dev --prefix "$FRONTEND_DIR" &
  FRONTEND_PID=$!

  "$REPO_ROOT/scripts/wait-and-open-browser.sh" "http://localhost:5000/" "API" 120 &
  API_BROWSER_PID=$!
  "$REPO_ROOT/scripts/wait-and-open-browser.sh" "http://localhost:5173/devlogin" "Frontend" 120 &
  FRONTEND_BROWSER_PID=$!

  cleanup() {
    log "Stopping dev servers"
    kill "$BACKEND_PID" "$FRONTEND_PID" "$API_BROWSER_PID" "$FRONTEND_BROWSER_PID" 2>/dev/null || true
    wait "$BACKEND_PID" "$FRONTEND_PID" 2>/dev/null || true
    if [[ "$SKIP_DOCKER" == false ]]; then
      unregister_dev_stack_server
    fi
  }
  trap cleanup EXIT INT TERM

  log "Dev stack is running"
  log "  Frontend: http://localhost:5173/devlogin"
  log "  API:      http://localhost:5000"
  if [[ "$SKIP_DOCKER" == false ]]; then
    log "  Postgres: localhost:${POSTGRES_HOST_PORT} (Docker; stops on exit)"
  fi
  log "Press Ctrl+C to stop servers and free the Postgres port"
  log "Or run: scripts/stop-dev.sh"

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
