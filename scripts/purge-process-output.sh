#!/bin/sh
# Delete DevOps research, review, and handoff process files.
#
# This is the authorized one-push cleanup. It must not run during
# research, implementation, review, or security work — those files
# are the working record until both gates exist.
#
# Required attestations (both):
#   --security-clear   Security recorded Clear on the change
#   --qa-pass          QA marked the publish gate PASS
#
# Does not git add, commit, or push. After this script, the
# Orchestrator may perform the single authorized push.

set -eu

usage() {
  echo "Usage: $0 --security-clear --qa-pass [--repo DIR] [--dry-run]" >&2
  exit 2
}

REPO=""
DRY_RUN=0
SECURITY_CLEAR=0
QA_PASS=0

while [ "$#" -gt 0 ]; do
  case "$1" in
    --security-clear) SECURITY_CLEAR=1; shift ;;
    --qa-pass) QA_PASS=1; shift ;;
    --repo) REPO="${2:-}"; shift 2 ;;
    --dry-run) DRY_RUN=1; shift ;;
    -h|--help) usage ;;
    *) echo "Unknown option: $1" >&2; usage ;;
  esac
done

if [ "$SECURITY_CLEAR" -ne 1 ] || [ "$QA_PASS" -ne 1 ]; then
  echo "Refusing to delete process output: need --security-clear and --qa-pass." >&2
  echo "Those flags attest Security Clear and QA PASS. Do not invent them." >&2
  exit 2
fi

if [ -z "$REPO" ]; then
  REPO=$(pwd)
fi
REPO=$(CDPATH= cd -- "$REPO" && pwd)

say() {
  echo "$1"
}

remove_path() {
  path="$1"
  if [ ! -e "$path" ] && [ ! -L "$path" ]; then
    return
  fi
  if [ "$DRY_RUN" -eq 1 ]; then
    say "dry-run: rm -rf $path"
    return
  fi
  rm -rf "$path"
  say "deleted $path"
}

# Keep only non-finalized/.gitkeep under a thoughts root.
purge_thoughts_root() {
  root="$1"
  if [ ! -d "$root" ]; then
    return
  fi
  if [ -d "$root/non-finalized" ]; then
    # -depth: remove children before parents so find does not walk a deleted dir.
    find "$root/non-finalized" -mindepth 1 ! -name .gitkeep -depth | while IFS= read -r path; do
      remove_path "$path"
    done
    if [ "$DRY_RUN" -eq 0 ]; then
      mkdir -p "$root/non-finalized"
      : >> "$root/non-finalized/.gitkeep"
    fi
  fi
  if [ -d "$root/finalized" ]; then
    remove_path "$root/finalized"
  fi
  # Files dropped on the thoughts root itself (not in a subdirectory).
  find "$root" -mindepth 1 -maxdepth 1 ! -name non-finalized ! -name finalized -depth | while IFS= read -r path; do
    remove_path "$path"
  done
}

say "purge-process-output: repo=$REPO"

purge_thoughts_root "$REPO/.cursor/thoughts"
purge_thoughts_root "$REPO/.ai/thoughts"
remove_path "$REPO/.cursor/reviews"

# Reserved scratch names: same class as thoughts. Do not walk .git.
if [ -d "$REPO" ]; then
  find "$REPO" \( -path "$REPO/.git" -o -path "$REPO/.git/*" \) -prune -o \
    \( -type d -name '_scratch' -o -name '*.scratch' -o -name '*.scratch.*' \) -print | while IFS= read -r path; do
    remove_path "$path"
  done
fi

if [ "$DRY_RUN" -eq 1 ]; then
  say "purge-process-output: dry-run complete (nothing deleted)"
  exit 0
fi

say "purge-process-output: process Markdown and scratch removed"
say "Orchestrator may now perform the one authorized push."
