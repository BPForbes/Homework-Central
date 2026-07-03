#!/usr/bin/env bash
# Shared helpers for starting/stopping the local Homework Central dev stack.
# Source from other scripts in this directory; do not run directly.

DEV_STACK_REPO_ROOT="${DEV_STACK_REPO_ROOT:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"
DEV_STACK_STATE_FILE="$DEV_STACK_REPO_ROOT/.hc-dev-stack.state"
DEV_STACK_COMPOSE_FILE="$DEV_STACK_REPO_ROOT/docker-compose.yml"
DEV_STACK_ENV_FILE="$DEV_STACK_REPO_ROOT/.env"
DEV_STACK_POSTGRES_PASSWORD="postgres"
DEV_STACK_FCAPTCHA_HOST_PORT="3010"
DEV_STACK_SERVER_REGISTERED=0

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

with_dev_stack_lock() {
  local lock_dir="${DEV_STACK_STATE_FILE}.lock.d"
  local attempt rc restore_errexit=0
  for ((attempt = 0; attempt < 150; attempt++)); do
    if mkdir "$lock_dir" 2>/dev/null; then
      case "$-" in
        *e*) restore_errexit=1; set +e ;;
      esac
      "$@"
      rc=$?
      if (( restore_errexit )); then
        set -e
      fi
      rmdir "$lock_dir" 2>/dev/null || true
      return $rc
    fi
    sleep 0.1
  done
  printf 'error: timed out waiting for dev stack state lock\n' >&2
  return 1
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
  printf '%s' "$DEV_STACK_REPO_ROOT/scripts/PostgresHostCheck/bin/Debug/net10.0/PostgresHostCheck.dll"
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

# FCaptcha (see docker-compose.yml's `fcaptcha` service) is stateless — no volume, no credentials
# to reset — so unlike Postgres above it doesn't need refcounted start/stop bookkeeping in
# .hc-dev-stack.state; it's simply started alongside Postgres and stopped whenever Postgres is.
start_dev_stack_fcaptcha_container() {
  local port="$1"
  command -v docker >/dev/null 2>&1 || return 1
  docker info >/dev/null 2>&1 || return 1

  export FCAPTCHA_HOST_PORT="$port"
  docker compose -f "$DEV_STACK_COMPOSE_FILE" --env-file "$DEV_STACK_ENV_FILE" up -d fcaptcha
}

test_dev_fcaptcha_connection() {
  local port="$1"
  (exec 3<>"/dev/tcp/127.0.0.1/${port}") 2>/dev/null
}

wait_dev_fcaptcha_ready() {
  local port="$1"
  local attempt
  for ((attempt = 1; attempt <= 30; attempt++)); do
    if test_dev_fcaptcha_connection "$port"; then
      return 0
    fi
    sleep 1
  done
  return 1
}

ensure_dev_fcaptcha_running() {
  local port="$1"
  if test_dev_fcaptcha_connection "$port"; then
    return 0
  fi

  printf '==> Starting Docker FCaptcha on localhost:%s\n' "$port"
  start_dev_stack_fcaptcha_container "$port" || return 1
  wait_dev_fcaptcha_ready "$port" || return 1
}

stop_dev_stack_fcaptcha() {
  command -v docker >/dev/null 2>&1 || return 0
  docker compose -f "$DEV_STACK_COMPOSE_FILE" --env-file "$DEV_STACK_ENV_FILE" stop fcaptcha >/dev/null 2>&1 || true
}

_join_dev_stack_if_managed() {
  local port="$1"
  if [[ "${HC_DEV_STACK_PREREGISTERED:-0}" == "1" ]]; then
    return 0
  fi

  if ! read_dev_stack_state || [[ "${DEV_STACK_STATE_managed_postgres:-}" != "1" ]]; then
    return 0
  fi

  local state_port="${DEV_STACK_STATE_postgres_port:-}"
  if [[ -z "$state_port" || "$state_port" != "$port" ]]; then
    return 0
  fi

  local current_refcount="${DEV_STACK_STATE_refcount:-1}"
  [[ "$current_refcount" =~ ^[0-9]+$ ]] || current_refcount=1
  write_dev_stack_state "$port" "$((current_refcount + 1))"
  DEV_STACK_SERVER_REGISTERED=1
}

