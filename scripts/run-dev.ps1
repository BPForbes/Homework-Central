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

function Get-PasswordFingerprint([string]$Password) {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Password)
    $hash = [System.Security.Cryptography.SHA256]::Create().ComputeHash($bytes)
    return [Convert]::ToBase64String($hash)
}

function Format-PostgresConnectionString([hashtable]$EnvValues) {
    $pgPort = $EnvValues['POSTGRES_HOST_PORT']
    $quotedPassword = $EnvValues['POSTGRES_PASSWORD'].Replace("'", "''")
    return "Host=localhost;Port=$pgPort;Database=homework_central;Username=postgres;Password='$quotedPassword'"
}

function Set-ComposeEnv([hashtable]$EnvValues) {
    $env:POSTGRES_PASSWORD = $EnvValues['POSTGRES_PASSWORD']
    $env:POSTGRES_HOST_PORT = $EnvValues['POSTGRES_HOST_PORT']
}

function Test-PostgresAuth([hashtable]$EnvValues) {
    $encoded = [uri]::EscapeDataString($EnvValues['POSTGRES_PASSWORD'])
    $uri = "postgresql://postgres:${encoded}@127.0.0.1:5432/homework_central"
    docker compose -f $ComposeFile --env-file $EnvFile exec -T postgres `
        psql $uri -tAc 'SELECT 1' *> $null
    return $LASTEXITCODE -eq 0
}

function Reset-PostgresVolume {
    Write-Step 'Recreating Postgres Docker volume to match POSTGRES_PASSWORD in .env'
    docker compose -f $ComposeFile --env-file $EnvFile down -v --remove-orphans *> $null
    if ($LASTEXITCODE -ne 0) {
        throw 'Failed to remove Postgres Docker volume'
    }
}

function Save-VolumePasswordFingerprint([hashtable]$EnvValues) {
    $fingerprintFile = Join-Path $RepoRoot '.postgres-volume-password'
    $fingerprint = Get-PasswordFingerprint $EnvValues['POSTGRES_PASSWORD']
    Set-Content -Path $fingerprintFile -Value $fingerprint -NoNewline
}

function Ensure-PostgresReady([hashtable]$EnvValues) {
    Set-ComposeEnv $EnvValues

    $fingerprintFile = Join-Path $RepoRoot '.postgres-volume-password'
    $currentFingerprint = Get-PasswordFingerprint $EnvValues['POSTGRES_PASSWORD']
    if ((Test-Path $fingerprintFile) -and (Get-Content $fingerprintFile -Raw).Trim() -ne $currentFingerprint) {
        Write-Step 'POSTGRES_PASSWORD changed since the database volume was created'
        Reset-PostgresVolume
    }

    docker compose -f $ComposeFile --env-file $EnvFile up -d postgres
    if ($LASTEXITCODE -ne 0) { throw 'docker compose up failed' }

    Write-Step 'Waiting for Postgres to accept connections'
    Wait-ForPostgres

    if (Test-PostgresAuth $EnvValues) {
        Save-VolumePasswordFingerprint $EnvValues
        return
    }

    Write-Step 'Postgres is running but rejected the password from .env (stale Docker volume)'
    Reset-PostgresVolume

    docker compose -f $ComposeFile --env-file $EnvFile up -d postgres
    if ($LASTEXITCODE -ne 0) { throw 'docker compose up failed after volume reset' }

    Write-Step 'Waiting for Postgres to accept connections'
    Wait-ForPostgres

    if (-not (Test-PostgresAuth $EnvValues)) {
        throw 'Postgres password verification failed after recreating the Docker volume. Check POSTGRES_PASSWORD in .env'
    }

    Save-VolumePasswordFingerprint $EnvValues
}

function Read-EnvFile {
    $values = @{
        JWT_SECRET = ''
        POSTGRES_PASSWORD = ''
        POSTGRES_HOST_PORT = '5432'
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
        $values['POSTGRES_HOST_PORT'] = '5432'
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

    if ([string]::IsNullOrWhiteSpace($values['POSTGRES_PASSWORD']) -or $values['POSTGRES_PASSWORD'] -eq 'replace-with-a-strong-password') {
        $values['POSTGRES_PASSWORD'] = New-RandomSecret
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
    $apiProjectLiteral = $ApiProject.Replace("'", "''")
    $connectionString = Format-PostgresConnectionString $EnvValues

    $backendEnv = [System.Collections.Generic.Dictionary[string, string]]::new()
    $backendEnv['ASPNETCORE_ENVIRONMENT'] = 'Development'
    $backendEnv['ASPNETCORE_URLS'] = 'http://localhost:5000'
    $backendEnv['Jwt__Secret'] = $EnvValues['JWT_SECRET']
    $backendEnv['ConnectionStrings__DefaultConnection'] = $connectionString

    $backendCommand = "Write-Host 'Homework Central API - http://localhost:5000' -ForegroundColor Cyan; dotnet run --project '$apiProjectLiteral' --no-build --urls http://localhost:5000"

    Write-Step 'Starting API in a new terminal (http://localhost:5000)'
    Start-Process -FilePath 'pwsh' `
        -ArgumentList @('-NoExit', '-NoLogo', '-Command', $backendCommand) `
        -WorkingDirectory $RepoRoot `
        -Environment $backendEnv

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
