# Launch the frontend for local development.
# Sets VITE_HC_DEV_BYPASS=true and opens http://localhost:5173/login when ready.
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
$ScriptRoot = $PSScriptRoot
$FrontendDir = Join-Path $RepoRoot 'frontend'

. (Join-Path $PSScriptRoot 'dev-stack-lib.ps1')

if ($PreRegistered) {
    $env:HC_DEV_STACK_PREREGISTERED = '1'
}

Write-Host 'Homework Central frontend - http://localhost:5173' -ForegroundColor Cyan

$env:VITE_HC_DEV_BYPASS = 'true' # Exposes the /devlogin route in the frontend router.
if (-not (Test-Path Env:NODE_OPTIONS) -or [string]::IsNullOrWhiteSpace($env:NODE_OPTIONS)) {
    # Vite normally stays well below this; the cap protects an 8 GiB workstation
    # from plugin/cache growth. An explicit caller value always wins.
    $env:NODE_OPTIONS = '--max-old-space-size=384'
}

Push-Location $FrontendDir
try {
    if ($env:HC_SKIP_BROWSER_OPEN -ne '1') {
        Start-DevStackPowerShellProcess -WindowStyle Hidden -ArgumentList @(
            '-File', (Join-Path $ScriptRoot 'wait-and-open-browser.ps1'),
            '-Url', 'http://localhost:5173/login',
            '-Label', 'Frontend',
            '-MaxAttempts', '300'
        ) -WorkingDirectory $RepoRoot
    }

    npm run dev
    $exitCode = $LASTEXITCODE

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
