#!/usr/bin/env bash
# Shared helpers for starting/stopping the local Homework Central dev stack.
# Source from other scripts in this directory; do not run directly.

DEV_STACK_REPO_ROOT="${DEV_STACK_REPO_ROOT:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"
DEV_STACK_STATE_FILE="$DEV_STACK_REPO_ROOT/.hc-dev-stack.state"
DEV_STACK_COMPOSE_FILE="$DEV_STACK_REPO_ROOT/docker-compose.yml"
DEV_STACK_ENV_FILE="$DEV_STACK_REPO_ROOT/.env"
DEV_STACK_POSTGRES_PASSWORD="postgres"
DEV_STACK_POSTGRES_HOST_PORT="5434"
DEV_STACK_FCAPTCHA_HOST_PORT="3010"
DEV_STACK_CLAMAV_HOST_PORT="3310"
# Must match docker-compose.yml's `fcaptcha` service image tag.
DEV_STACK_FCAPTCHA_IMAGE="homework-central-fcaptcha:1.12.0"
DEV_STACK_SERVER_REGISTERED=0
# 127.0.0.1 rather than localhost: localhost prefers ::1 on Windows and burns the whole connect
# timeout while Docker Desktop has published IPv4 only.
#
# Assigned unconditionally, not defaulted from the environment: the probe carries the dev
# Postgres credentials, and an exported value would aim it — and the "already running" check
# that clears on any answer — at an address the developer never chose. A sourcing script that
# wants a different host still reassigns it after the source, which is what run-dev.sh does.
DEV_POSTGRES_CONNECT_HOST="127.0.0.1"
# Last stdout/stderr from PostgresHostCheck, surfaced by readiness waits when they time out.
DEV_POSTGRES_HOST_CHECK_DETAIL=""
DEV_POSTGRES_HOST_CHECK_BUILD_FAILED=0

trim_dev_env_whitespace() {
  local value="$1"
  value="${value#"${value%%[![:space:]]*}"}"
  value="${value%"${value##*[![:space:]]}"}"
  printf '%s' "$value"
}

generate_dev_secret() {
  if command -v openssl >/dev/null 2>&1; then
    # URL-safe base64 avoids special characters breaking connection strings and shells.
    openssl rand -base64 48 | tr '+/' '-_' | tr -d '=\n'
  else
    printf 'error: openssl is required to generate secrets for a new .env file\n' >&2
    return 1
  fi
}

read_dev_env_file() {
  DEV_ENV_JWT_SECRET=""
  DEV_ENV_FCAPTCHA_SECRET=""
  DEV_ENV_POSTGRES_PASSWORD=""
  DEV_ENV_POSTGRES_HOST_PORT="$DEV_STACK_POSTGRES_HOST_PORT"
  DEV_ENV_FCAPTCHA_HOST_PORT="$DEV_STACK_FCAPTCHA_HOST_PORT"
  DEV_ENV_FCAPTCHA_HOST_PORT_SET=0

  if [[ ! -f "$DEV_STACK_ENV_FILE" ]]; then
    return 0
  fi

  while IFS= read -r line || [[ -n "$line" ]]; do
    case "$line" in
      ''|\#*) continue ;;
    esac

    local key="${line%%=*}"
    local value="${line#*=}"
    key="$(trim_dev_env_whitespace "$key")"

    case "$key" in
      JWT_SECRET) DEV_ENV_JWT_SECRET="$value" ;;
      FCAPTCHA_SECRET) DEV_ENV_FCAPTCHA_SECRET="$value" ;;
      POSTGRES_PASSWORD) DEV_ENV_POSTGRES_PASSWORD="$value" ;;
      POSTGRES_HOST_PORT) DEV_ENV_POSTGRES_HOST_PORT="$value" ;;
      FCAPTCHA_HOST_PORT) DEV_ENV_FCAPTCHA_HOST_PORT="$value"; DEV_ENV_FCAPTCHA_HOST_PORT_SET=1 ;;
    esac
  done <"$DEV_STACK_ENV_FILE"

  DEV_ENV_POSTGRES_HOST_PORT="$(trim_dev_env_whitespace "$DEV_ENV_POSTGRES_HOST_PORT")"
  [[ -n "$DEV_ENV_POSTGRES_HOST_PORT" ]] || DEV_ENV_POSTGRES_HOST_PORT="$DEV_STACK_POSTGRES_HOST_PORT"
  DEV_ENV_FCAPTCHA_HOST_PORT="$(trim_dev_env_whitespace "$DEV_ENV_FCAPTCHA_HOST_PORT")"
  [[ -n "$DEV_ENV_FCAPTCHA_HOST_PORT" ]] || DEV_ENV_FCAPTCHA_HOST_PORT="$DEV_STACK_FCAPTCHA_HOST_PORT"
}

