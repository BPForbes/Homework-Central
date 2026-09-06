#!/usr/bin/env bash
# Asserts that the no-var rules are actually ON, by asking each toolchain what
# its effective configuration is.
#
# Why this exists as a separate gate:
#
# The rest of the enforcement assumes the analyzers are enabled. Nothing checked
# that assumption, and it turned out to be one word deep. Two verified bypasses:
#
#   frontend/eslint.config.js: 'no-var': 'error' -> 'off'
#       Both existing gates stayed green. The C# side of the config was guarded
#       against tampering; the web side was guarded by nothing, and after the
#       split the entire web half of the rule rested on that one file.
#
#   Directory.Build.props: <!-- vendored --><NoWarn>$(NoWarn);IDE0008</NoWarn>
#       Invisible to a text scan that skips comment lines, because the line
#       begins with a comment opener that closes mid-line. `dotnet build` then
#       compiled a real `var z = 1` with "Build succeeded".
#
# Grepping for the forbidden text is what produced that second one, and a
# false positive, and a fix, in three consecutive review rounds. So this script
# does not grep. It asks MSBuild to evaluate the property and asks eslint to
# resolve the config, then asserts on the answer. An evaluated property and a
# resolved rule severity cannot be spoofed by a comment, a string, unusual
# whitespace, an unfamiliar file extension, or a syntax nobody thought of,
# because the tool that will act on the config is the one reporting it.
#
# Needs the toolchains, so it is a separate script from check-no-var.sh, which
# is deliberately dependency-free and runs in a job with no toolchain at all.
# Skips a probe only when the tool is genuinely absent, unless STRICT=1.

set -uo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root" || exit 2

strict="${STRICT:-0}"
failed=0

fail() {
    printf '\nFAIL: %s\n' "$1"
    failed=1
}

skip() {
    if [ "$strict" = '1' ]; then
        fail "$1 (STRICT=1, and this probe is the gate)"
    else
        printf 'skip: %s\n' "$1"
    fi
}

# ---------------------------------------------------------------- MSBuild ----
# EnforceCodeStyleInBuild must be on, or the .editorconfig :error severities are
# editor-only decoration. NoWarn must not name the two IDE rules, by any route:
# the root props, a csproj, a Directory.Build.targets, a response file, or an
# inherited $(NoWarn). -getProperty reports the value the compiler will see.
if command -v dotnet >/dev/null 2>&1; then
    while IFS= read -r proj; do
        [ -n "$proj" ] || continue

        enforce="$(dotnet msbuild "$proj" -getProperty:EnforceCodeStyleInBuild 2>/dev/null)" || {
            fail "cannot evaluate EnforceCodeStyleInBuild for $proj"
            continue
        }
        # Trailing whitespace and CR, because -getProperty prints a bare value.
        enforce="$(printf '%s' "$enforce" | tr -d '[:space:]')"
        if [ "$enforce" != 'true' ]; then
            fail "$proj: EnforceCodeStyleInBuild is '$enforce', expected 'true' (without it the .editorconfig :error severities never run at build)"
        fi

        nowarn="$(dotnet msbuild "$proj" -getProperty:NoWarn 2>/dev/null)" || {
            fail "cannot evaluate NoWarn for $proj"
            continue
        }
        case "$(printf '%s' "$nowarn" | tr -d '[:space:]')" in
            *IDE0007*|*IDE0008*)
                fail "$proj: NoWarn evaluates to '$nowarn', which suppresses the no-var analyzer"
                ;;
        esac

        severity="$(dotnet msbuild "$proj" -getProperty:CodeAnalysisTreatWarningsAsErrors 2>/dev/null || true)"
        : "$severity"
    done <<< "$(git ls-files '*.csproj')"
    printf 'msbuild property probe done (%s projects).\n' "$(git ls-files '*.csproj' | wc -l | tr -d ' ')"
else
    skip 'dotnet not installed; MSBuild property probe not run'
fi

