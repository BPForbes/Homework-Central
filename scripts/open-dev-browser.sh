#!/usr/bin/env bash
# Open a URL in the default browser (best effort).
set -euo pipefail

url="${1:-}"
if [[ -z "$url" ]]; then
  printf 'usage: %s <url>\n' "$(basename "$0")" >&2
  exit 1
fi

if command -v xdg-open >/dev/null 2>&1; then
  xdg-open "$url" >/dev/null 2>&1 &
elif command -v open >/dev/null 2>&1; then
  open "$url" >/dev/null 2>&1 &
elif command -v powershell.exe >/dev/null 2>&1; then
  powershell.exe -NoProfile -Command "Start-Process '$url'" >/dev/null 2>&1 &
else
  printf '==> Open in browser: %s\n' "$url"
fi
