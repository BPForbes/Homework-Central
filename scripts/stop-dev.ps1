# Stop the Homework Central local dev stack and free the Docker Postgres port.
#
# Usage:
#   scripts/stop-dev.ps1
#   scripts/stop-dev.ps1 -Help
[CmdletBinding()]
param(
    [switch]$Help
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

. (Join-Path $PSScriptRoot 'dev-stack-lib.ps1')

function Show-Usage {
    @'
Homework Central - stop local dev stack

Usage:
  scripts/stop-dev.ps1

Stops Docker Postgres started by scripts/run-dev.ps1 and frees its host port.
API and frontend terminals must still be closed manually on Windows.
Account data is preserved in the pgdata Docker volume.

To wipe the database volume (removes registered accounts):
  scripts/reset-dev-db.ps1
  # or: docker compose down -v
'@ | Write-Output
}

if ($Help) {
    Show-Usage
    exit 0
}

$state = Read-DevStackState
if ($null -eq $state -or $state['managed_postgres'] -ne '1') {
    Write-Host '==> No managed dev stack Postgres session found' -ForegroundColor DarkGray
    exit 0
}

$port = $state['postgres_port']
Write-Host "==> Stopping Docker Postgres on localhost:$port"
Stop-DevStack
Write-Host '==> Dev stack Postgres stopped (database volume preserved)'
