#!/usr/bin/env bash
# Wait until an HTTP endpoint responds, then open it in the browser.
# Treats 403 as healthy for the API root page (intentional dev forbidden landing).
set -euo pipefail

url="${1:-}"
label="${2:-server}"
max_attempts="${3:-90}"

if [[ -z "$url" ]]; then
  printf 'usage: %s <url> [label] [max_attempts]\n' "$(basename "$0")" >&2
  exit 1
fi

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

"$script_dir/wait-for-dev-server.sh" "$url" "$label" "$max_attempts"
"$script_dir/open-dev-browser.sh" "$url"
