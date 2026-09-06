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
# A probe in every directory that holds tracked source, with a name that is
# different on every run, and the diagnostic attributed to each probe by path.
#
# One probe per project at the project root was not enough. The probe's address
# was fixed and predictable, so four tamperings routed around it while a real
# `var` in shipping code compiled:
#
#   [**/Services/*.cs] IDE0008.severity = none   scopes the disable to a
#   [**/Services/*.cs] generated_code = true     directory the probe never
#   <Compile Remove="Legacy/**/*.cs" />          occupied
#
#   Directory.Build.targets with an <Error Text="IDE0008..."> conditioned on
#   Exists(.../NoVarAnalyzerProbe.scratch.cs) -- which satisfied the "the build
#   failed for another reason" guard, because that guard only looked for the
#   string IDE0008 anywhere in the output, while the analyzer never ran at all.
#
# So: a probe per source directory defeats any section- or subtree-scoped
# disable, a random name per run cannot be named in a condition or a Remove
# glob, and requiring `error IDE0008` on a line naming that specific probe
# defeats a forged error message. All probes for a project go into one build,
# so this costs the same four builds as before.
if [ "$want_csharp" -eq 1 ]; then
    if ! command -v dotnet >/dev/null 2>&1; then
        skip 'dotnet not installed; C# analyzer probe not run'
    else
        printf 'C# analyzer probe (planting implicitly typed locals):\n'
        # A loop over nothing succeeds, so a gate that iterates projects has to
        # say how many it found. Renaming every .csproj, or running from a
        # subdirectory, would otherwise report "check passed".
        # NUL-delimited, like the web half. A newline in a path split one
        # entry into two, and the halves named no project, so the loop reported
        # a failure for a path that did not exist while the real project went
        # unprobed.
        mapfile -d '' -t csproj_files < <(git -c core.quotePath=false ls-files -z '*.csproj')
        project_count=0
        for proj in "${csproj_files[@]:-}"; do
            [ -n "$proj" ] && project_count=$((project_count + 1))
        done
        if [ "${project_count:-0}" -eq 0 ]; then
            fail 'no tracked .csproj found, so the C# probe verified nothing'
        fi

        # Random per run. A fixed name is an address a condition can test for or
        # a Compile Remove glob can name.
        probe_tag="NoVarProbe$(od -An -tx1 -N4 /dev/urandom 2>/dev/null | tr -cd '[:alnum:]' || printf '%s' "$$")"

        for proj in "${csproj_files[@]:-}"; do
            [ -n "$proj" ] || continue
            # `dirname --`, because a project named `-p:WarningLevel=0.csproj`
            # is parsed as an option otherwise: proj_dir comes back empty and the
            # probe target lands at the filesystem root.
            proj_dir="$(dirname -- "$proj")"

            # Every directory under this project that holds tracked .cs. That is
            # where real code lives, and therefore where a scoped disable would
            # be aimed.
            mapfile -d '' -t proj_sources < <(
                git -c core.quotePath=false ls-files -z \
                    "$proj_dir/*.cs" "$proj_dir/**/*.cs" 2>/dev/null
            )
            probe_dirs=()
            for src in "${proj_sources[@]:-}"; do
                [ -n "$src" ] || continue
                src_dir="$(dirname -- "$src")"
                case " ${probe_dirs[*]-} " in
                    *" $src_dir "*) ;;
                    *) probe_dirs+=("$src_dir") ;;
                esac
            done
            [ "${#probe_dirs[@]}" -gt 0 ] || probe_dirs=("$proj_dir")

            planted=()
            for d in "${probe_dirs[@]}"; do
                [ -n "$d" ] || continue
                probe="$d/$probe_tag.scratch.cs"
                probe_files+=("$probe")
                planted+=("$probe")
                # A type name unique per directory, so several probes can coexist
                # in one compilation.
                printf 'namespace %s;\ninternal static class C%s\n{\n    internal static int Run()\n    {\n        var value = 1;\n        return value;\n    }\n}\n' \
                    "$probe_tag" "$(printf '%s' "$d" | tr -cd '[:alnum:]')" > "$probe"
            done

            out="$(dotnet build -c Release --nologo -- "$proj" 2>&1)"
            status=$?

            # Each probe must be named on a line reporting error IDE0008 in
            # MSBuild's diagnostic format — `path(line,col): error IDE0008:` —
            # not "IDE0008 appears somewhere", which a forged <Error Text>
            # satisfied.
            missing=()
            for probe in "${planted[@]}"; do
                if ! printf '%s' "$out" | grep -F "$probe" | grep -Eq '\([0-9]+,[0-9]+\): error IDE0008'; then
                    missing+=("$probe")
                fi
                rm -f "$probe"
            done

            # A build that *succeeded* is a failure however complete the
            # attribution looks, and this ordering is the point. Checking
            # attribution first and treating a full set as a pass let a root
            # Directory.Build.targets print the expected line for every probe
            # with a <Message Importance="High"> over @(Compile) items ending in
            # .scratch.cs, while `dotnet_analyzer_diagnostic` silenced the real
            # analyzer: 42 of 42 attributed, exit 0, and a genuine `var` compiled
            # in ApplicationReadiness.cs. A Message cannot fail a build, so
            # requiring a non-zero status is what separates a diagnostic from a
            # line of text that looks like one.
            if [ "$status" -eq 0 ]; then
                fail "$proj: the build SUCCEEDED with a real \`var value = 1;\` in ${#planted[@]} source directories, so IDE0008 is not in force — regardless of what the output says. Check dotnet_analyzer_diagnostic.category-Style.severity, NoWarn (including target-time appends), EnforceCodeStyleInBuild, and any target that prints diagnostic-shaped text."
            elif [ "${#missing[@]}" -eq 0 ]; then
                note "$proj rejected all ${#planted[@]} (IDE0008 attributed to each, build failed)"
            else
                fail "$proj: the build failed, but IDE0008 was not attributed to ${#missing[@]} of ${#planted[@]} probes, so the probe proves nothing for those directories:
