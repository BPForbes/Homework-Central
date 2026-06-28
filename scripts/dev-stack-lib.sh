#!/usr/bin/env bash
# Shared helpers for starting/stopping the local Homework Central dev stack.
# Source from other scripts in this directory; do not run directly.

DEV_STACK_REPO_ROOT="${DEV_STACK_REPO_ROOT:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"
DEV_STACK_STATE_FILE="$DEV_STACK_REPO_ROOT/.hc-dev-stack.state"
DEV_STACK_COMPOSE_FILE="$DEV_STACK_REPO_ROOT/docker-compose.yml"
DEV_STACK_ENV_FILE="$DEV_STACK_REPO_ROOT/.env"
DEV_STACK_POSTGRES_PASSWORD="postgres"

read_dev_stack_state() {
  local key value
  if [[ ! -f "$DEV_STACK_STATE_FILE" ]]; then
    return 1
  fi

  while IFS= read -r line || [[ -n "$line" ]]; do
    case "$line" in
      ''|\#*) continue ;;
      *=*)
        key="${line%%=*}"
        value="${line#*=}"
        printf -v "DEV_STACK_STATE_${key}" '%s' "$value"
        ;;
    esac
  done <"$DEV_STACK_STATE_FILE"
}

write_dev_stack_state() {
  local postgres_port="$1"
  local refcount="$2"
  cat >"$DEV_STACK_STATE_FILE" <<EOF
managed_postgres=1
postgres_port=${postgres_port}
refcount=${refcount}
EOF
}

stop_dev_stack_postgres() {
  local port="$1"
  if ! command -v docker >/dev/null 2>&1; then
    return 0
  fi

  export POSTGRES_PASSWORD="$DEV_STACK_POSTGRES_PASSWORD"
  export POSTGRES_HOST_PORT="$port"
  docker compose -f "$DEV_STACK_COMPOSE_FILE" --env-file "$DEV_STACK_ENV_FILE" stop postgres >/dev/null 2>&1 || true
}

postgres_host_check_dll() {
  printf '%s' "$DEV_STACK_REPO_ROOT/scripts/PostgresHostCheck/bin/Debug/net8.0/PostgresHostCheck.dll"
}

build_postgres_host_check_if_needed() {
  local dll project
  dll="$(postgres_host_check_dll)"
  if [[ -f "$dll" ]]; then
    return 0
  fi

  project="$DEV_STACK_REPO_ROOT/scripts/PostgresHostCheck/PostgresHostCheck.csproj"
  dotnet build "$project" -c Debug -v q >/dev/null
}

test_dev_postgres_connection() {
  local port="$1"
  local dll
  build_postgres_host_check_if_needed
  dll="$(postgres_host_check_dll)"
  [[ -f "$dll" ]] || return 1
  dotnet "$dll" "$port" >/dev/null 2>&1
}

start_dev_stack_postgres_container() {
  local port="$1"
  command -v docker >/dev/null 2>&1 || return 1
  docker info >/dev/null 2>&1 || return 1

  export POSTGRES_PASSWORD="$DEV_STACK_POSTGRES_PASSWORD"
  export POSTGRES_HOST_PORT="$port"
  docker compose -f "$DEV_STACK_COMPOSE_FILE" --env-file "$DEV_STACK_ENV_FILE" up -d postgres
}

wait_dev_postgres_ready() {
  local port="$1"
  local attempt
  for ((attempt = 1; attempt <= 30; attempt++)); do
    if test_dev_postgres_connection "$port"; then
      return 0
    fi
    sleep 1
  done
  return 1
}

ensure_dev_postgres_running() {
  local port="$1"
  if test_dev_postgres_connection "$port"; then
    return 0
  fi

  printf '==> Starting Docker Postgres on localhost:%s\n' "$port"
  start_dev_stack_postgres_container "$port" || return 1
  wait_dev_postgres_ready "$port" || return 1

  if ! read_dev_stack_state; then
    write_dev_stack_state "$port" 1
  fi
}

init_dev_stack_state() {
  local postgres_port="$1"
  local server_count="$2"

  if read_dev_stack_state && [[ "${DEV_STACK_STATE_managed_postgres:-}" == "1" ]]; then
    printf '==> Stopping previous dev stack Postgres session\n'
    stop_dev_stack_postgres "${DEV_STACK_STATE_postgres_port}"
  fi

  write_dev_stack_state "$postgres_port" "$server_count"
}

stop_dev_stack() {
  if read_dev_stack_state && [[ "${DEV_STACK_STATE_managed_postgres:-}" == "1" ]]; then
    stop_dev_stack_postgres "${DEV_STACK_STATE_postgres_port}"
  fi

  rm -f "$DEV_STACK_STATE_FILE"
}

release_dev_stack_postgres() {
  stop_dev_stack
  printf '==> Stopped Docker Postgres and freed localhost port\n'
}
