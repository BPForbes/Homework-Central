# Wipe the local Docker Postgres volume (removes all registered accounts and seed data).
#
# Usage:
#   scripts/reset-dev-db.ps1
#   scripts/reset-dev-db.ps1 -Yes
[CmdletBinding()]
param(
    [switch]$Yes
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$ComposeFile = Join-Path $RepoRoot 'docker-compose.yml'

. (Join-Path $PSScriptRoot 'dev-stack-lib.ps1')

if (-not $Yes) {
    Write-Host 'This removes the pgdata Docker volume and all local account data.'
    Write-Host 'Re-run with -Yes to continue: scripts/reset-dev-db.ps1 -Yes'
    exit 1
}

$envValues = Ensure-DevEnvFile -RollFCaptchaSecret

$env:POSTGRES_PASSWORD = $envValues['POSTGRES_PASSWORD']
$env:POSTGRES_HOST_PORT = $envValues['POSTGRES_HOST_PORT']
$env:FCAPTCHA_SECRET = $envValues['FCAPTCHA_SECRET']
$env:JWT_SECRET = $envValues['JWT_SECRET']
$env:FCAPTCHA_HOST_PORT = $envValues['FCAPTCHA_HOST_PORT']

$composeArgs = @('-f', $ComposeFile, '--env-file', $script:DevStackEnvFile)
docker compose @composeArgs down -v --remove-orphans
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host '==> Dev database volume removed. Run scripts/run-dev.ps1 to start fresh.'
