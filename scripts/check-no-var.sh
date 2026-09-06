#!/usr/bin/env bash
# Rejects implicitly typed locals that the compiler and eslint cannot see.
#
# Division of labour, after two rounds of review concluded that hardening a
# regex against a real language was a treadmill:
#
#   Roslyn  owns every C# *declaration*. IDE0008 is an error via .editorconfig
#           and EnforceCodeStyleInBuild, and CI compiles all four csproj, which
#           between them cover every tracked .cs file. Nothing here duplicates
#           that.
#   eslint  owns every .ts/.tsx/.mts/.cts/.js/.cjs/.mjs/.jsx file *and* inline
#           <script> in .html, which it lexes through eslint-plugin-html. A real
#           parser cannot be fooled by a `var` in a comment or a string, so the
#           web scan that used to live here is gone.
#   here    owns what neither can see: the word `var` in a C# pattern position,
#           `dynamic`, and suppressions of the rules in config files.
#
# The C# scan matches the bare word rather than declaration syntax. That is not
# laziness: `var` appears zero times in the 46k lines of C# here, including in
# prose, so the bare word has no false positives while catching every form at
# once — declarations, `is var x`, `case var x`, a wrapped initializer, a
# non-breaking space after the keyword, and a `/* */` that closes mid-line.
# Terminator-matching caught none of those reliably and false-positived on
# ordinary English. The cost is that C# prose may not use the word `var`; write
# "implicitly typed local" instead.
#
# Exits non-zero and prints every offending line. Run from anywhere.

set -uo pipefail

# grep word boundaries and character classes are locale-sensitive, so an
# unpinned locale makes the verdict depend on the machine. Pin it: a CI result
# and a laptop result must mean the same thing.
export LC_ALL=C

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root" || exit 2

failed=0

report() {
    printf '\n%s\n' "$1"
    printf '%s\n' "$2"
    failed=1
}

# Deriving the file list from git rather than from hardcoded directories means
# renaming or adding a source root cannot silently shrink the scan. Untracked
# files are included so a newly written .cs is caught before it is staged;
# --exclude-standard keeps generated obj/ and bin/ out, since both are ignored.
cs_files="$(git ls-files -z --cached --others --exclude-standard '*.cs' | tr '\0' '\n' | sort -u)"

# Drops a hit whose content begins with a comment marker. Anchored to the
# `path:lineno:` prefix, because an unanchored match also matches the `://` in a
# URL and would silently discard real code.
strip_comment_lines() {
    grep -v -E '^[^:]+:[0-9]+:[[:space:]]*(///|//|\*|/\*|<!--)'
}

# 1. `var` anywhere in C#. No comment filter: the word does not occur in this
#    codebase's prose, so filtering would only reopen the mid-line `/* */` hole.
if [ -n "$cs_files" ]; then
    var_hits="$(printf '%s\n' "$cs_files" | xargs -d '\n' grep -nw 'var' 2>/dev/null)"
    if [ -n "$var_hits" ]; then
        report 'The word `var` in C# (use an explicit type; in prose write "implicitly typed local"):' "$var_hits"
    fi
fi

# 2. `dynamic`, which defers binding to runtime and is the one construct
#    genuinely less typed than `var`. Unlike `var`, "dynamic" is legitimate
#    English here ("the dynamic threshold"), so comment lines are skipped and
#    the residual risk is a `dynamic` after a mid-line `/* */`.
if [ -n "$cs_files" ]; then
    dynamic_hits="$(
        printf '%s\n' "$cs_files" | xargs -d '\n' grep -nw 'dynamic' 2>/dev/null \
            | strip_comment_lines
    )"
    if [ -n "$dynamic_hits" ]; then
        report 'dynamic defers binding to runtime; use a static type:' "$dynamic_hits"
    fi
fi

