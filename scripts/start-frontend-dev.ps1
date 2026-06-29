# Launch the frontend for local development.
# Sets VITE_HC_DEV_BYPASS=true and opens http://localhost:5173/devlogin when ready.
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

$env:VITE_HC_DEV_BYPASS = 'true' # Exposes the /devlogin route in the frontend router.

Push-Location $FrontendDir
try {
    $browserJob = Start-Job -ScriptBlock {
        & (Join-Path $using:ScriptRoot 'wait-and-open-browser.ps1') -Url 'http://localhost:5173/devlogin' -Label 'Frontend' -MaxAttempts 120
    }

    npm run dev
    $exitCode = $LASTEXITCODE

    Stop-Job $browserJob -ErrorAction SilentlyContinue
    Remove-Job $browserJob -Force -ErrorAction SilentlyContinue

    if ($exitCode -ne 0) {
        exit $exitCode
    }
}
finally {
    Pop-Location
    if ($env:HC_DEV_STACK_PREREGISTERED -eq '1') {
        Unregister-DevStackServer
    }
}