$(printf '  %s\n' "${missing[@]}" | head -8)
First errors:
$(printf '%s' "$out" | grep -E 'error|warning' | head -5)"
            fi
        done

        # A .cs file that no project compiles is analysed by nothing. The
        # solution does not cover PostgresHostCheck or DevHost, which is why CI
        # builds them separately, and a new stray source root would repeat that.
        unowned=0
        mapfile -d '' -t cs_files < <(git -c core.quotePath=false ls-files -z '*.cs')
        for cs in "${cs_files[@]:-}"; do
            [ -n "$cs" ] || continue
            owned=0
            for proj in "${csproj_files[@]:-}"; do
                [ -n "$proj" ] || continue
                case "$cs" in "$(dirname -- "$proj")"/*) owned=1; break ;; esac
            done
            if [ "$owned" -eq 0 ]; then
                fail "$cs is compiled by no tracked project, so no analyzer sees it"
                unowned=1
            fi
        done
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

        # Every tracked file must be classified, and anything unrecognised fails.
        #
        # This started as a denylist of script-bearing extensions, which is a
        # shape that cannot be finished: it was case-sensitive and partial, so
        # `RbWidget.VUE` walked past it, and so did `.es6`, `.jsm` and any other
        # extension nobody had thought of. Before that it was not there at all,
        # and a lower-case `.vue` holding `var x = 1` passed all four gates while
        # the summary read "all 130 tracked web files".
        #
        # An allowlist inverts the burden. A new file type in this repository is
        # now a decision someone has to record here, and until they do the gate
        # is red. Two lists, both matched on the lower-cased extension:
        #
        #   web_ext      eslint parses it, and the enumeration above globs it
        #   inert_ext    it cannot carry a JavaScript variable declaration, or
        #                another gate owns it (`.cs` belongs to Roslyn)
        declare -A web_ext=(
            [ts]=1 [tsx]=1 [mts]=1 [cts]=1 [js]=1 [cjs]=1 [mjs]=1 [jsx]=1
            [html]=1 [htm]=1 [xhtml]=1
        )
        declare -A inert_ext=(
            # C# and MSBuild — the Roslyn half of this gate owns these.
            [cs]=1 [csproj]=1 [sln]=1 [slnlaunch]=1 [props]=1 [targets]=1
            # Prose.
            [md]=1 [mdc]=1 [txt]=1
            # Configuration and data.
            [json]=1 [yaml]=1 [yml]=1 [toml]=1 [conf]=1 [lock]=1 [example]=1
            [editorconfig]=1 [gitignore]=1 [gitkeep]=1 [gitattributes]=1
            [dockerignore]=1 [dockerfile]=1 [env]=1
            # Other languages, each with its own toolchain.
            [sh]=1 [ps1]=1 [rs]=1 [py]=1
            # Styles.
            [css]=1 [scss]=1
            # Markup that eslint cannot parse; `svg` is checked for <script>
            # separately below rather than trusted.
            [svg]=1 [ico]=1 [png]=1 [jpg]=1 [jpeg]=1 [webp]=1 [woff]=1 [woff2]=1
        )
        # Files with no extension at all, matched on the whole lower-cased name.
        declare -A inert_name=([dockerfile]=1 [makefile]=1 [license]=1 [readme]=1)

        mapfile -d '' -t all_tracked < <(git -c core.quotePath=false ls-files -z)
        unclassified=()
        svg_files=()
        for path in "${all_tracked[@]:-}"; do
            [ -n "$path" ] || continue
            base="${path##*/}"
            lower_base="$(printf '%s' "$base" | tr '[:upper:]' '[:lower:]')"
            if [ "$lower_base" = "${lower_base%.*}" ]; then
                # No dot: classify on the name.
                [ -n "${inert_name[$lower_base]:-}" ] || unclassified+=("$path")
                continue
            fi
            ext="${lower_base##*.}"
            [ "$ext" = svg ] && svg_files+=("$path")
            if [ -z "${web_ext[$ext]:-}" ] && [ -z "${inert_ext[$ext]:-}" ]; then
                unclassified+=("$path")
            fi
        done

        # Classifying a file as eslint-covered is a claim, so check it against
        # what the enumeration actually collected. The globs above are
        # case-sensitive (`'*.ts'`), while this classification lower-cases the
        # extension, so `App.TSX` would be called covered and then never
        # enumerated, config-checked or linted — the claim and the coverage
        # drifting apart in the one direction that reads as success.
        declare -A enumerated=()
        for path in "${all_web[@]:-}"; do
            [ -n "$path" ] && enumerated["$path"]=1
        done
        claimed_not_enumerated=()
        for path in "${all_tracked[@]:-}"; do
            [ -n "$path" ] || continue
            base="${path##*/}"
            lower_base="$(printf '%s' "$base" | tr '[:upper:]' '[:lower:]')"
            [ "$lower_base" = "${lower_base%.*}" ] && continue
            ext="${lower_base##*.}"
            [ -n "${web_ext[$ext]:-}" ] || continue
            [ -n "${enumerated[$path]:-}" ] || claimed_not_enumerated+=("$path")
        done
        if [ "${#claimed_not_enumerated[@]}" -gt 0 ]; then
            fail "these tracked files have an eslint-covered extension but were not
