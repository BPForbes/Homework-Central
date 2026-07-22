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

# Include optional profiles so clamav/llm containers (if started) release the compose network.
compose_args=(-f "$COMPOSE_FILE" --env-file "$DEV_STACK_ENV_FILE" --profile antivirus --profile ai)
docker compose "${compose_args[@]}" down -v --remove-orphans

# Compose warns "Network … Resource is still in use" when something outside this
# down still holds homework-central_default. Volume wipe already succeeded; try a
# best-effort network remove, then continue — run-dev reuses the network either way.
network_name='homework-central_default'
if docker network inspect "$network_name" >/dev/null 2>&1; then
  if docker network rm "$network_name" >/dev/null 2>&1; then
    printf '==> Removed leftover Docker network %s\n' "$network_name"
  else
    printf '==> Docker network %s is still in use (harmless). run-dev will reuse it.\n' "$network_name"
  fi
fi

printf '==> Dev database volume removed. Run scripts/run-dev.sh to start fresh.\n'
