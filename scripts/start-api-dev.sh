#!/usr/bin/env bash
# Launch the API for local development.
#
# Local Postgres credentials are fixed: postgres / postgres
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
POSTGRES_HOST_PORT="5434"

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

read_jwt_secret() {
  [[ -f "$ENV_FILE" ]] || fail ".env not found at $ENV_FILE. Run scripts/run-dev.sh first."

  JWT_SECRET=""
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
      POSTGRES_HOST_PORT) POSTGRES_HOST_PORT="$value" ;;
    esac
  done <"$ENV_FILE"

  POSTGRES_HOST_PORT="$(trim_whitespace "$POSTGRES_HOST_PORT")"
  [[ -n "$POSTGRES_HOST_PORT" ]] || POSTGRES_HOST_PORT="5434"
  [[ -n "$JWT_SECRET" ]] || fail "JWT_SECRET is not set in .env"
}

read_jwt_secret

export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS=http://localhost:5000
export Jwt__Secret="$JWT_SECRET"
export HC_DEV_BYPASS=1
export ConnectionStrings__DefaultConnection="Host=localhost;Port=${POSTGRES_HOST_PORT};Database=homework_central;Username=${DEV_POSTGRES_USER};Password=${DEV_POSTGRES_PASSWORD}"

printf 'Homework Central API - http://localhost:5000\n'
printf 'Using Postgres user %s on localhost:%s (local dev)\n' "$DEV_POSTGRES_USER" "$POSTGRES_HOST_PORT"

cleanup_api() {
  if [[ "${HC_SKIP_DOCKER:-0}" != "1" ]]; then
    unregister_dev_stack_server
  fi
}
trap cleanup_api EXIT

if [[ "${HC_SKIP_DOCKER:-0}" != "1" ]]; then
  ensure_dev_postgres_running "$POSTGRES_HOST_PORT" || fail "Could not start Docker Postgres on localhost:${POSTGRES_HOST_PORT}. Run scripts/run-dev.sh or start Docker Desktop."
fi

"$REPO_ROOT/scripts/wait-and-open-browser.sh" "http://localhost:5000/" "API" 120 &
BROWSER_WAIT_PID=$!

cd "$REPO_ROOT"
set +e
dotnet run --project "$API_PROJECT" --no-build --no-launch-profile --urls http://localhost:5000 2> >(tee /tmp/hc-api-run-errors.log >&2)
api_status=$?
set -e

kill "$BROWSER_WAIT_PID" 2>/dev/null || true
wait "$BROWSER_WAIT_PID" 2>/dev/null || true

if [[ $api_status -ne 0 ]]; then
  if [[ -s /tmp/hc-api-run-errors.log ]]; then
    "$REPO_ROOT/scripts/open-api-error-page.sh" "API Errors" /tmp/hc-api-run-errors.log || true
  fi
  exit "$api_status"
fi
