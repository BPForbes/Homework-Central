#!/usr/bin/env bash
# Rejects reviewer, Security and QA scratch files on the committed timeline.
#
# Only Coder edits belong in history. Reviewers and QA routinely create probe
# files to prove a gate fires — a `.cs` that must sit inside a real project to
# exercise the compiler, a nested MSBuild file, a throwaway `.js`. Those are
# gitignored by the reserved `_scratch/` and `*.scratch.*` names, but .gitignore
# only covers files nobody force-added, so this is the backstop.
#
# Checks the tracked file list, not the working tree, so it stays correct when
# run from CI on a fresh checkout.

set -uo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root" || exit 2

failed=0

report() {
    printf '\n%s\n' "$1"
    printf '%s\n' "$2"
    failed=1
}

# 1. Reserved scratch names must never be tracked.
scratch_tracked="$(git ls-files | grep -E '(^|/)_scratch/|\.scratch\.' || true)"
if [ -n "$scratch_tracked" ]; then
    report 'Reviewer/QA scratch files are committed (reserved names):' "$scratch_tracked"
fi

# 2. Thought files are gitignored, but a force-add would slip them in and every
#    later rewrite would keep the blob.
thoughts_tracked="$(
    git ls-files | grep -E '^\.cursor/thoughts/' \
        | grep -v -E '^\.cursor/thoughts/non-finalized/\.gitkeep$' || true
)"
if [ -n "$thoughts_tracked" ]; then
    report 'Thought files are committed (only .gitkeep is allowed):' "$thoughts_tracked"
fi

# 3. Local analysis output that review runs produce.
analysis_tracked="$(
    git ls-files | grep -E '^\.codeql-db|\.sarif$|^\.code-review-graph/|^\.codegraph/' || true
)"
if [ -n "$analysis_tracked" ]; then
    report 'Local analysis output is committed:' "$analysis_tracked"
fi

if [ "$failed" -ne 0 ]; then
    printf '\nclean-timeline check failed. Delete the file and amend, or add it to\n'
    printf '.gitignore if it is genuinely transient. See AGENTS.md.\n'
    exit 1
fi

printf 'clean-timeline check passed.\n'
