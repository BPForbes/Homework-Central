# Start the full Homework Central local dev stack (Postgres, API, frontend).
#
# Usage:
#   scripts/run-dev.ps1              # build + run everything
#   scripts/run-dev.ps1 -BuildOnly   # compile only (no servers)
#   scripts/run-dev.ps1 -Help
#
# Environment:
#   HC_SKIP_DOTNET_BUILD=1  Skip dotnet build only (set by IDE after a fresh compile)
#   HC_SKIP_DOCKER=1        Skip starting Postgres via Docker (use existing DB)
[CmdletBinding()]
param(
    [switch]$BuildOnly,
    [switch]$SkipDocker,
    [switch]$Help
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# dotnet/msbuild may write warnings to stderr; do not treat that as a terminating error.
$PSNativeCommandUseErrorActionPreference = $false

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$ApiProject = Join-Path $RepoRoot 'backend/HomeworkCentral.Api/HomeworkCentral.Api.csproj'
$FrontendDir = Join-Path $RepoRoot 'frontend'
$EnvFile = Join-Path $RepoRoot '.env'
$ComposeFile = Join-Path $RepoRoot 'docker-compose.yml'
$DevPostgresUser = 'postgres'
$DevPostgresPassword = 'postgres'
$DevPostgresHostPort = '5433'

function Show-Usage {
    @'
Homework Central - local dev stack

Usage:
  scripts/run-dev.ps1 [options]

Options:
  -BuildOnly    Compile the API and install frontend deps; do not start servers
  -SkipDocker   Do not start Postgres via Docker (expects DB on localhost)
  -Help         Show this help

After startup (each server opens in its own terminal window):
  Frontend  http://localhost:5173
  API       http://localhost:5000
  Health    http://localhost:5000/healthz

Requires: Docker (for Postgres), .NET 8 SDK, Node.js 18+, PowerShell 7+ (pwsh)
'@ | Write-Output
}

function Write-Step([string]$Message) {
    # Write-Host keeps status lines off the function output stream (Write-Output would
    # pollute return values, e.g. Ensure-EnvFile returning Object[] instead of hashtable).
    Write-Host "==> $Message"
}

function New-RandomSecret {
    $bytes = New-Object byte[] 48
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    # URL-safe base64 avoids special characters breaking connection strings and shells.
    return [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function Set-ComposeEnv([hashtable]$EnvValues) {
    $env:POSTGRES_PASSWORD = $DevPostgresPassword
    $env:POSTGRES_HOST_PORT = $EnvValues['POSTGRES_HOST_PORT']
}

function Invoke-PostgresAdminSql {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Sql
    )

    # Suppress psql NOTICE lines — uncaptured stdout would pollute caller return values in PowerShell.
    docker compose -f $ComposeFile --env-file $EnvFile exec -T postgres `
        sh -c "PGPASSWORD='$DevPostgresPassword' psql -h 127.0.0.1 -U $DevPostgresUser -d postgres -v ON_ERROR_STOP=1 -c `"$Sql`"" *> $null
    if ($LASTEXITCODE -ne 0) {
        throw "Postgres command failed: $Sql"
    }
}

function Get-PostgresPublishedPort {
    $raw = (docker compose -f $ComposeFile --env-file $EnvFile port postgres 5432 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($raw)) {
        return $null
    }

    if ($raw -match ':(\d+)$') {
        return $Matches[1]
    }

    return $null
}

function Test-PostgresHostConnection([hashtable]$EnvValues) {
    $port = $EnvValues['POSTGRES_HOST_PORT']
    $published = Get-PostgresPublishedPort
    if ($published -ne $port) {
        Write-Step "Docker Postgres is not published on localhost:$port (container maps to ${published})"
        return $false
    }

    $gatewayArgs = @()
    if ($IsLinux) {
        $gatewayArgs = @('--add-host=host.docker.internal:host-gateway')
    }

    # Ephemeral client container mirrors how the host reaches the published port.
    docker run --rm @gatewayArgs postgres:16-alpine `
        sh -c "PGPASSWORD='$DevPostgresPassword' psql -h host.docker.internal -p $port -U $DevPostgresUser -d homework_central -tAc 'SELECT 1'" *> $null
    if ($LASTEXITCODE -ne 0) {
        Write-Step "Docker Postgres is not reachable at localhost:$port from the host (another PostgreSQL may own that port)"
        return $false
    }

    return $true
}

function Test-PostgresAuth {
    param(
        [string]$Database = 'postgres'
    )

    docker compose -f $ComposeFile --env-file $EnvFile exec -T postgres `
        sh -c "PGPASSWORD='$DevPostgresPassword' psql -h 127.0.0.1 -p 5432 -U $DevPostgresUser -d $Database -tAc `"SELECT 1`"" *> $null
    return $LASTEXITCODE -eq 0
}

function Repair-PostgresCollation {
    Write-Step 'Refreshing Postgres collation versions (fixes stale Docker volumes)'
    Invoke-PostgresAdminSql 'ALTER DATABASE template1 REFRESH COLLATION VERSION;'
    Invoke-PostgresAdminSql 'ALTER DATABASE postgres REFRESH COLLATION VERSION;'

    $output = (docker compose -f $ComposeFile --env-file $EnvFile exec -T postgres `
        sh -c "PGPASSWORD='$DevPostgresPassword' psql -h 127.0.0.1 -U $DevPostgresUser -d postgres -tAc `"SELECT 1 FROM pg_database WHERE datname = 'homework_central'`"" 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -eq 0 -and $output -eq '1') {
        Invoke-PostgresAdminSql 'ALTER DATABASE homework_central REFRESH COLLATION VERSION;'
    }
}

function Prepare-HomeworkCentralDatabase {
    try {
        Repair-PostgresCollation
    } catch {
        return $false
    }

    $output = (docker compose -f $ComposeFile --env-file $EnvFile exec -T postgres `
        sh -c "PGPASSWORD='$DevPostgresPassword' psql -h 127.0.0.1 -U $DevPostgresUser -d postgres -tAc `"SELECT 1 FROM pg_database WHERE datname = 'homework_central'`"" 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) {
        return $false
    }

    if ($output -ne '1') {
        Write-Step 'Creating homework_central database'
        try {
            Invoke-PostgresAdminSql 'CREATE DATABASE homework_central;'
            Invoke-PostgresAdminSql 'ALTER DATABASE homework_central REFRESH COLLATION VERSION;'
        } catch {
            return $false
        }
    }

    if (-not (Test-PostgresAuth -Database 'homework_central')) {
        return $false
    }

    return $true
}

function Start-PostgresContainer {
    docker compose -f $ComposeFile --env-file $EnvFile up -d postgres
    if ($LASTEXITCODE -ne 0) { throw 'docker compose up failed' }
}

function Reset-PostgresVolume {
    Write-Step 'Recreating Postgres Docker volume (reset to postgres/postgres credentials)'
    docker compose -f $ComposeFile --env-file $EnvFile down -v --remove-orphans *> $null
    if ($LASTEXITCODE -ne 0) {
        throw 'Failed to remove Postgres Docker volume'
    }
}

function Ensure-PostgresReady([hashtable]$EnvValues) {
    Set-ComposeEnv $EnvValues

    Start-PostgresContainer

    Write-Step 'Waiting for Postgres to accept connections'
    Wait-ForPostgres

    if (-not (Test-PostgresAuth -Database 'postgres')) {
        Write-Step 'Postgres rejected postgres/postgres (stale Docker volume with a different password)'
        Reset-PostgresVolume
        Start-PostgresContainer
        Write-Step 'Waiting for Postgres to accept connections'
        Wait-ForPostgres
        if (-not (Test-PostgresAuth -Database 'postgres')) {
            throw 'Postgres password verification failed after recreating the Docker volume'
        }
    }

    if (-not (Prepare-HomeworkCentralDatabase)) {
        Write-Step 'Postgres volume is unhealthy (collation mismatch); recreating'
        Reset-PostgresVolume
        Start-PostgresContainer
        Write-Step 'Waiting for Postgres to accept connections'
        Wait-ForPostgres

        if (-not (Prepare-HomeworkCentralDatabase)) {
            throw 'Failed to prepare homework_central inside the Docker Postgres container'
        }
    }

    if (-not (Test-PostgresHostConnection $EnvValues)) {
        throw "Failed to reach Docker Postgres at localhost:$($EnvValues['POSTGRES_HOST_PORT']). Another PostgreSQL install may own that port. Try: docker compose down -v, set POSTGRES_HOST_PORT to a free port in .env, then rerun scripts/run-dev.ps1"
    }
}

function Read-EnvFile {
    $values = @{
        JWT_SECRET = ''
        POSTGRES_PASSWORD = ''
        POSTGRES_HOST_PORT = $DevPostgresHostPort
    }

    foreach ($line in Get-Content $EnvFile) {
        if ($line -match '^\s*#' -or $line -notmatch '=') { continue }
        $name, $value = $line -split '=', 2
        $name = $name.Trim()
        if ($values.ContainsKey($name)) {
            $values[$name] = $value.Trim()
        }
    }

    if ([string]::IsNullOrWhiteSpace($values['POSTGRES_HOST_PORT'])) {
        $values['POSTGRES_HOST_PORT'] = $DevPostgresHostPort
    }

    return $values
}

function Ensure-EnvFile {
    if (-not (Test-Path $EnvFile)) {
        Write-Step "Creating .env from .env.example"
        Copy-Item (Join-Path $RepoRoot '.env.example') $EnvFile
    }

    $lines = Get-Content $EnvFile
    $values = Read-EnvFile
    $updated = $false

    if ([string]::IsNullOrWhiteSpace($values['JWT_SECRET']) -or $values['JWT_SECRET'] -eq 'replace-with-a-long-random-secret') {
        $values['JWT_SECRET'] = New-RandomSecret
        $updated = $true
    }

    if ([string]::IsNullOrWhiteSpace($values['POSTGRES_PASSWORD']) -or $values['POSTGRES_PASSWORD'] -ne $DevPostgresPassword) {
        $values['POSTGRES_PASSWORD'] = $DevPostgresPassword
        $updated = $true
    }

    if ($values['POSTGRES_HOST_PORT'] -eq '5432') {
        Write-Step 'Using POSTGRES_HOST_PORT=5433 (port 5432 is often used by a local PostgreSQL install)'
        $values['POSTGRES_HOST_PORT'] = $DevPostgresHostPort
        $updated = $true
    }

    if ($updated) {
        $newLines = @()
        $seen = @{}
        foreach ($line in $lines) {
            if ($line -match '^\s*#' -or $line -notmatch '=') {
                $newLines += $line
                continue
            }
            $name = ($line -split '=', 2)[0].Trim()
            if ($values.ContainsKey($name)) {
                $newLines += "$name=$($values[$name])"
                $seen[$name] = $true
            } else {
                $newLines += $line
            }
        }
        foreach ($key in @('JWT_SECRET', 'POSTGRES_PASSWORD', 'POSTGRES_HOST_PORT')) {
            if (-not $seen.ContainsKey($key) -and $values.ContainsKey($key) -and -not [string]::IsNullOrWhiteSpace($values[$key])) {
                $newLines += "$key=$($values[$key])"
            }
        }
        Set-Content -Path $EnvFile -Value $newLines
        Write-Step 'Generated secrets in .env (local only, not committed)'
        $values = Read-EnvFile
    }

    if ([string]::IsNullOrWhiteSpace($values['JWT_SECRET'])) {
        throw 'JWT_SECRET is not set in .env'
    }
    if ($values['JWT_SECRET'].Length -lt 32) {
        throw 'JWT_SECRET must be at least 32 characters'
    }
    if ([string]::IsNullOrWhiteSpace($values['POSTGRES_PASSWORD'])) {
        throw 'POSTGRES_PASSWORD is not set in .env'
    }

    return $values
}

function Wait-ForPostgres {
    $attempts = 30
    for ($i = 1; $i -le $attempts; $i++) {
        docker compose -f $ComposeFile exec -T postgres pg_isready -U postgres -d homework_central *> $null
        if ($LASTEXITCODE -eq 0) { return }
        Start-Sleep -Seconds 1
    }
    throw "Postgres did not become ready within ${attempts}s"
}

function Start-Postgres([hashtable]$EnvValues) {
    Write-Step "Starting Postgres (Docker) on localhost:$($EnvValues['POSTGRES_HOST_PORT'])"

    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw 'Docker CLI not found. Install Docker Desktop and ensure docker is on PATH.'
    }

    docker info *> $null
    if ($LASTEXITCODE -ne 0) {
        throw 'Docker is not running. Start Docker Desktop and retry.'
    }

    Ensure-PostgresReady $EnvValues
}

function Build-Projects {
    $skipDotnet = $env:HC_SKIP_DOTNET_BUILD -eq '1' -or $env:HC_SKIP_BUILD -eq '1'
    if ($skipDotnet) {
        Write-Step 'Skipping API build (HC_SKIP_DOTNET_BUILD=1)'
    } else {
        Write-Step 'Building API'
        dotnet build $ApiProject -c Debug
        if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed' }
    }

    if (-not (Test-Path (Join-Path $FrontendDir 'node_modules'))) {
        Write-Step 'Installing frontend dependencies'
        npm ci --prefix $FrontendDir
        if ($LASTEXITCODE -ne 0) { throw 'npm ci failed' }
    } else {
        Write-Step 'Frontend dependencies already installed'
    }
}

function Start-DevStack([hashtable]$EnvValues) {
    $apiStarter = Join-Path $RepoRoot 'scripts/start-api-dev.ps1'

    Write-Step 'Starting API in a new terminal (http://localhost:5000)'
    Start-Process -FilePath 'pwsh' `
        -ArgumentList @('-NoExit', '-NoLogo', '-File', $apiStarter) `
        -WorkingDirectory $RepoRoot

    $frontendCommand = "Write-Host 'Homework Central frontend - http://localhost:5173' -ForegroundColor Cyan; npm run dev"

    Write-Step 'Starting frontend in a new terminal (http://localhost:5173)'
    Start-Process -FilePath 'pwsh' `
        -ArgumentList @('-NoExit', '-NoLogo', '-Command', $frontendCommand) `
        -WorkingDirectory $FrontendDir

    Write-Step 'Dev stack is running in separate terminals'
    Write-Host '  Frontend: http://localhost:5173'
    Write-Host '  API:      http://localhost:5000'
    Write-Host 'Close each terminal window to stop its server'
}

function Get-EnvValues {
    $raw = @(Ensure-EnvFile)
    $values = $raw | Where-Object { $_ -is [hashtable] } | Select-Object -First 1
    if ($null -eq $values) {
        throw 'Ensure-EnvFile did not return environment values (internal script error)'
    }
    return $values
}

function Start-RunPhase([hashtable]$EnvValues) {
    Write-Step 'Preparing dev stack (Postgres, API, frontend)'

    if (-not $SkipDocker) {
        Start-Postgres -EnvValues $EnvValues
    } else {
        Write-Step 'Skipping Docker Postgres (HC_SKIP_DOCKER / -SkipDocker)'
    }

    Start-DevStack -EnvValues $EnvValues
}

if ($Help) {
    Show-Usage
    exit 0
}

if ($env:HC_SKIP_DOCKER -eq '1') {
    $SkipDocker = $true
}

Push-Location $RepoRoot
try {
    $envValues = Get-EnvValues
    Build-Projects

    # Only stop for an explicit -BuildOnly on the command line (ignore profile defaults).
    if ($PSBoundParameters.ContainsKey('BuildOnly') -and $BuildOnly.IsPresent) {
        Write-Step 'Build complete (-BuildOnly)'
        exit 0
    }

    Start-RunPhase -EnvValues $envValues
}
catch {
    Write-Host "error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
finally {
    Pop-Location
}
