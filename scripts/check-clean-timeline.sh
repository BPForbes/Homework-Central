#!/usr/bin/env bash
# Rejects reviewer, Security and QA process output on the committed timeline.
#
# Only product, pipeline, infra, test code and durable docs/ updates belong in
# history. Reviewers and QA routinely create probe files to prove a gate fires —
# a .cs that must sit inside a real project to exercise the compiler, a nested
# MSBuild file, a throwaway .js. Those use the reserved gitignored `_scratch/`
# and `*.scratch.*` names, but .gitignore only covers files nobody force-added,
# so this is the backstop.
#
# Two modes:
#   (default)          check the tip: is anything non-Coder tracked right now.
#   --history <base>   also check every commit in <base>..HEAD. A file added in
#                      one commit and deleted in a later one is absent from both
#                      the tip and from `git diff <base>...HEAD` (a net diff),
#                      yet its blob ships to every clone forever. That is not a
#                      hypothetical: it is how .cursor/reviews/rust-optimization.md
#                      reached this branch's history while the tip was clean.
#
# Matching is case-insensitive throughout. .gitignore cannot case-fold portably,
# and core.ignorecase defaults to true on macOS and Windows, so `_Scratch/` is
# silently ignored there and tracked on Linux. Making this layer case-insensitive
# is what stops the convention behaving differently per developer platform.

set -uo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root" || exit 2

history_base=""
while [ "$#" -gt 0 ]; do
    case "$1" in
        --history)
            history_base="${2:-}"
            [ -n "$history_base" ] || { printf 'usage: %s [--history <base-ref>]\n' "$0" >&2; exit 2; }
            shift 2
            ;;
        -h|--help)
            printf 'usage: %s [--history <base-ref>]\n' "$0"
            exit 0
            ;;
        *)
            printf 'unknown argument: %s\n' "$1" >&2
            exit 2
            ;;
    esac
done

# Every check below ends in `|| true`, because grep exits 1 when it matches
# nothing. That also swallows a failing git, which would turn this backstop into
# a rubber stamp: "git broke" and "nothing to report" would print the same
# thing. Fail loudly here instead, so a bad GIT_DIR or a safe.directory
# rejection in a container cannot pass as a clean run.
git rev-parse --git-dir >/dev/null 2>&1 || {
    printf 'not a git repository: %s\n' "$repo_root" >&2
    exit 2
}
# core.quotePath=false on every invocation that produces a path. By default git
# wraps a path containing a non-ASCII byte, a control character, `"` or `\` in
# double quotes, and that trailing quote defeats the `$` anchor in every pattern
# below (a leading one defeats `^`). Two reserved names spelled with a single
# accented character were tracked while this script printed "passed" and exited
# 0 — the backstop reporting clean on exactly what it exists to catch.
tracked="$(git -c core.quotePath=false ls-files)" || {
    printf 'git ls-files failed in %s\n' "$repo_root" >&2
    exit 2
}

failed=0

report() {
    printf '\n%s\n' "$1"
    printf '%s\n' "$2"
    failed=1
}

# Reserved scratch names, at any depth. `\.scratch(\.|$)` so an extensionless
# `Probe.scratch` is covered as well as `Probe.scratch.cs`.
scratch_re='(^|/)_scratch/|\.scratch(\.|$)'
# Local analysis output. Anchored to any path segment rather than the repository
# root, matching how .gitignore already treats these directories.
analysis_re='(^|/)\.codeql-db|\.sarif$|(^|/)\.code-review-graph/|(^|/)\.codegraph/'
# Thought files. Only the keepfile may be tracked.
thoughts_re='^\.cursor/thoughts/'
keepfile='^\.cursor/thoughts/non-finalized/\.gitkeep$'
# A nested .gitignore can re-include the reserved names for its whole subtree.
# The root one is excluded by a second filter at each use site.
nested_gitignore_re='(^|/)\.gitignore$'

# 1. Reserved scratch names must never be tracked.
scratch_tracked="$(printf '%s\n' "$tracked" | grep -Ei "$scratch_re" || true)"
if [ -n "$scratch_tracked" ]; then
    report 'Reviewer/QA scratch files are committed (reserved names):' "$scratch_tracked"
fi

# 2. Thought files are gitignored, but a force-add would slip one in and every
#    later rewrite would carry the blob.
thoughts_tracked="$(
    printf '%s\n' "$tracked" | grep -Ei "$thoughts_re" | grep -v -E "$keepfile" || true
)"
if [ -n "$thoughts_tracked" ]; then
    report 'Thought files are committed (only .gitkeep is allowed):' "$thoughts_tracked"
fi

# 3. Local analysis output that a review run produces.
analysis_tracked="$(printf '%s\n' "$tracked" | grep -Ei "$analysis_re" || true)"
if [ -n "$analysis_tracked" ]; then
    report 'Local analysis output is committed:' "$analysis_tracked"
fi

# 4. Review write-ups. These belong in .cursor/thoughts/non-finalized/.
reviews_tracked="$(printf '%s\n' "$tracked" | grep -Ei '^\.cursor/reviews/' || true)"
if [ -n "$reviews_tracked" ]; then
    report 'Review write-ups are committed (use .cursor/thoughts/non-finalized/):' "$reviews_tracked"