set_dev_env_var() {
  local key="$1"
  local value="$2"
  local tmp replaced=false
  tmp="$(mktemp)"

  while IFS= read -r line || [[ -n "$line" ]]; do
    if [[ "$line" == "${key}="* ]]; then
      printf '%s=%s\n' "$key" "$value" >>"$tmp"
      replaced=true
    else
      printf '%s\n' "$line" >>"$tmp"
    fi
  done <"$DEV_STACK_ENV_FILE"

  if [[ "$replaced" == false ]]; then
    printf '%s=%s\n' "$key" "$value" >>"$tmp"
  fi

  mv "$tmp" "$DEV_STACK_ENV_FILE"
}

ensure_dev_env_file() {
  local roll_fcaptcha_secret="${1:-0}"

  if [[ ! -f "$DEV_STACK_ENV_FILE" ]]; then
    printf '==> Creating %s from .env.example\n' "$DEV_STACK_ENV_FILE"
    cp "$DEV_STACK_REPO_ROOT/.env.example" "$DEV_STACK_ENV_FILE"
  fi

  read_dev_env_file

  local updated=false

  if [[ "$DEV_ENV_JWT_SECRET" == "replace-with-a-long-random-secret" || -z "$DEV_ENV_JWT_SECRET" ]]; then
    DEV_ENV_JWT_SECRET="$(generate_dev_secret)" || return 1
    set_dev_env_var "JWT_SECRET" "$DEV_ENV_JWT_SECRET"
    updated=true
  fi

  if [[ "$roll_fcaptcha_secret" == "1" ]]; then
    DEV_ENV_FCAPTCHA_SECRET="$(generate_dev_secret)" || return 1
    set_dev_env_var "FCAPTCHA_SECRET" "$DEV_ENV_FCAPTCHA_SECRET"
    updated=true
  elif [[ "$DEV_ENV_FCAPTCHA_SECRET" == "replace-with-a-long-random-secret" || -z "$DEV_ENV_FCAPTCHA_SECRET" ]]; then
    DEV_ENV_FCAPTCHA_SECRET="$(generate_dev_secret)" || return 1
    set_dev_env_var "FCAPTCHA_SECRET" "$DEV_ENV_FCAPTCHA_SECRET"
    updated=true
  fi

  if [[ "$DEV_ENV_FCAPTCHA_HOST_PORT_SET" != "1" ]]; then
    DEV_ENV_FCAPTCHA_HOST_PORT="$DEV_STACK_FCAPTCHA_HOST_PORT"
    set_dev_env_var "FCAPTCHA_HOST_PORT" "$DEV_ENV_FCAPTCHA_HOST_PORT"
    updated=true
  fi

  if [[ "$DEV_ENV_POSTGRES_PASSWORD" != "$DEV_STACK_POSTGRES_PASSWORD" ]]; then
    DEV_ENV_POSTGRES_PASSWORD="$DEV_STACK_POSTGRES_PASSWORD"
    set_dev_env_var "POSTGRES_PASSWORD" "$DEV_ENV_POSTGRES_PASSWORD"
    updated=true
  fi

  if [[ "$DEV_ENV_POSTGRES_HOST_PORT" == "5432" || "$DEV_ENV_POSTGRES_HOST_PORT" == "5433" ]]; then
    printf '==> Using POSTGRES_HOST_PORT=%s (avoids local PostgreSQL on 5432/5433)\n' "$DEV_STACK_POSTGRES_HOST_PORT"
    DEV_ENV_POSTGRES_HOST_PORT="$DEV_STACK_POSTGRES_HOST_PORT"
    set_dev_env_var "POSTGRES_HOST_PORT" "$DEV_ENV_POSTGRES_HOST_PORT"
    updated=true
  fi

  if [[ "$updated" == true ]]; then
    read_dev_env_file
    if [[ "$roll_fcaptcha_secret" == "1" ]]; then
      printf '==> Rolled FCAPTCHA_SECRET in .env (local only, not committed)\n'
    else
      printf '==> Generated secrets in .env (local only, not committed)\n'
    fi
  fi

  [[ -n "$DEV_ENV_JWT_SECRET" ]] || { printf 'error: JWT_SECRET is not set in .env\n' >&2; return 1; }
  [[ ${#DEV_ENV_JWT_SECRET} -ge 32 ]] || { printf 'error: JWT_SECRET must be at least 32 characters\n' >&2; return 1; }
  [[ -n "$DEV_ENV_FCAPTCHA_SECRET" ]] || { printf 'error: FCAPTCHA_SECRET is not set in .env\n' >&2; return 1; }
  [[ ${#DEV_ENV_FCAPTCHA_SECRET} -ge 32 ]] || { printf 'error: FCAPTCHA_SECRET must be at least 32 characters\n' >&2; return 1; }
  [[ -n "$DEV_ENV_POSTGRES_PASSWORD" ]] || { printf 'error: POSTGRES_PASSWORD is not set in .env\n' >&2; return 1; }

  export JWT_SECRET="$DEV_ENV_JWT_SECRET"
  export FCAPTCHA_SECRET="$DEV_ENV_FCAPTCHA_SECRET"
  export POSTGRES_PASSWORD="$DEV_ENV_POSTGRES_PASSWORD"
  export POSTGRES_HOST_PORT="$DEV_ENV_POSTGRES_HOST_PORT"
  export FCAPTCHA_HOST_PORT="$DEV_ENV_FCAPTCHA_HOST_PORT"
}

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
  # A build that already failed will not start succeeding on its own, and the readiness loops
  # call this once per attempt: without this the failing build — and its log line — would
  # repeat every second for as long as a wait runs.
  if [[ "$DEV_POSTGRES_HOST_CHECK_BUILD_FAILED" == "1" ]]; then
    return 1
  fi

  local dll project project_dir
  dll="$(postgres_host_check_dll)"
  project="$DEV_STACK_REPO_ROOT/scripts/PostgresHostCheck/PostgresHostCheck.csproj"
  project_dir="$DEV_STACK_REPO_ROOT/scripts/PostgresHostCheck"

  # Only hand-written sources count. bin/ and obj/ hold MSBuild-generated .cs files, and a
  # Release build (CI, CodeQL) leaves them newer than this Debug output forever, which would
  # make every readiness attempt rebuild the checker.
  #
  # Command substitution rather than `find ... | grep -q`: grep closes the pipe on its first
  # match, and the SIGPIPE that find then takes surfaces as 141 under `pipefail`, which the
  # negation would read as "nothing newer" and skip a rebuild the sources do need.
  if [[ -f "$dll" ]]; then
    local newer_source
    newer_source="$(find "$project_dir" \
      \( -type d \( -name bin -o -name obj \) -prune \) -o \
      \( -type f \( -name '*.cs' -o -name '*.csproj' \) -newer "$dll" -print -quit \))"
    if [[ -z "$newer_source" ]]; then
      return 0
    fi
  fi

  # Logged only on the rebuild path: the readiness loops call this helper once per attempt,
  # so an "already built" line would repeat for as long as a wait runs.
  printf '==> Building Postgres host check\n'
  if ! dotnet build "$project" -c Debug -v q >/dev/null; then
    DEV_POSTGRES_HOST_CHECK_BUILD_FAILED=1
    return 1
  fi
}

# Sets DEV_POSTGRES_HOST_CHECK_DETAIL and returns the checker's exit code. See
# scripts/PostgresHostCheck/Program.cs for the meaning of each code.
invoke_dev_postgres_host_check() {
  local port="$1"
  local dll
  if ! build_postgres_host_check_if_needed; then
    DEV_POSTGRES_HOST_CHECK_DETAIL="PostgresHostCheck build failed"
    return 1
  fi

  dll="$(postgres_host_check_dll)"
  if [[ ! -f "$dll" ]]; then
    DEV_POSTGRES_HOST_CHECK_DETAIL="PostgresHostCheck is not built"
    return 1
  fi

  local output status=0
  output="$(dotnet "$dll" "$port" "$DEV_POSTGRES_CONNECT_HOST" 2>&1)" || status=$?
  DEV_POSTGRES_HOST_CHECK_DETAIL="$output"
  return "$status"
}

test_dev_postgres_connection() {
  invoke_dev_postgres_host_check "$1"
}

# Readiness gate for "the host can reach Docker Postgres on this published port".
# Exit code 3 (a server answered without handing back a usable master database, normally a fresh
# volume) and exit code 5 (the volume's password is not the dev one) both mean a server answered
# and refused this connection, which still proves
# the published port reaches Postgres. run-dev creates the database and resets a mismatched
# volume only after this wait, so treating either as not-ready deadlocks the wait against its
# own repair. Exit code 4 (server not accepting sessions yet) stays not-ready: it clears on
# its own.
#
# Silent, because polling loops call it once per second. One-shot callers should prefer
# test_dev_postgres_already_running, which names the rejection.
test_dev_postgres_host_reachable() {
  local status=0
  invoke_dev_postgres_host_check "$1" || status=$?
  [[ "$status" -eq 0 || "$status" -eq 3 || "$status" -eq 5 ]]
}

# True when a server answered and rejected the dev credentials, which means the volume behind
# it was initialised with a different password.
#
# Only the host's view of the published port can establish this. A psql probe run inside the
# container connects over loopback, which initdb trusts ahead of the image's scram-sha-256
# rule, so it authenticates against no password and succeeds on a mismatched volume.
test_dev_postgres_credentials_rejected() {
  local status=0
  invoke_dev_postgres_host_check "$1" || status=$?
  [[ "$status" -eq 5 ]]
}

report_dev_postgres_rejected() {
  local port="$1"
  printf '==> Postgres answered on %s:%s but rejected the check: %s\n' \
    "$DEV_POSTGRES_CONNECT_HOST" "$port" "${DEV_POSTGRES_HOST_CHECK_DETAIL:-no detail}"
}

# One-shot "Postgres is already up on this published port" check, for the callers that skip
# starting a container. It names a rejected connection rather than swallowing it: nothing on
# this path resets a volume whose password does not match, so an unreported 28P01 would
# resurface as an opaque API startup failure well after the cause scrolled away.
test_dev_postgres_already_running() {
  local port="$1"
  local status=0
  invoke_dev_postgres_host_check "$port" || status=$?
  if [[ "$status" -eq 3 || "$status" -eq 5 ]]; then
    report_dev_postgres_rejected "$port"
  fi
  [[ "$status" -eq 0 || "$status" -eq 3 || "$status" -eq 5 ]]
}

start_dev_stack_postgres_container() {
  local port="$1"
  local force_recreate="${2:-0}"
  command -v docker >/dev/null 2>&1 || return 1
  docker info >/dev/null 2>&1 || return 1

  export POSTGRES_PASSWORD="$DEV_STACK_POSTGRES_PASSWORD"
  export POSTGRES_HOST_PORT="$port"
  local -a compose_args=(-f "$DEV_STACK_COMPOSE_FILE" --env-file "$DEV_STACK_ENV_FILE" up -d)
  if [[ "$force_recreate" == "1" ]]; then
    compose_args+=(--force-recreate)
  fi
  compose_args+=(postgres)
  docker compose "${compose_args[@]}"
}

wait_dev_postgres_ready() {
  local port="$1"
  local timeout_seconds=30
  local deadline=$((SECONDS + timeout_seconds))
  local status
  while true; do
    status=0
    invoke_dev_postgres_host_check "$port" || status=$?
    if [[ "$status" -eq 0 ]]; then
      return 0
    fi
    if [[ "$status" -eq 3 || "$status" -eq 5 ]]; then
      # Nothing repairs the volume behind this wait — start-api-dev only starts the
      # container — so name the rejection instead of leaving the API to fail on it.
      report_dev_postgres_rejected "$port"
      return 0
    fi
    if [[ "$SECONDS" -ge "$deadline" ]]; then
      break
    fi
    sleep 1
  done
  printf 'error: Postgres did not become ready on %s:%s within %ss: %s\n' \
    "$DEV_POSTGRES_CONNECT_HOST" "$port" "$timeout_seconds" \
    "${DEV_POSTGRES_HOST_CHECK_DETAIL:-no detail}" >&2
  return 1
}

# FCaptcha (see docker-compose.yml's `fcaptcha` service) is stateless — no volume, no credentials
# to reset — so unlike Postgres above it doesn't need refcounted start/stop bookkeeping in
# .hc-dev-stack.state; it's simply started alongside Postgres and stopped whenever Postgres is.
start_dev_stack_fcaptcha_container() {
  local port="$1"
  local force_recreate="${2:-0}"
  command -v docker >/dev/null 2>&1 || return 1
  docker info >/dev/null 2>&1 || return 1

  export FCAPTCHA_HOST_PORT="$port"
  local -a compose_args=(-f "$DEV_STACK_COMPOSE_FILE" --env-file "$DEV_STACK_ENV_FILE" up -d)
  # `--build` used to be passed on every start. The build context is a pinned upstream tag that
  # never changes between runs, so that woke BuildKit — and the memory its daemon holds for the
  # build graph and cache — for a no-op rebuild each time the dev stack came up. Build only when
  # the image really is missing, or when HC_FCAPTCHA_REBUILD=1 forces it.
  if [[ "${HC_FCAPTCHA_REBUILD:-0}" == "1" ]] \
    || ! docker image inspect "$DEV_STACK_FCAPTCHA_IMAGE" >/dev/null 2>&1; then
    compose_args+=(--build)
  fi
  if [[ "$force_recreate" == "1" ]]; then
    compose_args+=(--force-recreate)
  fi
  compose_args+=(fcaptcha)
  if ! docker compose "${compose_args[@]}"; then
    printf 'error: docker compose up fcaptcha failed (first run builds from github.com/WebDecoy/FCaptcha v1.12.0 — check network and Docker BuildKit)\n' >&2
    return 1
  fi
}

# Postgres helper first, then the existing FCaptcha helper in the background.
# /healthz only needs the master database; login captcha can finish after Ready.
start_dev_stack_postgres_then_fcaptcha_background() {
  local postgres_port="$1"
  local fcaptcha_port="$2"
  local force_recreate="${3:-0}"
  start_dev_stack_postgres_container "$postgres_port" "$force_recreate" || return 1
  start_dev_stack_fcaptcha_container "$fcaptcha_port" "$force_recreate" &
  return 0
}

get_dev_fcaptcha_container_secret() {
  local container_id line
  container_id="$(docker compose -f "$DEV_STACK_COMPOSE_FILE" --env-file "$DEV_STACK_ENV_FILE" ps -q fcaptcha 2>/dev/null || true)"
  [[ -n "$container_id" ]] || return 1

  while IFS= read -r line; do
    case "$line" in
      FCAPTCHA_SECRET=*) printf '%s' "${line#FCAPTCHA_SECRET=}"; return 0 ;;
    esac
  done < <(docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' "$container_id" 2>/dev/null)
  return 1
}

test_dev_fcaptcha_secret_aligned() {
  local expected="${DEV_ENV_FCAPTCHA_SECRET:-}"
  [[ -n "$expected" ]] || return 1

  local actual
  actual="$(get_dev_fcaptcha_container_secret || true)"
  [[ -n "$actual" && "$actual" == "$expected" ]]
}

test_dev_fcaptcha_connection() {
  local port="$1"
  if command -v curl >/dev/null 2>&1; then
    curl -sf --max-time 2 "http://127.0.0.1:${port}/fcaptcha.js" >/dev/null 2>&1
    return $?
  fi

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

# start-api-dev (not already waited by run-dev) starts Postgres first and
# backgrounds FCaptcha. A cold `up --build postgres fcaptcha` holds migrate
# until the FCaptcha git image exists.
ensure_dev_stack_core_running() {
  local postgres_port="$1"
  local fcaptcha_port="$2"

  # Reachability, not homework_central_master: see ensure_dev_postgres_running.
  if test_dev_postgres_already_running "$postgres_port"; then
    if test_dev_fcaptcha_connection "$fcaptcha_port" && test_dev_fcaptcha_secret_aligned; then
      with_dev_stack_lock _join_dev_stack_if_managed "$postgres_port"
      return 0
    fi
    if test_dev_fcaptcha_connection "$fcaptcha_port"; then
      printf '==> Recreating Docker FCaptcha (FCAPTCHA_SECRET changed in .env)\n'
      start_dev_stack_fcaptcha_container "$fcaptcha_port" 1 &
    else
      start_dev_stack_fcaptcha_container "$fcaptcha_port" 0 &
    fi
    with_dev_stack_lock _join_dev_stack_if_managed "$postgres_port"
    return 0
  fi
  printf '==> Starting Docker Postgres on %s:%s (FCaptcha continues in the background)\n' \
    "$DEV_POSTGRES_CONNECT_HOST" "$postgres_port"
  start_dev_stack_postgres_then_fcaptcha_background "$postgres_port" "$fcaptcha_port" 0 || return 1
  wait_dev_postgres_ready "$postgres_port" || return 1
  with_dev_stack_lock _ensure_dev_postgres_state "$postgres_port"
}

ensure_dev_fcaptcha_running() {
  local port="$1"
  local needs_start=1
  local needs_recreate=0

  if test_dev_fcaptcha_connection "$port"; then
    needs_start=0
    if ! test_dev_fcaptcha_secret_aligned; then
      needs_recreate=1
      printf '==> Recreating Docker FCaptcha (FCAPTCHA_SECRET changed in .env)\n'
    fi
  fi

  if [[ "$needs_start" -eq 1 || "$needs_recreate" -eq 1 ]]; then
    if [[ "$needs_start" -eq 1 ]]; then
      printf '==> Starting Docker FCaptcha on localhost:%s\n' "$port"
    fi
    start_dev_stack_fcaptcha_container "$port" "$needs_recreate" || return 1
    wait_dev_fcaptcha_ready "$port" || return 1
  fi
}

stop_dev_stack_fcaptcha() {
  command -v docker >/dev/null 2>&1 || return 0
  docker compose -f "$DEV_STACK_COMPOSE_FILE" --env-file "$DEV_STACK_ENV_FILE" stop fcaptcha >/dev/null 2>&1 || true
}

# ClamAV (see docker-compose.yml's `clamav` service) backs upload malware scanning
# (appsettings.Development.json enables it). Like FCaptcha it is stateless from the app's
# point of view, so it is started alongside Postgres and stopped whenever Postgres is.
# Unlike FCaptcha, readiness is best-effort: the first run downloads virus signatures
# (minutes), and the API scanner fails open (NotScanned) while clamd is unreachable, so a
# slow ClamAV must never block the dev stack.
start_dev_stack_clamav_container() {
  local port="$1"
  command -v docker >/dev/null 2>&1 || return 1
  docker info >/dev/null 2>&1 || return 1

  export CLAMAV_HOST_PORT="$port"
  docker compose -f "$DEV_STACK_COMPOSE_FILE" --env-file "$DEV_STACK_ENV_FILE" up -d clamav
}

test_dev_clamav_connection() {
  local port="$1"
  local reply=""
  if command -v nc >/dev/null 2>&1; then
    reply="$(printf 'PING\n' | nc -w 2 127.0.0.1 "$port" 2>/dev/null || true)"
    [[ "$reply" == PONG* ]]
  else
    # Fallback: TCP connect check only (no PING round-trip without nc).
    (exec 3<>"/dev/tcp/127.0.0.1/${port}") 2>/dev/null
  fi
}

# ClamAV is opt-in for local dev: clamd keeps ~1.2-1.5g of signatures resident, which is a
# lot on small machines, and the API scanner fails open (NotScanned) when it's absent.
dev_clamav_opted_in() {
  [[ "${HC_ENABLE_CLAMAV:-0}" == "1" ]]
}

ensure_dev_clamav_running() {
  local port="$1"
  local attempt

  if ! dev_clamav_opted_in; then
    return 0
  fi

  if test_dev_clamav_connection "$port"; then
    return 0
  fi

  printf '==> Starting Docker ClamAV on localhost:%s\n' "$port"
  start_dev_stack_clamav_container "$port" || return 1

  for ((attempt = 1; attempt <= 30; attempt++)); do
    if test_dev_clamav_connection "$port"; then
      return 0
    fi
    sleep 1
  done

  printf '==> ClamAV is still loading virus signatures (first run downloads them; can take minutes).\n'
  printf '==> Uploads scan as NotScanned (fail-open) until clamd is ready; check: docker compose logs clamav\n'
  return 0
}

stop_dev_stack_clamav() {
  command -v docker >/dev/null 2>&1 || return 0
  docker compose -f "$DEV_STACK_COMPOSE_FILE" --env-file "$DEV_STACK_ENV_FILE" stop clamav >/dev/null 2>&1 || true
}

# The local API defaults to the in-memory cache (appsettings.Development.json blanks the
# Redis connection string), but docker-compose.yml defines a `redis` service a developer may
# have started by hand. Stop it with the rest of the stack; a no-op when it was never started.
stop_dev_stack_redis() {
  command -v docker >/dev/null 2>&1 || return 0
  docker compose -f "$DEV_STACK_COMPOSE_FILE" --env-file "$DEV_STACK_ENV_FILE" stop redis >/dev/null 2>&1 || true
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
  # "Already running" is a reachability question, not a homework_central_master question:
  # on a freshly wiped volume the server is up before that database exists, and starting a
  # second time would skip the refcount join that keeps the container alive for both servers.
  if test_dev_postgres_already_running "$port"; then
    with_dev_stack_lock _join_dev_stack_if_managed "$port"
    return 0
  fi

  printf '==> Starting Docker Postgres on %s:%s\n' "$DEV_POSTGRES_CONNECT_HOST" "$port"
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
      stop_dev_stack_clamav
      stop_dev_stack_redis
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
      stop_dev_stack_clamav
      stop_dev_stack_redis
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
  stop_dev_stack_clamav
  stop_dev_stack_redis
  DEV_STACK_SERVER_REGISTERED=0
  printf '==> Stopped Docker Postgres and freed localhost port\n'
}

release_dev_stack_postgres() {
  unregister_dev_stack_server
}

# Builds rust/ including libhc_kernels for EmbedText, store cosine, GEMV, and related kernels.
# The API loads that library at runtime; C# remains the fallback so the
# Docker image does not need rustc.
# rustup puts cargo on PATH via ~/.cargo/bin after `source ~/.cargo/env` or a new shell.
add_rustup_bin_to_path() {
  local cargo_bin="$HOME/.cargo/bin"
  if [[ -d "$cargo_bin" && ":$PATH:" != *":$cargo_bin:"* ]]; then
    PATH="$cargo_bin:$PATH"
    export PATH
  fi
}

require_rust_cargo() {
  add_rustup_bin_to_path

  if ! command -v cargo >/dev/null 2>&1; then
    printf 'error: cargo is required to compile rust/. Install rustup from https://rustup.rs/, then: rustup default stable\n' >&2
    printf 'error: add ~/.cargo/bin to PATH (source ~/.cargo/env) or open a new shell\n' >&2
    printf 'error: set HC_SKIP_RUST_BUILD=1 to skip cargo build\n' >&2
    return 1
  fi

  if ! command -v rustc >/dev/null 2>&1; then
    printf 'error: rustc is required to compile rust/. cargo is on PATH but rustc is not — run: rustup default stable\n' >&2
    printf 'error: set HC_SKIP_RUST_BUILD=1 to skip cargo build\n' >&2
    return 1
  fi
}

build_rust_workspace() {
  if [[ "${HC_SKIP_RUST_BUILD:-}" == "1" ]]; then
    printf '==> Skipping Rust build (HC_SKIP_RUST_BUILD=1)\n'
    return 0
  fi
  if [[ "${HC_SKIP_BUILD:-}" == "1" ]]; then
    printf '==> Skipping Rust build (HC_SKIP_BUILD=1)\n'
    return 0
  fi

  require_rust_cargo || return 1

  printf '==> Building Rust workspace (cargo build --workspace)\n'
  (cd "$DEV_STACK_REPO_ROOT/rust" && cargo build --workspace) || return 1
  copy_hc_kernels_native
}

copy_hc_kernels_native() {
  local dest="$DEV_STACK_REPO_ROOT/backend/HomeworkCentral.Api/native"
  local debug_dir="$DEV_STACK_REPO_ROOT/rust/target/debug"
  mkdir -p "$dest"
  local name
  for name in libhc_kernels.so hc_kernels.dll libhc_kernels.dylib; do
    if [[ -f "$debug_dir/$name" ]]; then
      cp "$debug_dir/$name" "$dest/$name"
      printf '==> Copied %s into backend/HomeworkCentral.Api/native\n' "$name"
    fi
  done
}
