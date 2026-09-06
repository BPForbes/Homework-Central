#!/usr/bin/env bash
# Catches the implicit-type cases the compiler and eslint cannot.
#
# `dotnet build` blocks ordinary implicitly typed C# locals through IDE0008, and
# eslint blocks `var` in .ts/.tsx/.js/.cjs/.mjs/.jsx. Four classes escape both:
#
#   1. C# pattern positions (`is var x`, `case var x`, a `var` switch arm).
#      These are patterns rather than declarations, so IDE0008 never fires.
#   2. A deliberate suppression of the rule, including via an MSBuild file or a
#      workflow command-line flag.
#   3. Inline <script> blocks in .html, which no compiler or linter reads.
#   4. `dynamic`, which defers binding to runtime and so is more untyped than
#      `var`; no analyzer here rejects it.
#
# KNOWN LIMITATION. grep cannot lex C#, so the C# scan can report a `var` that
# sits inside a `/* */` body line not starting with `*`, or inside a verbatim
# or ordinary string literal whose wording looks like a declaration. If that
# happens, rephrase the prose or start the comment line with `*` — do NOT reach
# for a suppression, which is what this script exists to prevent. IDE0008
# already covers real declarations in every compiled file, so only the three
# pattern forms rely on this scan alone.
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

# Drops a grep hit whose *content* begins with a comment marker. Anchored to the
# `path:lineno:` prefix: an unanchored `:[[:space:]]*//` also matches the `://`
# inside a URL, which silently discarded real declarations.
strip_comment_lines() {
    grep -v -E '^[^:]+:[0-9]+:[[:space:]]*(///|//|\*|/\*|<!--)'
}

# 1. C# implicitly typed locals and pattern positions. Matching declaration and
#    pattern syntax rather than the bare token keeps prose, string literals and
#    identifiers such as `invariant` or `EnvVar` from tripping it. A terminator
#    at end-of-line is accepted so a wrapped initializer cannot evade the scan.
csharp_hits="$(
    grep -rn --include='*.cs' -E \
        '(^|[^A-Za-z0-9_])(is|case)[[:space:]]+var[[:space:]]+[A-Za-z_]|(^|[^A-Za-z0-9_])var[[:space:]]*\(|(^|[^A-Za-z0-9_])var[[:space:]]+[A-Za-z_][A-Za-z0-9_]*[[:space:]]*(=>|=[^=]|=[[:space:]]*$|;|,|\)|[[:space:]]in[[:space:]]|[[:space:]]*$)' \
        backend tools scripts 2>/dev/null \
        | grep -v -E '/(obj|bin)/' \
        | strip_comment_lines
)"
if [ -n "$csharp_hits" ]; then
    report 'Implicitly typed C# local or pattern (use an explicit type):' "$csharp_hits"
fi

# 2. `dynamic` locals. Deliberately does not accept ` in ` as a terminator, so
#    prose such as "the dynamic threshold in place of..." cannot match.
dynamic_hits="$(
    grep -rn --include='*.cs' -E \
        '(^|[^A-Za-z0-9_])dynamic[[:space:]]+[A-Za-z_][A-Za-z0-9_]*[[:space:]]*(=[^=]|;|,|\))' \
        backend tools scripts 2>/dev/null \
        | grep -v -E '/(obj|bin)/' \
        | strip_comment_lines
)"
if [ -n "$dynamic_hits" ]; then
    report 'dynamic defers binding to runtime; use a static type:' "$dynamic_hits"
fi

# 3. Suppressions of the var style rules. Matches suppression *syntax* only, so
#    documentation naming the rules cannot trip it. Covers MSBuild files that are
#    imported implicitly (.targets as well as .props) and workflow YAML, where a
#    "make CI green" change would otherwise land unseen.
suppression_hits="$(
    grep -rn --include='*.cs' --include='*.csproj' --include='*.props' \
        --include='*.targets' --include='*.sln' --include='*.rsp' \
        --include='*.yml' --include='*.yaml' \
        --include='*.ts' --include='*.tsx' \
        --include='*.js' --include='*.cjs' --include='*.mjs' --include='*.jsx' \
        -E '#pragma[[:space:]]+warning[[:space:]]+disable[^;]*IDE000[78]|<NoWarn>[^<]*IDE000[78]|SuppressMessage[^)]*IDE000[78]|dotnet_diagnostic\.IDE000[78]\.severity[[:space:]]*=[[:space:]]*(none|silent|suggestion|warning)|csharp_style_var_[a-z_]+[[:space:]]*=[[:space:]]*true|csharp_style_var_[a-z_]+[[:space:]]*=[[:space:]]*false[[:space:]]*$|csharp_style_var_[a-z_]+[[:space:]]*=[[:space:]]*false:(none|silent|suggestion|warning)|<EnforceCodeStyleInBuild>[[:space:]]*false|-p:NoWarn=[^[:space:]]*IDE000[78]|-p:EnforceCodeStyleInBuild=false|eslint-disable[^:]*no-var' \
        . 2>/dev/null \
        | grep -v -E '/(obj|bin|node_modules|dist)/' \
        | grep -v -E '^\./scripts/check-no-var\.sh:'
)"
if [ -n "$suppression_hits" ]; then
    report 'Suppression or relaxation of the no-var rules:' "$suppression_hits"
fi

# 4. Any .editorconfig other than the repository root one. Rejecting these
#    outright is simpler and stricter than parsing severity suffixes: a bare
#    `csharp_style_var_* = false` with no suffix silently drops the rule to its
#    default suggestion severity, which cannot fail a build.
nested_editorconfig="$(
    find . -name '.editorconfig' -not -path './.editorconfig' \
        -not -path '*/node_modules/*' -not -path '*/obj/*' -not -path '*/bin/*' \
        -not -path '*/dist/*' 2>/dev/null
)"
if [ -n "$nested_editorconfig" ]; then
    report 'Only the repository root .editorconfig is allowed:' "$nested_editorconfig"
fi

# 5. `var` in files neither the C# build nor eslint covers. Comment lines are
#    skipped, since a `var` in a comment does not execute. Vendored bundles are
#    excluded: nobody here can reformat third-party minified code.
web_hits="$(
    grep -rn --include='*.html' --include='*.js' --include='*.cjs' \
        --include='*.mjs' --include='*.jsx' \
        -E '(^|[^A-Za-z0-9_$.])var[[:space:]]+[A-Za-z_$][A-Za-z0-9_$]*[[:space:]]*(=[^=]|;|,|\)|[[:space:]](in|of)[[:space:]])' \
        . 2>/dev/null \
        | grep -v -E '/(node_modules|dist|obj|bin)/' \
        | grep -v -E '^\./frontend/public/' \
        | grep -v -E '\.min\.js:' \
        | grep -v -E '<!--.*var' \
        | strip_comment_lines
)"
if [ -n "$web_hits" ]; then
    report 'var in an unlinted web file (use const, or let when reassigned):' "$web_hits"
fi

if [ "$failed" -ne 0 ]; then
    printf '\nno-var check failed. See AGENTS.md for the rule and its exceptions.\n'
    exit 1
fi

printf 'no-var check passed.\n'
