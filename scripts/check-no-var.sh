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
# It has a deliberate consequence worth stating: reserved-name probe files
# (`_scratch/`, `*.scratch.*`) are gitignored, so they are *not* scanned. A
# reviewer's probe may therefore contain `var` freely, which is the point — the
# probe is throwaway and never reaches the timeline, and check-clean-timeline.sh
# rejects it if it ever does. The trade is that a probe cannot be relied on to
# demonstrate this scan catching something; use a tracked file for that.
#
# The list is never held in a shell variable. Command substitution silently
# discards NUL bytes, so `$(git ls-files -z ...)` concatenates every path into
# one 20KB string; grep then reports "File name too long" and scans nothing.
# Piping git straight into xargs keeps the NUL delimiter intact, which is the
# only reason to ask for -z in the first place, and is also what makes a path
# containing a newline safe.
cs_file_list() {
    git ls-files -z --cached --others --exclude-standard '*.cs'
}

cs_count="$(git ls-files --cached --others --exclude-standard '*.cs' | wc -l | tr -d ' ')" || {
    printf 'git ls-files failed\n' >&2
    exit 2
}

grep_err="$(mktemp)" || exit 2
trap 'rm -f "$grep_err"' EXIT

# Runs grep over the C# file list and distinguishes "no matches" from "grep
# could not run".
#
# Exit codes cannot make that distinction here: grep exits 1 on no-match, and
# xargs collapses any child status in 1..125 into its own 123, so "clean scan"
# and "grep rejected its arguments" are the same number. Verified — an earlier
# attempt to gate on the status reported failure on a clean tree.
#
# stderr does distinguish them. grep writes nothing there when it simply finds
# no match, and writes a diagnostic for every real problem (`File name too
# long`, `invalid option`, `No such file`). So: stdout is matches, stderr is
# breakage, and stderr must never be discarded. Discarding it is precisely how a
# single file named `-dash.cs` made grep parse a path as flags, abort, and leave
# every file unscanned while the gate printed "no-var check passed".
#
# `--` stops option parsing, `-H` forces the path prefix that grep omits when a
# batch holds exactly one file, `-w` matches whole words.
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

# Drops a hit whose content begins with a comment marker. Anchored to the
# `path:lineno:` prefix, because an unanchored match also matches the `://` in a
# URL and would silently discard real code.
#
# Used for exactly one scan now. It was also applied to the suppression scan,
# where it produced a false positive, a fix, and then a bypass in three
# consecutive review rounds — `<!-- vendored --><NoWarn>...IDE0008</NoWarn>`
# begins with a comment opener that closes mid-line, so the filter dropped a
# live suppression. That scan no longer text-matches at all; see below.
strip_comment_lines() {
    grep -v -E '^[^:]+:[0-9]+:[[:space:]]*(///|//|\*|/\*|<!--)'
}

# 1. `var` anywhere in C#. No comment filter: the word does not occur in this
#    codebase's prose, so filtering would only reopen the mid-line `/* */` hole.
if [ "$cs_count" -gt 0 ]; then
    var_hits="$(grep_cs 'var')"
    if [ -n "$var_hits" ]; then
        report 'The word `var` in C# (use an explicit type; in prose write "implicitly typed local"):' "$var_hits"
    fi
fi

# 2. `dynamic`, which defers binding to runtime and is the one construct
#    genuinely less typed than `var`. Unlike `var`, "dynamic" is legitimate
#    English here ("the dynamic threshold"), so comment lines are skipped and
#    the residual risk is a `dynamic` after a mid-line `/* */`.
if [ "$cs_count" -gt 0 ]; then
    dynamic_hits="$(grep_cs 'dynamic' | strip_comment_lines)"
    if [ -n "$dynamic_hits" ]; then
        report 'dynamic defers binding to runtime; use a static type:' "$dynamic_hits"
    fi
fi

# 3. Per-file suppressions in C#, which no evaluated property can show: a
#    #pragma or a [SuppressMessage] is scoped to a file or a member, not to the
#    build. Everything build-wide — EnforceCodeStyleInBuild, NoWarn by any route,
#    the .editorconfig severities, the eslint rule severity — moved to
#    scripts/check-no-var-config.sh, which asks the toolchain what its effective
#    configuration is instead of guessing from text. That is what ended the
#    round-after-round pattern here.
#
#    No comment filter, and it is safe to have none: C# requires #pragma to be
#    the first token on its line, so the mid-line `/* */` trick that defeated the
#    old filter is a compile error (verified: 2x error CS). And the words
#    "pragma warning" and "SuppressMessage" appear in zero tracked .cs files, so
#    there is no prose to false-positive on. Both facts are why the filter could
#    be deleted rather than patched again.
if [ "$cs_count" -gt 0 ]; then
    pragma_hits="$(grep_cs '^[[:space:]]*#[[:space:]]*pragma[[:space:]]+warning[[:space:]]+disable[^;]*IDE000[78]|SuppressMessage[^)]*IDE000[78]')"
    if [ -n "$pragma_hits" ]; then
        report 'Per-file suppression of the no-var analyzer:' "$pragma_hits"
    fi
fi

# 3b. eslint disable directives. Two shapes, and the first one is why this scan
#     was rewritten: a *blanket* `/* eslint-disable */` or a bare
#     `// eslint-disable-next-line` names no rule, so a pattern looking for
#     "no-var" near "eslint-disable" missed it entirely while eslint honoured it.
#     `[[:space:]]*--` covers the description form, `/* eslint-disable -- later */`,
#     which is still blanket but does not end the comment after the directive and
#     so slipped past the end-of-line branch
#     and stopped reporting the var. Verified both ways.
#
#     The structural fix is `eslint . --no-inline-config` in CI, which ignores
#     every inline directive; check-no-var-config.sh asserts that script exists.
#     A blanket linterOptions.noInlineConfig in the config file would be stronger
#     still, but would also kill the one legitimate warn-level
#     react-hooks/exhaustive-deps directive. This scan stays as the local signal.
eslint_disable_hits="$(
    grep -rn --include='*.ts' --include='*.tsx' --include='*.mts' --include='*.cts' \
        --include='*.js' --include='*.cjs' --include='*.mjs' --include='*.jsx' \
        --include='*.html' --include='*.htm' --include='*.xhtml' --include='*.vue' \
        -E '(//|/\*)[[:space:]]*eslint-disable(-next-line|-line)?([[:space:]]*(\*/)?[[:space:]]*$|[[:space:]]*--|[[:space:]][^*]*(no-var|prefer-const))' \
        . 2>/dev/null \
        | grep -v -E '/(obj|bin|node_modules|dist)/'
)"
if [ -n "$eslint_disable_hits" ]; then
    report 'Blanket eslint-disable, or one aimed at no-var/prefer-const:' "$eslint_disable_hits"
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
