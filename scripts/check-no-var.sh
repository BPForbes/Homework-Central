#!/usr/bin/env bash
# Backstop for what Roslyn IDE0008 and eslint no-var cannot see: C# pattern
# `var`, `dynamic`, per-file suppressions, and nested editorconfig /
# Directory.Build.* that shadow the root pin.

set -uo pipefail
export LC_ALL=C

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root" || exit 2

failed=0
report() {
    printf '\n%s\n' "$1"
    printf '%s\n' "$2"
    failed=1
}

cs_file_list() {
    git ls-files -z --cached --others --exclude-standard '*.cs'
}

cs_count="$(git ls-files --cached --others --exclude-standard '*.cs' | wc -l | tr -d ' ')" || {
    printf 'git ls-files failed\n' >&2
    exit 2
}

grep_err="$(mktemp)" || exit 2
trap 'rm -f "$grep_err"' EXIT

# xargs maps grep's no-match (1) and real errors onto 123. Empty stderr
# means no match; non-empty stderr means the scan did not complete.
grep_cs() {
    local out
    : > "$grep_err"
    out="$(cs_file_list | xargs -0 -r grep -nHwE -- "$1" 2>"$grep_err")"
    if [ -s "$grep_err" ]; then
        printf 'grep could not complete the scan of %s C# files for /%s/:\n' "$cs_count" "$1" >&2
        cat "$grep_err" >&2
        exit 2
    fi
    printf '%s' "$out"
}

# Anchored to path:lineno: so a URL :// is not treated as a comment.
strip_comment_lines() {
    grep -v -E '^[^:]+:[0-9]+:[[:space:]]*(///|//|\*|/\*|<!--)'
}

if [ "$cs_count" -gt 0 ]; then
    var_hits="$(grep_cs 'var')"
    if [ -n "$var_hits" ]; then
        report 'The word `var` in C# (use an explicit type; in prose write "implicitly typed local"):' "$var_hits"
    fi

    dynamic_hits="$(grep_cs 'dynamic' | strip_comment_lines)"
    if [ -n "$dynamic_hits" ]; then
        report 'dynamic defers binding to runtime; use a static type:' "$dynamic_hits"
    fi

    pragma_hits="$(grep_cs '^[[:space:]]*#[[:space:]]*pragma[[:space:]]+warning[[:space:]]+disable[^;]*IDE000[78]|SuppressMessage[^)]*IDE000[78]')"
    if [ -n "$pragma_hits" ]; then
        report 'Per-file suppression of the no-var analyzer:' "$pragma_hits"
    fi
fi

nested_editorconfig="$(
    find . -name '.editorconfig' -not -path './.editorconfig' \
        -not -path '*/node_modules/*' -not -path '*/obj/*' -not -path '*/bin/*' \
        -not -path '*/dist/*'
)"
if [ -n "$nested_editorconfig" ]; then
    report 'Only the repository root .editorconfig is allowed:' "$nested_editorconfig"
fi

nested_msbuild="$(
    find . \( -name 'Directory.Build.props' -o -name 'Directory.Build.targets' \) \
        -not -path './Directory.Build.props' -not -path './Directory.Build.targets' \
        -not -path '*/node_modules/*' -not -path '*/obj/*' -not -path '*/bin/*' \
        -not -path '*/dist/*'
)"
if [ -n "$nested_msbuild" ]; then
    report 'Only a root Directory.Build.props/.targets is allowed (a nested one shadows it):' "$nested_msbuild"
fi

if [ "$failed" -ne 0 ]; then
    printf '\nno-var check failed. See AGENTS.md for the rule and its exceptions.\n'
    exit 1
fi

printf 'no-var check passed.\n'
