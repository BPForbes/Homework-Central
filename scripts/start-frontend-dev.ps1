# Launch the frontend for local development.
#
# Usage:
#   scripts/start-frontend-dev.ps1
#   scripts/start-frontend-dev.ps1 -PreRegistered
[CmdletBinding()]
param(
    [switch]$PreRegistered
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$FrontendDir = Join-Path $RepoRoot 'frontend'

. (Join-Path $PSScriptRoot 'dev-stack-lib.ps1')

if ($PreRegistered) {
    $env:HC_DEV_STACK_PREREGISTERED = '1'
}

Write-Host 'Homework Central frontend - http://localhost:5173' -ForegroundColor Cyan

Push-Location $FrontendDir
try {
    npm run dev
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
finally {
    Pop-Location
    if ($env:HC_DEV_STACK_PREREGISTERED -eq '1') {
        Unregister-DevStackServer
    }
}
