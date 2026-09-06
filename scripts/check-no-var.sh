#!/usr/bin/env bash
# Catches the implicit-type cases the compiler and eslint cannot.
#
# `dotnet build` blocks ordinary implicitly typed C# locals through IDE0008, and
# eslint blocks `var` in .ts/.tsx. Three classes escape both:
#
#   1. C# pattern positions (`is var x`, `case var x`, a `var` switch arm). These
#      are patterns rather than declarations, so IDE0008 never fires on them.
#   2. A deliberate suppression of the rule (#pragma, NoWarn, SuppressMessage, a
#      nested .editorconfig relaxing the severity, or an eslint-disable).
#   3. Files no compiler or linter reads: inline <script> blocks in .html, and
#      plain .js/.cjs/.mjs/.jsx, which eslint's {ts,tsx} glob does not match.
#
# Exits non-zero and prints every offending line. Run from the repository root.

set -uo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root" || exit 2

failed=0

report() {
    printf '\n%s\n' "$1"
    printf '%s\n' "$2"
    failed=1
}

# 1. Any `var` token left in C#. The build already rejects ordinary declarations,
#    so whatever reaches here is a pattern position or a suppressed declaration.
#    Comment-only lines are skipped so prose about the rule does not trip it.
#    Matching declaration and pattern *syntax* rather than the bare token keeps
#    prose, string literals and identifiers such as `invariant` from tripping it.
csharp_hits="$(
    grep -rn --include='*.cs' -E \
        '(^|[^A-Za-z0-9_])(is|case)[[:space:]]+var[[:space:]]+[A-Za-z_]|(^|[^A-Za-z0-9_])var[[:space:]]*\(|(^|[^A-Za-z0-9_])var[[:space:]]+[A-Za-z_][A-Za-z0-9_]*[[:space:]]*(=>|=[^=]|;|,|\)|[[:space:]]in[[:space:]])' \
        backend tools scripts 2>/dev/null \
        | grep -v -E '/(obj|bin)/' \
        | grep -v -E ':[[:space:]]*(///|//|\*|/\*)'
)"
if [ -n "$csharp_hits" ]; then
    report 'Implicitly typed C# local or pattern (use an explicit type):' "$csharp_hits"
fi

# 2. Suppressions of the two var style rules. This matches suppression *syntax*
#    only, so documentation naming the rules cannot trip it. The states that
#    switch the rules ON (`= false:error`, `EnforceCodeStyleInBuild=true`) are
#    not suppression syntax and so never match.
suppression_hits="$(
    grep -rn --include='*.cs' --include='*.csproj' --include='*.props' \
        --include='.editorconfig' --include='*.ts' --include='*.tsx' \
        --include='*.js' --include='*.cjs' --include='*.mjs' --include='*.jsx' \
        -E '#pragma[[:space:]]+warning[[:space:]]+disable[^;]*IDE000[78]|<NoWarn>[^<]*IDE000[78]|SuppressMessage[^)]*IDE000[78]|dotnet_diagnostic\.IDE000[78]\.severity[[:space:]]*=[[:space:]]*(none|silent|suggestion|warning)|csharp_style_var_[a-z_]+[[:space:]]*=[[:space:]]*true|csharp_style_var_[a-z_]+[[:space:]]*=[[:space:]]*false:(none|silent|suggestion|warning)|<EnforceCodeStyleInBuild>[[:space:]]*false|eslint-disable[^:]*no-var' \
        . 2>/dev/null \
        | grep -v -E '/(obj|bin|node_modules|dist)/' \
        | grep -v -E '^\./scripts/check-no-var\.sh:'
)"
if [ -n "$suppression_hits" ]; then
    report 'Suppression or relaxation of the no-var rules:' "$suppression_hits"
fi

# 3. `var` in files neither the C# build nor the eslint {ts,tsx} glob covers.
#    Same rule as above: require declaration syntax, and skip comment lines,
#    since a `var` inside a comment does not execute.
web_hits="$(
    grep -rn --include='*.html' --include='*.js' --include='*.cjs' \
        --include='*.mjs' --include='*.jsx' \
        -E '(^|[^A-Za-z0-9_$.])var[[:space:]]+[A-Za-z_$][A-Za-z0-9_$]*[[:space:]]*(=[^=]|;|,|\)|[[:space:]](in|of)[[:space:]])' \
        . 2>/dev/null \
        | grep -v -E '/(node_modules|dist|obj|bin)/' \
        | grep -v -E ':[[:space:]]*(//|\*|/\*|<!--)'
)"
if [ -n "$web_hits" ]; then
    report 'var in an unlinted web file (use const, or let when reassigned):' "$web_hits"
fi

if [ "$failed" -ne 0 ]; then
    printf '\nno-var check failed. See AGENTS.md for the rule and its one exception.\n'
    exit 1
fi

printf 'no-var check passed.\n'
