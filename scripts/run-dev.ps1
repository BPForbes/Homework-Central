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

After startup:
  Frontend  http://localhost:5173
  API       http://localhost:5000
  Health    http://localhost:5000/healthz

Requires: Docker (for Postgres), .NET 8 SDK, Node.js 18+
'@ | Write-Output
}

function Write-Step([string]$Message) {
    Write-Output "==> $Message"
}

function New-RandomSecret {
    $bytes = New-Object byte[] 48
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    return [Convert]::ToBase64String($bytes)
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
    docker info *> $null
    if ($LASTEXITCODE -ne 0) {
        throw 'Docker is not running. Start Docker Desktop and retry.'
    }

    Write-Step "Starting Postgres (Docker) on localhost:$($EnvValues['POSTGRES_HOST_PORT'])"
    docker compose -f $ComposeFile up -d postgres
    if ($LASTEXITCODE -ne 0) { throw 'docker compose up failed' }

    Write-Step 'Waiting for Postgres to accept connections'
    Wait-ForPostgres
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

function Wait-ForFirstProcessExit {
    param(
        [System.Diagnostics.Process]$Backend,
        [System.Diagnostics.Process]$Frontend
    )

    $exited = $null
    while ($true) {
        foreach ($proc in @($Backend, $Frontend)) {
            if ($proc.HasExited) {
                $exited = $proc
                break
            }
        }
        if ($null -ne $exited) { break }
        Start-Sleep -Milliseconds 200
    }

    foreach ($proc in @($Backend, $Frontend)) {
        if (-not $proc.HasExited) {
            Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
            $proc.WaitForExit()
        }
    }

    return $exited.ExitCode
}

function Start-DevStack([hashtable]$EnvValues) {
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = 'http://localhost:5000'
    $env:Jwt__Secret = $EnvValues['JWT_SECRET']
    $env:ConnectionStrings__DefaultConnection = "Host=localhost;Port=$($EnvValues['POSTGRES_HOST_PORT']);Database=homework_central;Username=postgres;Password=$($EnvValues['POSTGRES_PASSWORD'])"

    Write-Step 'Starting API on http://localhost:5000'
    $backend = Start-Process -FilePath 'dotnet' `
        -ArgumentList @('run', '--project', $ApiProject, '--no-build', '--urls', 'http://localhost:5000') `
        -PassThru -NoNewWindow

    Write-Step 'Starting frontend on http://localhost:5173'
    $frontend = Start-Process -FilePath 'npm' `
        -ArgumentList @('run', 'dev', '--prefix', $FrontendDir) `
        -PassThru -NoNewWindow

    Write-Step 'Dev stack is running'
    Write-Output '  Frontend: http://localhost:5173'
    Write-Output '  API:      http://localhost:5000'
    Write-Output 'Press Ctrl+C to stop'

    $exitCode = Wait-ForFirstProcessExit -Backend $backend -Frontend $frontend
    if ($exitCode -ne 0) {
        exit $exitCode
    }
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
    $envValues = Ensure-EnvFile
    Build-Projects

    if ($BuildOnly) {
        Write-Step 'Build complete (-BuildOnly)'
        exit 0
    }

    if (-not $SkipDocker) {
        Start-Postgres -EnvValues $envValues
    } else {
        Write-Step 'Skipping Docker Postgres (HC_SKIP_DOCKER / -SkipDocker)'
    }

    Start-DevStack -EnvValues $envValues
}
finally {
    Pop-Location
}
