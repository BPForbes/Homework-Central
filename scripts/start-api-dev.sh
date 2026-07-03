#!/usr/bin/env bash
# Launch the API for local development.
#
# Local Postgres credentials are fixed: postgres / postgres
# Sets HC_DEV_BYPASS=1 so localhost dev auth endpoints and the styled 403 root page are enabled.
#
# Usage:
#   scripts/start-api-dev.sh
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=scripts/dev-stack-lib.sh
source "$REPO_ROOT/scripts/dev-stack-lib.sh"
API_PROJECT="$REPO_ROOT/backend/HomeworkCentral.Api/HomeworkCentral.Api.csproj"
ENV_FILE="$REPO_ROOT/.env"
DEV_POSTGRES_USER="postgres"
DEV_POSTGRES_PASSWORD="postgres"
JWT_SECRET=""
FCAPTCHA_SECRET=""
POSTGRES_HOST_PORT="5434"
FCAPTCHA_HOST_PORT="${DEV_STACK_FCAPTCHA_HOST_PORT}"

fail() {
  printf 'error: %s\n' "$*" >&2
  exit 1
}

trim_whitespace() {
  local value="$1"
  value="${value#"${value%%[![:space:]]*}"}"
  value="${value%"${value##*[![:space:]]}"}"
  printf '%s' "$value"
}

read_env_secrets() {
  [[ -f "$ENV_FILE" ]] || fail ".env not found at $ENV_FILE. Run scripts/run-dev.sh first."

  JWT_SECRET=""
  FCAPTCHA_SECRET=""
  POSTGRES_HOST_PORT="5434"
  FCAPTCHA_HOST_PORT="${DEV_STACK_FCAPTCHA_HOST_PORT}"

  while IFS= read -r line || [[ -n "$line" ]]; do
    case "$line" in
      ''|\#*) continue ;;
    esac

    local key="${line%%=*}"
    local value="${line#*=}"
    key="$(trim_whitespace "$key")"

    case "$key" in
      JWT_SECRET) JWT_SECRET="$value" ;;
      FCAPTCHA_SECRET) FCAPTCHA_SECRET="$value" ;;
      POSTGRES_HOST_PORT) POSTGRES_HOST_PORT="$value" ;;
      FCAPTCHA_HOST_PORT) FCAPTCHA_HOST_PORT="$value" ;;
    esac
  done <"$ENV_FILE"

  POSTGRES_HOST_PORT="$(trim_whitespace "$POSTGRES_HOST_PORT")"
  [[ -n "$POSTGRES_HOST_PORT" ]] || POSTGRES_HOST_PORT="5434"
  FCAPTCHA_HOST_PORT="$(trim_whitespace "$FCAPTCHA_HOST_PORT")"
  [[ -n "$FCAPTCHA_HOST_PORT" ]] || FCAPTCHA_HOST_PORT="${DEV_STACK_FCAPTCHA_HOST_PORT}"
  [[ -n "$JWT_SECRET" ]] || fail "JWT_SECRET is not set in .env"
  [[ -n "$FCAPTCHA_SECRET" ]] || fail "FCAPTCHA_SECRET is not set in .env"
}

read_env_secrets

export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS=http://localhost:5000
export Jwt__Secret="$JWT_SECRET"
export FCaptcha__Secret="$FCAPTCHA_SECRET"
export FCaptcha__ServerUrl="http://localhost:${FCAPTCHA_HOST_PORT}"
export FCaptcha__PublicUrl="http://localhost:${FCAPTCHA_HOST_PORT}"
# Enables DevAuthController, dev seed data, and the styled localhost root page.
export HC_DEV_BYPASS=1
export ConnectionStrings__MasterConnection="Host=localhost;Port=${POSTGRES_HOST_PORT};Database=homework_central_master;Username=${DEV_POSTGRES_USER};Password=${DEV_POSTGRES_PASSWORD}"
export ConnectionStrings__PostgresAdmin="Host=localhost;Port=${POSTGRES_HOST_PORT};Database=postgres;Username=${DEV_POSTGRES_USER};Password=${DEV_POSTGRES_PASSWORD}"
export Tenancy__ClusterEnvironment=dev

printf 'Homework Central API - http://localhost:5000\n'
printf 'Using Postgres user %s on localhost:%s (local dev)\n' "$DEV_POSTGRES_USER" "$POSTGRES_HOST_PORT"
printf 'Note: first-run EF logs about missing __EF*MigrationsHistory tables are normal.\n'
printf 'Note: first startup provisions ~70 persona databases and may take several minutes.\n'

cleanup_api() {
  if [[ "${HC_SKIP_DOCKER:-0}" != "1" ]]; then
    unregister_dev_stack_server
  fi
}
trap cleanup_api EXIT

if [[ "${HC_SKIP_DOCKER:-0}" != "1" ]]; then
  ensure_dev_postgres_running "$POSTGRES_HOST_PORT" || fail "Could not start Docker Postgres on localhost:${POSTGRES_HOST_PORT}. Run scripts/run-dev.sh or start Docker Desktop."
  ensure_dev_fcaptcha_running "$FCAPTCHA_HOST_PORT" || fail "Could not start the FCaptcha Docker container on localhost:${FCAPTCHA_HOST_PORT}. Run scripts/run-dev.sh or start Docker Desktop."
fi

if [[ "${HC_SKIP_BROWSER_OPEN:-0}" != "1" ]]; then
  "$REPO_ROOT/scripts/wait-and-open-browser.sh" "http://localhost:5000/" "API" 300 &
  BROWSER_WAIT_PID=$!
fi

cd "$REPO_ROOT"
API_ERROR_LOG="$(mktemp /tmp/hc-api-run-errors-XXXXXX.log)"

cleanup_on_exit() {
  rm -f "$API_ERROR_LOG"
  cleanup_api
}
trap cleanup_on_exit EXIT

set +e
dotnet run --project "$API_PROJECT" --no-build --no-launch-profile --urls http://localhost:5000 2> >(tee "$API_ERROR_LOG" >&2)
api_status=$?
set -e

kill "${BROWSER_WAIT_PID:-}" 2>/dev/null || true
wait "${BROWSER_WAIT_PID:-}" 2>/dev/null || true

if [[ $api_status -ne 0 ]]; then
  if [[ -s "$API_ERROR_LOG" ]]; then
    "$REPO_ROOT/scripts/open-api-error-page.sh" "API Errors" "$API_ERROR_LOG" || true
  fi
  exit "$api_status"
fi
