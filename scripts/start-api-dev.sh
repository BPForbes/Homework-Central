#!/usr/bin/env bash
# Launch the API for local development using secrets from .env.
#
# Usage:
#   scripts/start-api-dev.sh
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
API_PROJECT="$REPO_ROOT/backend/HomeworkCentral.Api/HomeworkCentral.Api.csproj"
ENV_FILE="$REPO_ROOT/.env"
JWT_SECRET=""
POSTGRES_PASSWORD=""
POSTGRES_HOST_PORT="5432"

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

read_dev_env() {
  [[ -f "$ENV_FILE" ]] || fail ".env not found at $ENV_FILE. Run scripts/run-dev.sh first."

  JWT_SECRET=""
  POSTGRES_PASSWORD=""
  POSTGRES_HOST_PORT="5432"

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
  [[ -n "$POSTGRES_HOST_PORT" ]] || POSTGRES_HOST_PORT="5432"
  [[ -n "$JWT_SECRET" ]] || fail "JWT_SECRET is not set in .env"
  [[ -n "$POSTGRES_PASSWORD" ]] || fail "POSTGRES_PASSWORD is not set in .env"
}

postgres_connection_string() {
  local quoted_password
  quoted_password="${POSTGRES_PASSWORD//\'/\'\'}"
  printf "Host=localhost;Port=%s;Database=homework_central;Username=postgres;Password='%s'" \
    "$POSTGRES_HOST_PORT" "$quoted_password"
}

read_dev_env

export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS=http://localhost:5000
export Jwt__Secret="$JWT_SECRET"
export ConnectionStrings__DefaultConnection
ConnectionStrings__DefaultConnection="$(postgres_connection_string)"

printf 'Homework Central API - http://localhost:5000\n'
printf 'Using database credentials from .env\n'

cd "$REPO_ROOT"
dotnet run --project "$API_PROJECT" --no-build --no-launch-profile --urls http://localhost:5000