# 3. Suppressions of the rules. Config-file syntax is line-oriented with no
#    lexing problem, which is why this part has held up. Comment lines are
#    stripped so prose that *forbids* a suppression does not trip it.
#
#    `eslint-disable` is handled separately below and deliberately NOT stripped:
#    it is only ever written *as* a comment, so stripping comments here would
#    make it unfindable. A blanket linterOptions.noInlineConfig would be a
#    stronger fix but would also break the one legitimate
#    react-hooks/exhaustive-deps directive in the frontend.
suppression_hits="$(
    grep -rn --include='*.cs' --include='*.csproj' --include='*.props' \
        --include='*.targets' --include='*.sln' --include='*.rsp' \
        --include='*.yml' --include='*.yaml' \
        --include='*.ts' --include='*.tsx' --include='*.mts' --include='*.cts' \
        --include='*.js' --include='*.cjs' --include='*.mjs' --include='*.jsx' \
        -E '#pragma[[:space:]]+warning[[:space:]]+disable[^;]*IDE000[78]|<NoWarn>[^<]*IDE000[78]|SuppressMessage[^)]*IDE000[78]|dotnet_diagnostic\.IDE000[78]\.severity[[:space:]]*=[[:space:]]*(none|silent|suggestion|warning)|csharp_style_var_[a-z_]+[[:space:]]*=[[:space:]]*true|csharp_style_var_[a-z_]+[[:space:]]*=[[:space:]]*false[[:space:]]*$|csharp_style_var_[a-z_]+[[:space:]]*=[[:space:]]*false:(none|silent|suggestion|warning)|<EnforceCodeStyleInBuild>[[:space:]]*false|-p:NoWarn=[^[:space:]]*IDE000[78]|-p:EnforceCodeStyleInBuild=false' \
        . 2>/dev/null \
        | grep -v -E '/(obj|bin|node_modules|dist)/' \
        | grep -v -E '^\./scripts/check-no-var\.sh:' \
        | strip_comment_lines
)"
if [ -n "$suppression_hits" ]; then
    report 'Suppression or relaxation of the no-var rules:' "$suppression_hits"
fi

# 3b. eslint disable directives aimed at these two rules. No comment filter, for
#     the reason above. Matched as directive syntax after a comment opener so
#     that a sentence merely naming the directive does not trip it.
eslint_disable_hits="$(
    grep -rn --include='*.ts' --include='*.tsx' --include='*.mts' --include='*.cts' \
        --include='*.js' --include='*.cjs' --include='*.mjs' --include='*.jsx' \
        --include='*.html' --include='*.htm' --include='*.xhtml' \
        -E '(//|/\*)[[:space:]]*eslint-disable(-next-line|-line)?[[:space:]][^*]*(no-var|prefer-const)' \
        . 2>/dev/null \
        | grep -v -E '/(obj|bin|node_modules|dist)/'
)"
if [ -n "$eslint_disable_hits" ]; then
    report 'eslint-disable aimed at no-var/prefer-const:' "$eslint_disable_hits"
fi

# 4. Any .editorconfig other than the root one. Rejecting these outright is
#    stricter than parsing severity suffixes: a bare `csharp_style_var_* = false`
#    with no suffix silently drops to the default suggestion severity, which
#    cannot fail a build.
nested_editorconfig="$(
    find . -name '.editorconfig' -not -path './.editorconfig' \
        -not -path '*/node_modules/*' -not -path '*/obj/*' -not -path '*/bin/*' \
        -not -path '*/dist/*' 2>/dev/null
)"
if [ -n "$nested_editorconfig" ]; then
    report 'Only the repository root .editorconfig is allowed:' "$nested_editorconfig"
fi

# 5. Any Directory.Build.props/.targets other than the root one. MSBuild takes
#    the *nearest* such file and does not merge, so a subdirectory copy that
#    merely omits EnforceCodeStyleInBuild silently shadows the root and disables
#    IDE0008 for that subtree. There is no suppression syntax to grep for in
#    that case — the bypass is the omission — so the file itself is rejected.
nested_msbuild="$(
    find . \( -name 'Directory.Build.props' -o -name 'Directory.Build.targets' \) \
        -not -path './Directory.Build.props' -not -path './Directory.Build.targets' \
        -not -path '*/node_modules/*' -not -path '*/obj/*' -not -path '*/bin/*' \
        -not -path '*/dist/*' 2>/dev/null
)"
if [ -n "$nested_msbuild" ]; then
    report 'Only a root Directory.Build.props/.targets is allowed (a nested one shadows it):' "$nested_msbuild"
fi

if [ "$failed" -ne 0 ]; then
    printf '\nno-var check failed. See AGENTS.md for the rule and its exceptions.\n'
    exit 1
fi

printf 'no-var check passed.\n'
