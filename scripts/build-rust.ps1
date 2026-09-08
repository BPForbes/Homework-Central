# Compile the rust/ workspace (hc-feature-encode, hc-vector-cosine).
#
# Usage:
#   scripts/build-rust.ps1
#
# Environment:
#   HC_SKIP_RUST_BUILD=1  Skip cargo (also honored by run-dev / start-api-dev)
#   HC_SKIP_BUILD=1       Skip cargo and other compile-script builds
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'dev-stack-lib.ps1')

Build-RustWorkspace
