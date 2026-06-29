#!/usr/bin/env bash
# Wipe the local Docker Postgres volume (removes all registered accounts and seed data).
#
# Usage:
#   scripts/reset-dev-db.sh
#   scripts/reset-dev-db.sh --yes
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ENV_FILE="$REPO_ROOT/.env"
COMPOSE_FILE="$REPO_ROOT/docker-compose.yml"

if [[ "${1:-}" != "--yes" ]]; then
  printf 'This removes the pgdata Docker volume and all local account data.\n'
  printf 'Re-run with --yes to continue: scripts/reset-dev-db.sh --yes\n'
  exit 1
fi

if [[ -f "$ENV_FILE" ]]; then
  # shellcheck disable=SC1090
  set -a
  source "$ENV_FILE"
  set +a
fi

export POSTGRES_PASSWORD="${POSTGRES_PASSWORD:-postgres}"
export POSTGRES_HOST_PORT="${POSTGRES_HOST_PORT:-5434}"

compose_args=(-f "$COMPOSE_FILE")
if [[ -f "$ENV_FILE" ]]; then
  compose_args+=(--env-file "$ENV_FILE")
fi
docker compose "${compose_args[@]}" down -v --remove-orphans
printf '==> Dev database volume removed. Run scripts/run-dev.sh to start fresh.\n'