# ----------------------------------------------------------- .editorconfig ----
# The third route into a disabled analyzer, and the one no evaluated MSBuild
# property shows: the severity suffix. `csharp_style_var_elsewhere = false` with
# no suffix, or `:suggestion`, or a `dotnet_diagnostic.IDE0008.severity = none`,
# all leave EnforceCodeStyleInBuild true and NoWarn empty while the build stops
# failing.
#
# Parsed rather than grepped. .editorconfig is INI-shaped and line-oriented, so
# a parser is a few lines and has no comment-versus-code ambiguity — the problem
# that made the C# text scans unreliable does not exist here. Nested files are
# rejected outright by check-no-var.sh, so the root file is the whole surface.
editorconfig_verdict="$(
    python3 - <<'PY' 2>/dev/null
required = (
    'csharp_style_var_for_built_in_types',
    'csharp_style_var_when_type_is_apparent',
    'csharp_style_var_elsewhere',
)
values, diagnostics = {}, {}
with open('.editorconfig', encoding='utf-8') as handle:
    for raw in handle:
        line = raw.strip()
        if not line or line[0] in '#;[':
            continue
        if '=' not in line:
            continue
        key, _, value = line.partition('=')
        key, value = key.strip().lower(), value.strip()
        if key in required:
            values[key] = value
        elif key.startswith('dotnet_diagnostic.ide000') and key.endswith('.severity'):
            diagnostics[key] = value.lower()

problems = []
for key in required:
    value = values.get(key)
    if value is None:
        problems.append('%s is missing' % key)
    elif not value.lower().endswith(':error'):
        # A bare `false` silently falls back to the default suggestion severity,
        # which cannot fail a build. Only an explicit :error does.
        problems.append('%s = %s (needs :error)' % (key, value))
for key, value in diagnostics.items():
    if value != 'error':
        problems.append('%s = %s (needs error)' % (key, value))
print('; '.join(problems))
PY
)" || editorconfig_verdict='could not parse .editorconfig'
if [ -n "$editorconfig_verdict" ]; then
    fail ".editorconfig: $editorconfig_verdict"
else
    printf 'editorconfig severity probe done.\n'
fi

# ----------------------------------------------------------------- eslint ----
# --print-config resolves the flat config the way a lint run would, including
# every override block, and reports each rule as [severity, ...options]. 2 is
# error. One representative file per config block, so a block that quietly stops
# matching a file type is caught too: index.html covers the HTML processor.
if [ -d frontend/node_modules ]; then
    probe_files='src/main.tsx src/vite-env.d.ts eslint.config.js index.html'
    for rel in $probe_files; do
        [ -f "frontend/$rel" ] || { fail "eslint probe target frontend/$rel is missing; update this list"; continue; }
        cfg="$(cd frontend && npx eslint --print-config "$rel" 2>/dev/null)" || {
            fail "eslint --print-config failed for frontend/$rel"
            continue
        }
        verdict="$(
            printf '%s' "$cfg" | python3 -c '
import json, sys
cfg = json.load(sys.stdin)
rules = cfg.get("rules", {})
bad = []
for name in ("no-var", "prefer-const"):
    entry = rules.get(name)
    sev = entry[0] if isinstance(entry, list) and entry else entry
    if sev not in (2, "error"):
        bad.append("%s=%r" % (name, sev))
print("; ".join(bad))
' 2>/dev/null
        )" || verdict='probe error'
        if [ -n "$verdict" ]; then
            fail "frontend/$rel: $verdict (expected error/2 for both)"
        fi
    done

    # noInlineConfig is deliberately NOT set, because it would also kill the one
    # legitimate warn-level react-hooks/exhaustive-deps directive. Instead the
    # CI lint runs --no-inline-config, so a blanket `/* eslint-disable */` cannot
    # hide a var there. Assert that wiring exists rather than trusting it.
    if ! grep -q -- '--no-inline-config' frontend/package.json; then
        fail 'frontend/package.json has no --no-inline-config lint script; a blanket /* eslint-disable */ would suppress no-var'
    fi
    printf 'eslint effective-config probe done.\n'
else
    skip 'frontend/node_modules absent; eslint config probe not run'
fi

if [ "$failed" -ne 0 ]; then
    printf '\nno-var config check failed: the rule is configured off or suppressed.\n'
    printf 'See AGENTS.md for which gate owns which case.\n'
    exit 1
fi

printf 'no-var config check passed.\n'