ensure_dev_postgres_running() {
  local port="$1"
  if test_dev_postgres_connection "$port"; then
    with_dev_stack_lock _join_dev_stack_if_managed "$port"
    return 0
  fi

  printf '==> Starting Docker Postgres on localhost:%s\n' "$port"
  start_dev_stack_postgres_container "$port" || return 1
  wait_dev_postgres_ready "$port" || return 1

  with_dev_stack_lock _ensure_dev_postgres_state "$port"
}

_ensure_dev_postgres_state() {
  local port="$1"
  if ! read_dev_stack_state; then
    write_dev_stack_state "$port" 1
    DEV_STACK_SERVER_REGISTERED=1
  fi
}

dev_stack_server_owns_ref() {
  [[ "$DEV_STACK_SERVER_REGISTERED" -eq 1 || "${HC_DEV_STACK_PREREGISTERED:-0}" == "1" ]]
}

init_dev_stack_state() {
  with_dev_stack_lock _init_dev_stack_state_impl "$@"
}

_init_dev_stack_state_impl() {
  local postgres_port="$1"
  local server_count="$2"

  if read_dev_stack_state && [[ "${DEV_STACK_STATE_managed_postgres:-}" == "1" ]]; then
    local state_port="${DEV_STACK_STATE_postgres_port:-}"
    if [[ -n "$state_port" ]]; then
      printf '==> Stopping previous dev stack Postgres session\n'
      stop_dev_stack_postgres "$state_port"
      stop_dev_stack_fcaptcha
    fi
  fi

  write_dev_stack_state "$postgres_port" "$server_count"
}

stop_dev_stack() {
  with_dev_stack_lock _stop_dev_stack_impl
}

_stop_dev_stack_impl() {
  if read_dev_stack_state && [[ "${DEV_STACK_STATE_managed_postgres:-}" == "1" ]]; then
    local state_port="${DEV_STACK_STATE_postgres_port:-}"
    if [[ -n "$state_port" ]]; then
      stop_dev_stack_postgres "$state_port"
      stop_dev_stack_fcaptcha
    fi
  fi

  rm -f "$DEV_STACK_STATE_FILE"
}

unregister_dev_stack_server() {
  with_dev_stack_lock _unregister_dev_stack_server_impl
}

_unregister_dev_stack_server_impl() {
  if ! dev_stack_server_owns_ref; then
    return 0
  fi

  if ! read_dev_stack_state || [[ "${DEV_STACK_STATE_managed_postgres:-}" != "1" ]]; then
    return 0
  fi

  local state_port="${DEV_STACK_STATE_postgres_port:-}"
  if [[ -z "$state_port" ]]; then
    rm -f "$DEV_STACK_STATE_FILE"
    DEV_STACK_SERVER_REGISTERED=0
    return 0
  fi

  local current_refcount="${DEV_STACK_STATE_refcount:-1}"
  [[ "$current_refcount" =~ ^[0-9]+$ ]] || current_refcount=1
  local refcount=$((current_refcount - 1))
  if (( refcount > 0 )); then
    write_dev_stack_state "$state_port" "$refcount"
    DEV_STACK_SERVER_REGISTERED=0
    return 0
  fi

  rm -f "$DEV_STACK_STATE_FILE"
  stop_dev_stack_postgres "$state_port"
  stop_dev_stack_fcaptcha
  DEV_STACK_SERVER_REGISTERED=0
  printf '==> Stopped Docker Postgres and freed localhost port\n'
}

release_dev_stack_postgres() {
  unregister_dev_stack_server
}
