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
$PSNativeCommandUseErrorActionPreference = $false

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

# Include optional profiles so clamav/llm containers (if started) release the compose network.
$composeArgs = @('-f', $ComposeFile, '--env-file', $script:DevStackEnvFile, '--profile', 'antivirus', '--profile', 'ai')
docker compose @composeArgs down -v --remove-orphans
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Compose warns "Network … Resource is still in use" when something outside this
# down still holds homework-central_default. Volume wipe already succeeded; try a
# best-effort network remove, then continue — run-dev reuses the network either way.
$networkName = 'homework-central_default'
docker network inspect $networkName *> $null
if ($LASTEXITCODE -eq 0) {
    docker network rm $networkName *> $null
    if ($LASTEXITCODE -eq 0) {
        Write-Host "==> Removed leftover Docker network $networkName"
    }
    else {
        Write-Host "==> Docker network $networkName is still in use (harmless). run-dev will reuse it." -ForegroundColor DarkGray
    }
}

Write-Host '==> Dev database volume removed. Run scripts/run-dev.ps1 to start fresh.'
