#!/usr/bin/env bash
# Wipe the local Docker Postgres volume (removes all registered accounts and seed data).
#
# Usage:
#   scripts/reset-dev-db.sh
#   scripts/reset-dev-db.sh --yes
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
COMPOSE_FILE="$REPO_ROOT/docker-compose.yml"

# shellcheck source=scripts/dev-stack-lib.sh
source "$REPO_ROOT/scripts/dev-stack-lib.sh"

if [[ "${1:-}" != "--yes" ]]; then
  printf 'This removes the pgdata Docker volume and all local account data.\n'
  printf 'Re-run with --yes to continue: scripts/reset-dev-db.sh --yes\n'
  exit 1
fi

ensure_dev_env_file 1

compose_args=(-f "$COMPOSE_FILE" --env-file "$DEV_STACK_ENV_FILE")
docker compose "${compose_args[@]}" down -v --remove-orphans
printf '==> Dev database volume removed. Run scripts/run-dev.sh to start fresh.\n'