picked up by the enumeration globs, so nothing checked them:
$(printf '  %s\n' "${claimed_not_enumerated[@]}" | head -20)
The globs are case-sensitive; the classification is not. Either rename the file
to the conventional lower-case extension, or add the casing to the globs."
        fi

        if [ "${#unclassified[@]}" -gt 0 ]; then
            fail "these tracked files have a type no no-var gate has been told about:
$(printf '  %s\n' "${unclassified[@]}" | head -20)
Classify each one in this script. If eslint can parse it, add the extension to
web_ext *and* to the enumeration globs above, and teach the eslint config to
lint it. If it cannot hold a JavaScript variable declaration, add it to
inert_ext with a comment saying why. Leaving it unlisted is the one option the
gate will not accept, because an unglobbed extension is invisible to every
check here."
        fi

        # SVG is inert by declaration, not by inspection: eslint cannot parse it,
        # so an <svg> holding a <script> element would be covered by nothing. The
        # match is case-insensitive because `<SCRIPT>` is equally valid HTML, and
        # the enumeration above is case-insensitive for the same reason — a
        # tracked `icon.SVG` was skipped entirely while `icon.svg` was checked.
        svg_with_script=()
        for path in "${svg_files[@]:-}"; do
            [ -n "$path" ] || continue
            grep -liq '<script' -- "$path" 2>/dev/null && svg_with_script+=("$path")
        done
        if [ "${#svg_with_script[@]}" -gt 0 ]; then
            fail "these tracked SVG files contain a <script> element, which no no-var gate can parse:
