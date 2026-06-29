#!/usr/bin/env bash
# Stop the Homework Central local dev stack and free the Docker Postgres port.
#
# Usage:
#   scripts/stop-dev.sh
#   scripts/stop-dev.sh --help
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=scripts/dev-stack-lib.sh
source "$REPO_ROOT/scripts/dev-stack-lib.sh"

usage() {
  cat <<'EOF'
Homework Central - stop local dev stack

Usage:
  scripts/stop-dev.sh

Stops Docker Postgres started by scripts/run-dev.sh and frees its host port.
Account data is preserved in the pgdata Docker volume.

To wipe the database volume (removes registered accounts):
  scripts/reset-dev-db.sh --yes
  # or: docker compose down -v
EOF
}

if [[ "${1:-}" == "--help" || "${1:-}" == "-h" ]]; then
  usage
  exit 0
fi

if ! read_dev_stack_state || [[ "${DEV_STACK_STATE_managed_postgres:-}" != "1" ]]; then
  printf '==> No managed dev stack Postgres session found\n'
  exit 0
fi

printf '==> Stopping Docker Postgres on localhost:%s\n' "${DEV_STACK_STATE_postgres_port}"
stop_dev_stack
printf '==> Dev stack Postgres stopped (database volume preserved)\n'
