#!/usr/bin/env bash
# Write a temporary HTML page listing API errors and open it in the browser.
set -euo pipefail

title="${1:-API Errors}"
error_file="${2:-}"

if [[ -z "$error_file" || ! -f "$error_file" ]]; then
  printf 'usage: %s <title> <error-log-file>\n' "$(basename "$0")" >&2
  exit 1
fi

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
html_file="$(mktemp /tmp/hc-api-errors-XXXXXX.html)"
encoded_title="$(python3 -c 'import html,sys; print(html.escape(sys.argv[1]))' "$title")"

{
  printf '%s\n' '<!DOCTYPE html>'
  printf '<html lang="en"><head><meta charset="UTF-8" /><title>%s</title>' "$encoded_title"
  cat <<'EOF'
<style>
  body { margin: 0; font-family: "Segoe UI", Tahoma, Geneva, Verdana, sans-serif; background: #f5f5f5; }
  .header { background: #808080; color: #ffffff; padding: 0.5rem 1rem; font-size: 1.25rem; font-weight: 600; }
  pre { margin: 0; padding: 1rem; background: #ffffff; color: #000000; white-space: pre-wrap; word-break: break-word; font-size: 0.9rem; line-height: 1.4; }
</style></head><body>
EOF
  printf '<div class="header">%s</div><pre>' "$encoded_title"
  python3 -c 'import html,sys; print(html.escape(open(sys.argv[1], encoding="utf-8", errors="replace").read()))' "$error_file"
  printf '%s\n' '</pre></body></html>'
} >"$html_file"

"$script_dir/open-dev-browser.sh" "file://$html_file"