$(printf '  %s\n' "${svg_with_script[@]}")
Move the script into a .ts/.js module that eslint lints."
        fi

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
                # Resolved through the same node probe as everything else,
                # rather than `--print-config` piped into python3. eslint reports
                # both "outside of base path" and "no matching configuration was
                # supplied" as *warnings* and exits 0, so an unclaimed file in a
                # new directory looked exactly like a clean lint; resolving the
                # config per file turns "nothing claims this" into a hard answer.
                out_report="$(
                    printf '%s\n' "${outside_files[@]}" \
                        | node scripts/novar-eslint-probe.mjs --config eslint.config.mjs 2>&1
                )"
                out_bad="$(printf '%s\n' "$out_report" | sed -n 's/^problem //p')"
                if ! printf '%s\n' "$out_report" | grep -qE '^verdict (ok|problems)$'; then
                    fail "the root-config probe did not run to completion, so it proves nothing:
$out_report"
                fi

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
            # The probe's last line is its verdict. Requiring that line to be
            # present is what keeps a probe that died halfway — or never
            # started — from reading as "no problems found".
            if ! printf '%s\n' "$eslint_report" | grep -qE '^verdict (ok|problems)$'; then
                fail "eslint probe did not run to completion, so it proves nothing:
$eslint_report"
            else
                checked="$(printf '%s\n' "$eslint_report" | sed -n 's/^checked //p')"
                problems="$(printf '%s\n' "$eslint_report" | sed -n 's/^problem //p')"
                # A count that is absent, zero or non-numeric means the
                # enumeration did not happen, however clean the problem list looks.
                case "$checked" in
                    ''|*[!0-9]*) fail "the eslint probe reported no usable file count (got '$checked'), so its clean result proves nothing" ;;
                    0) fail 'the eslint probe resolved config for 0 files, so its clean result proves nothing' ;;
                esac
                if [ -n "$problems" ]; then
                    fail "eslint effective config over $checked tracked web files:
$problems"
                else
                    # "under frontend/", because the outside-frontend files are
                    # counted by their own note above. Saying "all 130 tracked
                    # web files" when 132 are tracked invited the reader to
                    # assume the two lists were one.
                    note "eslint: no-var and prefer-const are error for all $checked tracked web files under frontend/, and no var survives with inline directives ignored"
                fi
            fi

            # Behavioural check of the inline-config half, replacing a grep for
            # `--no-inline-config` in package.json. The grep was satisfied by
            # narrowing the script to a single file, which left a blanket
            # /* eslint-disable */ working everywhere else. Planting the probe in
            # the real source tree and running the real script means the
            # assertion fails if the script stops covering that tree — whether by
            # narrowing its arguments or by ignoring the directory.
            #
            # The rejection has to be attributed to the probe by name. A bare
            # "lint:ci exited non-zero" was satisfied by *any* pre-existing
            # error in the tree: with a real `var` behind a blanket disable in
            # frontend/src/main.tsx, lint:ci failed because of that var, and the
            # gate read its own probe as rejected and printed the reassuring
            # note. That is the same forged-signal shape as the C# probe's
            # `<Error Text="IDE0008">`, and it wants the same answer — require
            # the probe's own path in the output.
            # Random name, for the reason the C# probe has one: a fixed path is
            # an address that an `ignores` entry or a narrowed lint argument can
            # name. Attribution alone already fails closed if the probe goes
            # unmentioned, but there is no reason to hand out the address.
            inline_probe="frontend/src/novar_inline_$(od -An -tx1 -N4 /dev/urandom 2>/dev/null | tr -cd '[:alnum:]' || printf '%s' "$$").scratch.ts"
            probe_files+=("$inline_probe")
            printf '/* eslint-disable */\nvar x = 1;\nexport default x;\n' > "$inline_probe"
            inline_out="$(cd frontend && npm run --silent lint:ci 2>&1)"
            inline_status=$?
            rm -f "$inline_probe"

            if [ "$inline_status" -eq 0 ]; then
                fail 'npm run lint:ci accepted a blanket /* eslint-disable */ hiding a `var` in frontend/src. Either it is missing --no-inline-config, or it no longer lints the whole source tree.'
            elif ! printf '%s' "$inline_out" | grep -F "$(basename -- "$inline_probe")" | grep -q .; then
                fail "npm run lint:ci failed, but never mentioned the probe, so its failure proves nothing about --no-inline-config:
$(printf '%s' "$inline_out" | grep -E 'error|Error' | head -5)"
            else
                note 'npm run lint:ci rejects a blanket /* eslint-disable */ hiding a var'
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
