#!/usr/bin/env bash
# Wait until an HTTP endpoint responds. Treats 403 as healthy for the API root page.
set -euo pipefail

url="${1:-}"
label="${2:-server}"
max_attempts="${3:-300}"

if [[ -z "$url" ]]; then
  printf 'usage: %s <url> [label] [max_attempts]\n' "$(basename "$0")" >&2
  exit 1
fi

for ((attempt = 1; attempt <= max_attempts; attempt++)); do
  status="$(curl -s -o /dev/null -w '%{http_code}' "$url" 2>/dev/null || true)"
  if [[ "$status" =~ ^[23] || "$status" == "403" ]]; then
    exit 0
  fi
  sleep 1
done

printf '==> Timed out waiting for %s at %s\n' "$label" "$url" >&2
exit 1
