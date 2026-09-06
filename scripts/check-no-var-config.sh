#!/usr/bin/env bash
# Asserts the no-var rules are actually in force, by making them fire.
#
# Every earlier version of this gate inspected configuration and drew a
# conclusion. Each time, review found a route the inspection did not model:
#
#   text scan of <NoWarn>      `<!-- vendored --><NoWarn>$(NoWarn);IDE0008`
#                              began with a comment opener that closed mid-line
#   -getProperty:NoWarn        reports evaluation-time values, so a root
#                              Directory.Build.targets that appends to NoWarn
#                              inside a *target* is invisible
#   parsed .editorconfig       ignores [section] headers, so scoping the
#                              severities to [docs/*.cs], or restating them
#                              under a trailing [*.nomatch], satisfies the
#                              parser while real code goes unchecked
#   eslint --print-config on   a block scoped to src/components/** with
#   four sample files          'no-var': 'off' never gets sampled, and an
#                              `ignores` entry removes files from the lint run
#                              while the samples still resolve
#
# In every case `dotnet build` or `npx eslint` then accepted a real implicitly
# typed local. The lesson is that modelling a configuration language is the same
# treadmill as modelling a programming language with a regex. So this script no
# longer reasons about configuration at all. It plants a `var`, runs the real
# toolchain over the real tree, and requires the toolchain to reject it.
#
# Modes, because the two halves need different toolchains and a gate that skips
# itself is not a gate:
#   --csharp   needs dotnet.  Run it from the job that has dotnet.
#   --web      needs frontend/node_modules.  Run it from the job that has node.
#   (neither)  run whichever half is available; skip the other unless STRICT=1.
#
# STRICT=1 turns a skip into a failure. CI sets it, and each CI job passes the
# flag for the half it can actually run — asserting the other half from a job
# without its toolchain is how this script turned CI red for five runs.

set -uo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root" || exit 2

want_csharp=0
want_web=0
while [ "$#" -gt 0 ]; do
    case "$1" in
        --csharp) want_csharp=1; shift ;;
        --web) want_web=1; shift ;;
        -h|--help)
            printf 'usage: %s [--csharp] [--web]   (default: both, skipping absent toolchains)\n' "$0"
            exit 0
            ;;
        *) printf 'unknown argument: %s\n' "$1" >&2; exit 2 ;;
    esac
done
if [ "$want_csharp" -eq 0 ] && [ "$want_web" -eq 0 ]; then
    want_csharp=1
    want_web=1
    explicit=0
else
    explicit=1
fi

# Anything set and not an explicit off-value counts as strict. Matching only
# '1' meant STRICT=true, STRICT=yes and STRICT=on all silently disabled the
# very behaviour they were spelled to enable.
case "$(printf '%s' "${STRICT:-0}" | tr '[:upper:]' '[:lower:]')" in
    ''|0|false|no|off) strict=0 ;;
    *) strict=1 ;;
esac
failed=0

fail() { printf '\nFAIL: %s\n' "$1"; failed=1; }
note() { printf '  %s\n' "$1"; }

skip() {
    # An explicitly requested half must never silently skip: the caller said it
    # has that toolchain.
    if [ "$strict" = '1' ] || [ "$explicit" = '1' ]; then
        fail "$1"
    else
        printf 'skip: %s\n' "$1"
    fi
}

# Probe files use the reserved gitignored .scratch infix, so a crash mid-run
# cannot leave a committable file behind, and the clean-timeline guard would
# reject one anyway.
probe_files=()
cleanup() {
    local f
    for f in "${probe_files[@]:-}"; do
        [ -n "$f" ] && rm -f "$f"
    done
}
trap cleanup EXIT

# ------------------------------------------------------------------- C# ------
# One probe per project rather than per solution: each project has its own
# effective .editorconfig section, its own NoWarn, and its own analyzer
# settings, and a section-scoped severity is precisely the bypass that a single
# whole-solution probe would miss.
if [ "$want_csharp" -eq 1 ]; then
    if ! command -v dotnet >/dev/null 2>&1; then
        skip 'dotnet not installed; C# analyzer probe not run'
    else
        printf 'C# analyzer probe (planting an implicitly typed local per project):\n'
        # A loop over nothing succeeds, so a gate that iterates projects has to
        # say how many it found. Renaming every .csproj, or running from a
        # subdirectory, would otherwise report "check passed".
        project_count="$(git ls-files '*.csproj' | grep -c . || true)"
        if [ "${project_count:-0}" -eq 0 ]; then
            fail 'no tracked .csproj found, so the C# probe verified nothing'
        fi
        while IFS= read -r proj; do
            [ -n "$proj" ] || continue
            # `dirname --`, because a project named `-p:WarningLevel=0.csproj`
            # is parsed as an option otherwise: proj_dir comes back empty and the
            # probe target becomes `/NoVarAnalyzerProbe.scratch.cs`, at the
            # filesystem root. Unprivileged that fails on permissions; as root it
            # writes outside the repo.
            proj_dir="$(dirname -- "$proj")"
            probe="$proj_dir/NoVarAnalyzerProbe.scratch.cs"
            probe_files+=("$probe")

            # A distinct type name per project keeps two probes from colliding
            # if a project ever includes a sibling's sources.
            printf 'namespace NoVarAnalyzerProbe;\ninternal static class Probe%s\n{\n    internal static int Run()\n    {\n        var value = 1;\n        return value;\n    }\n}\n' \
                "$(printf '%s' "$proj_dir" | tr -cd '[:alnum:]')" > "$probe"

            out="$(dotnet build -c Release --nologo -- "$proj" 2>&1)"
            status=$?
            rm -f "$probe"

            if [ "$status" -eq 0 ]; then
                fail "$proj: a real \`var value = 1;\` compiled successfully. The IDE0008 analyzer is not in force for this project — check NoWarn (including target-time appends in a .targets), EnforceCodeStyleInBuild, the .editorconfig section that matches this directory, and dotnet_analyzer_diagnostic.category-Style.severity."
            elif ! printf '%s' "$out" | grep -q 'IDE0008'; then
                # The build failed, but not for the reason being tested. Reading
                # that as a pass would make any broken build look like a working
                # gate.
                fail "$proj: build failed without reporting IDE0008, so the probe proves nothing. First errors:
