#!/usr/bin/env bash
# Compile the rust/ workspace (hc-feature-encode, hc-vector-cosine).
#
# Usage:
#   scripts/build-rust.sh
#
# Environment:
#   HC_SKIP_RUST_BUILD=1  Skip cargo (also honored by run-dev / start-api-dev)
#   HC_SKIP_BUILD=1       Skip cargo and other compile-script builds
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=scripts/dev-stack-lib.sh
source "$REPO_ROOT/scripts/dev-stack-lib.sh"

build_rust_workspace
