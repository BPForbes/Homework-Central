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
$EnvFile = Join-Path $RepoRoot '.env'
$ComposeFile = Join-Path $RepoRoot 'docker-compose.yml'

if (-not $Yes) {
    Write-Host 'This removes the pgdata Docker volume and all local account data.'
    Write-Host 'Re-run with -Yes to continue: scripts/reset-dev-db.ps1 -Yes'
    exit 1
}

if (Test-Path $EnvFile) {
    Get-Content $EnvFile | ForEach-Object {
        if ($_ -match '^\s*#' -or $_ -notmatch '=') { return }
        $name, $value = $_ -split '=', 2
        Set-Item -Path "Env:$($name.Trim())" -Value $value.Trim()
    }
}

if (-not $env:POSTGRES_PASSWORD) { $env:POSTGRES_PASSWORD = 'postgres' }
if (-not $env:POSTGRES_HOST_PORT) { $env:POSTGRES_HOST_PORT = '5434' }

$composeArgs = @('-f', $ComposeFile)
if (Test-Path $EnvFile) {
    $composeArgs += @('--env-file', $EnvFile)
}
docker compose @composeArgs down -v --remove-orphans
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host '==> Dev database volume removed. Run scripts/run-dev.ps1 to start fresh.'