$(printf '%s' "$out" | grep -E 'error|warning' | head -5)"
            else
                note "$proj rejected it (IDE0008)"
            fi
        done <<< "$(git ls-files '*.csproj')"

        # A .cs file that no project compiles is analysed by nothing. The
        # solution does not cover PostgresHostCheck or DevHost, which is why CI
        # builds them separately, and a new stray source root would repeat that.
        unowned=0
        while IFS= read -r cs; do
            [ -n "$cs" ] || continue
            owned=0
            while IFS= read -r proj; do
                case "$cs" in "$(dirname "$proj")"/*) owned=1; break ;; esac
            done <<< "$(git ls-files '*.csproj')"
            if [ "$owned" -eq 0 ]; then
                fail "$cs is compiled by no tracked project, so no analyzer sees it"
                unowned=1
            fi
        done <<< "$(git ls-files '*.cs')"
        [ "$unowned" -eq 0 ] && note 'every tracked .cs belongs to a tracked project'
    fi
fi

# ------------------------------------------------------------------ web ------
# Enumerates every tracked web file instead of sampling. Sampling was a complete
# escape: one config block scoped to src/components/** with 'no-var': 'off', and
# a var in a file there passed every gate end to end.
if [ "$want_web" -eq 1 ]; then
    missing_web_tool=''
    for tool in node npm; do
        command -v "$tool" >/dev/null 2>&1 || missing_web_tool="$tool"
    done
    if [ -n "$missing_web_tool" ]; then
        # Named up front, because otherwise a missing interpreter surfaces as
        # "eslint probe did not run: node: command not found" followed by an
        # unrelated lint failure, which reads like a config problem.
        skip "$missing_web_tool is not on PATH; eslint probe not run"
    elif [ ! -d frontend/node_modules ]; then
        skip 'frontend/node_modules absent; eslint probe not run (run npm ci first)'
    else
        if ! (mapfile -d '' -t _probe < <(printf 'x\0')) 2>/dev/null; then
            fail "bash $BASH_VERSION cannot run this gate: \`mapfile -d\` needs bash 4.4+ (macOS ships 3.2). Install a newer bash, or run this gate in CI."
        fi
        # NUL-delimited into an array. A newline-separated string plus an
        # unquoted expansion split `scripts/two words.mjs` into two arguments
        # and eslint reported `No files matching the pattern "scripts/two"`.
        # That failed closed, but a legitimate filename with a space should not
        # break the gate, and a path can also contain a glob character or a
        # newline.
        mapfile -d '' -t all_web < <(
            git ls-files -z \
                '*.ts' '*.tsx' '*.mts' '*.cts' '*.js' '*.cjs' '*.mjs' '*.jsx' \
                '*.html' '*.htm' '*.xhtml'
        )
        web_files=''
        outside_files=()
        for path in "${all_web[@]:-}"; do
            [ -n "$path" ] || continue
            case "$path" in
                frontend/*) web_files="$web_files${path#frontend/}"$'\n' ;;
                *) outside_files+=("$path") ;;
            esac
        done
        web_files="${web_files%$'\n'}"

        # Web files outside frontend/ are covered by nothing unless something
        # says so: eslint refuses to lint above its base path, and reports the
        # refusal as a *warning* ("File ignored because outside of base path"),
        # which reads as success. scripts/novar-eslint-probe.mjs sat in exactly
        # that hole. The root eslint.config.mjs exists for these, and this loop
        # fails on any that neither config claims.
        if [ "${#outside_files[@]}" -gt 0 ]; then
            outside="$(printf '%s\n' "${outside_files[@]}")"
            if [ ! -f eslint.config.mjs ]; then
                fail "these tracked web files are outside frontend/ and there is no root eslint.config.mjs to cover them:
$outside"
            else
                # Asserted per file with --print-config rather than read off a
                # lint exit code. eslint reports both "outside of base path" and
                # "no matching configuration was supplied" as *warnings* and
                # exits 0, so an unclaimed file in a new directory looked exactly
                # like a clean lint. Resolving the config per file turns "nothing
                # claims this" into a hard answer.
                out_bad=''
                for rel in "${outside_files[@]}"; do
                    [ -n "$rel" ] || continue
                    # `--print-config=<path>`, not `--print-config -- <path>`:
                    # the flag takes the path as its own value, so `--` would be
                    # consumed as that value. The `=` form is also what keeps a
                    # dash-prefixed path from being read as another option.
                    rel_cfg="$(./frontend/node_modules/.bin/eslint --config eslint.config.mjs "--print-config=$rel" 2>&1)"
                    if ! printf '%s' "$rel_cfg" | grep -q '^{'; then
                        out_bad="$out_bad
  $rel: no config resolves for it ($(printf '%s' "$rel_cfg" | head -1))"
                        continue
                    fi
                    rel_verdict="$(
                        printf '%s' "$rel_cfg" | python3 -c '
import json, sys
rules = json.load(sys.stdin).get("rules", {})
bad = []
for name in ("no-var", "prefer-const"):
    entry = rules.get(name)
    sev = entry[0] if isinstance(entry, list) and entry else entry
    if sev not in (2, "error"):
        bad.append("%s=%r" % (name, sev))
print("; ".join(bad))
' 2>/dev/null
                    )"
                    [ -n "$rel_verdict" ] && out_bad="$out_bad
  $rel: $rel_verdict"
                done

                out_lint="$(./frontend/node_modules/.bin/eslint --config eslint.config.mjs -- "${outside_files[@]}" 2>&1)"
                out_status=$?
                if [ -n "$out_bad" ]; then
                    fail "web files outside frontend/ that no root config block covers with error severity:$out_bad
Add them to eslint.config.mjs, or move them under frontend/."
                elif [ "$out_status" -ne 0 ]; then
                    fail "eslint reported problems in web files outside frontend/:
$out_lint"
                else
                    note "root config covers ${#outside_files[@]} web file(s) outside frontend/"
                fi
            fi
        fi
        if [ -z "$web_files" ]; then
            fail 'no tracked web files found; the enumeration globs are wrong'
        else
            eslint_report="$(
                printf '%s\n' "$web_files" \
                    | (cd frontend && node ../scripts/novar-eslint-probe.mjs) 2>&1
            )"
            verdict="$(printf '%s' "$eslint_report" | tail -1)"
            if ! printf '%s' "$verdict" | grep -q '^{'; then
                fail "eslint probe did not run:
$eslint_report"
            else
                checked="$(printf '%s' "$verdict" | python3 -c 'import json,sys; print(json.load(sys.stdin)["checked"])' 2>/dev/null)"
                problems="$(printf '%s' "$verdict" | python3 -c 'import json,sys; print("\n".join(json.load(sys.stdin)["problems"]))' 2>/dev/null)"
                if [ -n "$problems" ]; then
                    fail "eslint effective config over $checked tracked web files:
$problems"
                else
                    note "eslint: no-var and prefer-const are error for all $checked tracked web files"
                fi
            fi

            # Behavioural check of the inline-config half, replacing a grep for
            # `--no-inline-config` in package.json. The grep was satisfied by
            # narrowing the script to a single file, which left a blanket
            # /* eslint-disable */ working everywhere else. Planting the probe in
            # the real source tree and running the real script means the
            # assertion fails if the script stops covering that tree — whether by
            # narrowing its arguments or by ignoring the directory.
            inline_probe='frontend/src/novar_inline_probe.scratch.ts'
            probe_files+=("$inline_probe")
            printf '/* eslint-disable */\nvar x = 1;\nexport default x;\n' > "$inline_probe"
            if ! (cd frontend && npm run --silent lint:ci) >/dev/null 2>&1; then
                note 'npm run lint:ci rejects a blanket /* eslint-disable */ hiding a var'
            else
                fail 'npm run lint:ci accepted a blanket /* eslint-disable */ hiding a `var` in frontend/src. Either it is missing --no-inline-config, or it no longer lints the whole source tree.'
            fi
            rm -f "$inline_probe"

            # And the plain lint script must stay clean, so the probe above is
            # measuring the flag rather than a pre-existing failure.
            if ! (cd frontend && npm run --silent lint) >/dev/null 2>&1; then
                fail 'npm run lint fails on the unmodified tree, so the lint:ci result above is not attributable to --no-inline-config'
            fi
        fi
    fi
fi

if [ "$failed" -ne 0 ]; then
    printf '\nno-var enforcement check failed.\n'
    printf 'This gate plants a real implicitly typed local and requires the toolchain\n'
    printf 'to reject it, so a failure means the rule is genuinely not in force —\n'
    printf 'not that a pattern needs adjusting. See AGENTS.md.\n'
    exit 1
fi

printf 'no-var enforcement check passed.\n'