fi

# 5. Any .gitignore other than the root one. A nested .gitignore can re-include
#    the reserved names for its whole subtree (`!*.scratch.*`), which defeats the
#    first layer entirely — and it is a fixed name, so it cannot itself be
#    reserved. Rejecting it mirrors how check-no-var.sh treats a nested
#    .editorconfig, and is equally safe: the repository has exactly one.
nested_gitignore="$(
    printf '%s\n' "$tracked" | grep -Ei "$nested_gitignore_re" | grep -v -E '^\.gitignore$' || true
)"
if [ -n "$nested_gitignore" ]; then
    report 'Only the repository root .gitignore is allowed (a nested one can re-include reserved names):' "$nested_gitignore"
fi

# 6. History. Skipped unless --history is given, because it needs a base ref and
#    enough history to walk.
if [ -n "$history_base" ]; then
    if ! git rev-parse --verify --quiet "$history_base" >/dev/null; then
        printf 'unknown base ref: %s\n' "$history_base" >&2
        exit 2
    fi

    # A shallow clone has the tip commits but not the range, so the walk below
    # returns fewer paths and the check quietly under-reports. Verified: the same
    # base ref gives exit 1 on a full clone and exit 0 at --depth 2. Refuse
    # rather than pass, because a silent under-scan is worse than no scan.
    if [ "$(git rev-parse --is-shallow-repository)" = 'true' ]; then
        printf 'refusing to scan history in a shallow clone: fetch with fetch-depth 0\n' >&2
        exit 2
    fi

    # Every path added anywhere in the range, not the net diff, so a path added
    # in one commit and deleted in a later one is still seen. Three flags carry
    # weight here:
    #
    #   -c diff.renames=false  a rename is reported as R, not A, so
    #                          `git mv notes.md leak.scratch.md` slipped past
    #                          --diff-filter=A entirely while the blob stayed
    #                          reachable. Disabling detection makes every rename
    #                          an add of the new path plus a delete of the old.
    #   -m                     without it `git log --name-only` prints nothing at
    #                          all for a merge commit, so a path first introduced
    #                          by a merge was invisible.
    #   --no-abbrev            paths are what we match on; never let git shorten.
    #
    # git failure must not read as "nothing found". Capture the status before the
    # pipeline, because `|| true` on the whole thing is exactly how a broken
    # GIT_DIR used to print "passed".
    # Paths the base already tracks are not this range's doing. The -m flag
    # above reports, for a merge commit, every path added relative to *each*
    # parent — so a file that lives on the base branch looks "added" when the
    # merge is viewed from the other side. Without this subtraction the scan
    # blamed this branch for .cursor/reviews/ai-library-optimization.md, which
    # exists at the base and which this branch only deletes.
    #
    # It also settles the more general question: a reviewer is answerable for
    # what their range introduces, not for what they inherited. Cleaning the
    # base is a separate change against the base.
    #
    # Keyed on (blob, path), never on path alone. Exempting a path outright
    # exempts it for any *content*: deleting the inherited file, re-adding the
    # same path with a fresh write-up, and deleting it again passed with the new
    # text sitting in history. Inheritance is about the exact bytes already in
    # the base, so compare object ids — the real false positive this fixes has an
    # identical blob on both sides.
    base_blobs="$(git -c core.quotePath=false ls-tree -r "$history_base" | awk '{print $3 "\t" substr($0, index($0, $4))}')" || {
        printf 'cannot read the tree at %s\n' "$history_base" >&2
        exit 2
    }
    # Walks the range carrying the blob id of each added path, so inherited
    # bytes can be told from new bytes at the same path. --raw gives ':mode mode preoid postoid A\tpath'.
    history_pairs="$(
        git -c diff.renames=false -c core.quotePath=false \
            log -m --diff-filter=A --raw --no-abbrev \
            --format='' "$history_base..HEAD"
    )" || {
        printf 'git log failed over %s..HEAD; cannot verify history\n' "$history_base" >&2
        exit 2
    }
    history_added="$(
        printf '%s\n' "$history_pairs" \
            | awk -F'\t' 'NF>1 { split($1, f, " "); print f[4] "\t" $2 }' \
            | sort -u \
            | { [ -n "$base_blobs" ] && grep -Fxv -f <(printf '%s\n' "$base_blobs") || cat; } \
            | cut -f2- \
            | grep -Ei "$scratch_re|$analysis_re|$thoughts_re|^\.cursor/reviews/|$nested_gitignore_re" \
            | grep -v -E "$keepfile" \
            | grep -v -E '^\.gitignore$' \
            | sort -u || true
    )"
    if [ -n "$history_added" ]; then
        report "Non-Coder output was committed earlier in $history_base..HEAD (the blob ships to every clone even though the tip is clean):" "$history_added"
    fi
fi

if [ "$failed" -ne 0 ]; then
    printf '\nclean-timeline check failed.\n'
    printf 'Tip findings: delete the file and amend.\n'
    printf 'History findings: strip the path from every commit in the range before\n'
    printf 'replaying — see thoughts-layout.md, One push. See also AGENTS.md.\n'
    exit 1
fi

printf 'clean-timeline check passed.\n'
