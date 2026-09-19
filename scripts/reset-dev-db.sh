#!/usr/bin/env bash
# Wipe the local Docker Postgres volume (removes all registered accounts and seed data).
#
# `docker compose down -v` stops the published 127.0.0.1 Postgres port. Run this
# before scripts/run-dev.sh, not in a second terminal while run-dev is up.
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
  printf 'Stop run-dev first, or run this before starting the API. A live API keeps retrying until Postgres is back.\n'
  printf 'Re-run with --yes to continue: scripts/reset-dev-db.sh --yes\n'
  exit 1
fi

ensure_dev_env_file 1

printf 'Stopping Docker Postgres (and optional profile containers) and removing the pgdata volume. Do not run this beside a live run-dev.\n'

# Include optional profiles so clamav/llm/minio containers (if started) release the compose network.
compose_args=(-f "$COMPOSE_FILE" --env-file "$DEV_STACK_ENV_FILE" --profile antivirus --profile ai --profile object-storage)
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

# compose down does not remove .hc-dev-stack.state. Leaving it makes the next
# run-dev treat this wipe as a live managed session and stop Postgres after it
# has already waited for the published port.
rm -f "$DEV_STACK_STATE_FILE"
rmdir "${DEV_STACK_STATE_FILE}.lock.d" 2>/dev/null || true

printf '==> Dev database volume removed. Run scripts/run-dev.sh to start fresh.\n'
