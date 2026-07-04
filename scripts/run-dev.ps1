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
# Dev bypass (HC_DEV_BYPASS / VITE_HC_DEV_BYPASS) is set by start-api-dev.ps1 and start-frontend-dev.ps1.
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
$PostgresHostCheckProject = Join-Path $RepoRoot 'scripts/PostgresHostCheck/PostgresHostCheck.csproj'
$PostgresHostCheckDll = Join-Path $RepoRoot 'scripts/PostgresHostCheck/bin/Debug/net10.0/PostgresHostCheck.dll'
$FrontendDir = Join-Path $RepoRoot 'frontend'
$EnvFile = Join-Path $RepoRoot '.env'
$ComposeFile = Join-Path $RepoRoot 'docker-compose.yml'
$DevPostgresUser = 'postgres'
$DevPostgresPassword = 'postgres'
$DevPostgresHostPort = '5434'
$DevPostgresHostPortMin = 5434
$DevPostgresHostPortMax = 5450
$script:ApiBuildFailed = $false

. (Join-Path $PSScriptRoot 'dev-stack-lib.ps1')

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

Stop:
  scripts/stop-dev.ps1
  Closing both API and frontend terminals stops Docker Postgres and frees its port.
  Restarting the API alone will auto-start Postgres if needed.

Requires: Docker (for Postgres), .NET 10 SDK, Node.js 18+, PowerShell 7+ (pwsh)
'@ | Write-Output
}

function Write-Step([string]$Message) {
    # Write-Host keeps status lines off the function output stream (Write-Output would
    # pollute return values, e.g. Ensure-EnvFile returning Object[] instead of hashtable).
    Write-Host "==> $Message"
}

function New-RandomSecret {
    return New-DevRandomSecret
}

function Read-EnvFile {
    return Read-DevEnvFile
}

function Update-EnvFileValues([hashtable]$Values) {
    Update-DevEnvFileValues $Values
}

function Test-IsWindowsHost {
    return $IsWindows -or $env:OS -match '(?i)Windows'
}

function Test-LoopbackPortListener([int]$Port) {
    if (-not (Test-IsWindowsHost)) {
        return $false
    }

    try {
        $listeners = @(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)
    } catch {
        $listeners = @()
    }

    if ($listeners.Count -eq 0) {
        $pattern = ":$Port\s"
        $listeners = netstat -ano | Select-String 'LISTENING' | Select-String $pattern
        foreach ($line in $listeners) {
            $text = $line.ToString()
            if ($text -match '127\.0\.0\.1:' -or $text -match '\[::1\]:') {
                return $true
            }
        }
        return $false
    }

    return $null -ne ($listeners | Where-Object { $_.LocalAddress -in @('127.0.0.1', '::1') } | Select-Object -First 1)
}

function Find-FreePostgresHostPort {
    for ($port = $DevPostgresHostPortMin; $port -le $DevPostgresHostPortMax; $port++) {
        if (Test-LoopbackPortListener $port) {
            continue
        }

        $listeners = @()
        try {
            $listeners = @(Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue)
        } catch {
            $listeners = @()
        }

        if ($listeners.Count -eq 0) {
            return "$port"
        }
    }

    return $null
}

function Resolve-PostgresHostPort([hashtable]$Values) {
    $port = [int]$Values['POSTGRES_HOST_PORT']

    if (-not (Test-LoopbackPortListener $port)) {
        return $Values
    }

    Write-Step "Port $port is bound on 127.0.0.1 by another PostgreSQL install (localhost would not reach Docker)"
    $freePort = Find-FreePostgresHostPort
    if ([string]::IsNullOrWhiteSpace($freePort)) {
        throw "No free Postgres host port found between $DevPostgresHostPortMin and $DevPostgresHostPortMax"
    }

    Write-Step "Using POSTGRES_HOST_PORT=$freePort instead"
    $Values['POSTGRES_HOST_PORT'] = $freePort
    Update-EnvFileValues @{ POSTGRES_HOST_PORT = $freePort }
    return (Read-EnvFile)
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
    if ($published -and $published -ne $port) {
        Write-Step "Docker Postgres is not published on localhost:$port (container maps to ${published})"
        return $false
    }

    if (-not (Test-Path $PostgresHostCheckDll)) {
        Build-PostgresHostCheck
    }

    # Same localhost path the API uses (docker run + host.docker.internal is unreliable on Windows).
    $stderr = ''
    for ($attempt = 1; $attempt -le 10; $attempt++) {
        $stderr = dotnet $PostgresHostCheckDll $port 2>&1 | Out-String
        if ($LASTEXITCODE -eq 0) {
            return $true
        }
        Start-Sleep -Seconds 1
    }

    Write-Step "Cannot connect to homework_central_master on localhost:$port from the host"
    $detail = $stderr.Trim()
    if ($detail) {
        Write-Host "       $detail" -ForegroundColor DarkGray
    }
    return $false
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
        sh -c "PGPASSWORD='$DevPostgresPassword' psql -h 127.0.0.1 -U $DevPostgresUser -d postgres -tAc `"SELECT 1 FROM pg_database WHERE datname = 'homework_central_master'`"" 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -eq 0 -and $output -eq '1') {
        Invoke-PostgresAdminSql 'ALTER DATABASE homework_central_master REFRESH COLLATION VERSION;'
    }
}

function Prepare-HomeworkCentralDatabase {
    try {
        Repair-PostgresCollation
    } catch {
        return $false
    }

    $output = (docker compose -f $ComposeFile --env-file $EnvFile exec -T postgres `
        sh -c "PGPASSWORD='$DevPostgresPassword' psql -h 127.0.0.1 -U $DevPostgresUser -d postgres -tAc `"SELECT 1 FROM pg_database WHERE datname = 'homework_central_master'`"" 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) {
        return $false
    }

    if ($output -ne '1') {
        Write-Step 'Creating homework_central_master database'
        try {
            Invoke-PostgresAdminSql 'CREATE DATABASE homework_central_master;'
            Invoke-PostgresAdminSql 'ALTER DATABASE homework_central_master REFRESH COLLATION VERSION;'
        } catch {
            return $false
        }
    }

    if (-not (Test-PostgresAuth -Database 'homework_central_master')) {
        return $false
    }

    return $true
}

function Start-PostgresContainer([hashtable]$EnvValues) {
    $expectedPort = $EnvValues['POSTGRES_HOST_PORT']
    $published = Get-PostgresPublishedPort

    if ($published -and $published -ne $expectedPort) {
        Write-Step "Recreating Postgres container for localhost:$expectedPort"
        docker compose -f $ComposeFile --env-file $EnvFile up -d --force-recreate postgres
    } else {
        docker compose -f $ComposeFile --env-file $EnvFile up -d postgres
    }

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

    Start-PostgresContainer -EnvValues $EnvValues

    Write-Step 'Waiting for Postgres to accept connections'
    Wait-ForPostgres

    if (-not (Test-PostgresAuth -Database 'postgres')) {
        Write-Step 'Postgres rejected postgres/postgres (stale Docker volume with a different password)'
        Reset-PostgresVolume
        Start-PostgresContainer -EnvValues $EnvValues
        Write-Step 'Waiting for Postgres to accept connections'
        Wait-ForPostgres
        if (-not (Test-PostgresAuth -Database 'postgres')) {
            throw 'Postgres password verification failed after recreating the Docker volume'
        }
    }

    if (-not (Prepare-HomeworkCentralDatabase)) {
        Write-Step 'Postgres volume is unhealthy (collation mismatch); recreating'
        Reset-PostgresVolume
        Start-PostgresContainer -EnvValues $EnvValues
        Write-Step 'Waiting for Postgres to accept connections'
        Wait-ForPostgres

        if (-not (Prepare-HomeworkCentralDatabase)) {
            throw 'Failed to prepare homework_central_master inside the Docker Postgres container'
        }
    }

    if (-not (Test-PostgresHostConnection $EnvValues)) {
        $port = $EnvValues['POSTGRES_HOST_PORT']
        $loopbackHint = ''
        if (Test-LoopbackPortListener ([int]$port)) {
            $loopbackHint = "`nPort $port is bound on 127.0.0.1 by another PostgreSQL install, so localhost does not reach Docker."
        }
        throw @"
Failed to reach homework_central_master on localhost:$port.$loopbackHint
Pick a free port in .env (for example POSTGRES_HOST_PORT=5434), then run:
  docker compose down -v
  pwsh .\scripts\run-dev.ps1
"@
    }
}

function Ensure-EnvFile {
    return Ensure-DevEnvFile
}

function Wait-ForPostgres {
    $attempts = 30
    for ($i = 1; $i -le $attempts; $i++) {
        docker compose -f $ComposeFile exec -T postgres pg_isready -U postgres -d postgres *> $null
        if ($LASTEXITCODE -eq 0) { return }
        Start-Sleep -Seconds 1
    }
    throw "Postgres did not become ready within ${attempts}s"
}

function Assert-DockerRunning {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw 'Docker CLI not found. Install Docker Desktop and ensure docker is on PATH.'
    }

    docker info *> $null
    if ($LASTEXITCODE -ne 0) {
        throw 'Docker is not running. Start Docker Desktop and retry.'
    }
}

function Start-Postgres([hashtable]$EnvValues) {
    Write-Step "Starting Postgres (Docker) on localhost:$($EnvValues['POSTGRES_HOST_PORT'])"

    Assert-DockerRunning

    Ensure-PostgresReady $EnvValues
}

function Start-FCaptcha {
    param([hashtable]$EnvValues)

    $port = $EnvValues['FCAPTCHA_HOST_PORT']
    if ([string]::IsNullOrWhiteSpace($port)) {
        $port = $script:DevFCaptchaHostPort
    }

    Write-Step "Starting FCaptcha (Docker) on localhost:$port"

    Assert-DockerRunning

    Ensure-DevFCaptchaRunning -Port $port
}

function Build-PostgresHostCheck {
    dotnet build $PostgresHostCheckProject -c Debug -v q *> $null
    if ($LASTEXITCODE -ne 0) { throw 'PostgresHostCheck build failed' }
}

function Test-PostgresHostCheckFresh {
    $dll = Join-Path $RepoRoot 'scripts/PostgresHostCheck/bin/Debug/net10.0/PostgresHostCheck.dll'
    if (-not (Test-Path $dll)) {
        return $false
    }

    $built = Get-Item $dll

    # Compare against the project file *and* every .cs source under it, not just the .csproj —
    # otherwise a source-only edit (no .csproj change) is wrongly treated as already fresh and
    # this script keeps running the stale compiled checker.
    $projectDir = Split-Path $PostgresHostCheckProject -Parent
    $sourceFiles = @(Get-Item $PostgresHostCheckProject) + @(Get-ChildItem -Path $projectDir -Filter '*.cs' -Recurse)
    $newestSource = $sourceFiles | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1

    return $built.LastWriteTimeUtc -ge $newestSource.LastWriteTimeUtc
}

function Build-PostgresHostCheckIfNeeded {
    if (Test-PostgresHostCheckFresh) {
        Write-Step 'Postgres host check already built'
        return
    }

    Build-PostgresHostCheck
}

function Build-Projects {
    $script:ApiBuildFailed = $false
    $skipDotnet = $env:HC_SKIP_DOTNET_BUILD -eq '1' -or $env:HC_SKIP_BUILD -eq '1'
    $apiBuildLog = Join-Path ([System.IO.Path]::GetTempPath()) 'hc-api-build-errors.log'
    $apiBuildJob = $null

    Ensure-FrontendDependencies -FrontendDir $FrontendDir

    if ($skipDotnet) {
        Write-Step 'Skipping API build (HC_SKIP_DOTNET_BUILD=1)'
    } else {
        Write-Step 'Building API (parallel with frontend typecheck)'
        $apiBuildJob = Start-Job -ScriptBlock {
            param($Project, $Log)
            dotnet build $Project -c Debug 2>&1 | Tee-Object -FilePath $Log
            if ($LASTEXITCODE -ne 0) {
                throw "API build failed with exit code $LASTEXITCODE"
            }
        } -ArgumentList $ApiProject, $apiBuildLog
    }

    $frontendTscJob = Start-FrontendTypecheckJob -FrontendDir $FrontendDir

    # Capture host-check and frontend typecheck failures separately so a host-check throw still
    # drains Wait-FrontendTypecheckJob before the API build job wait/cleanup below runs.
    $hostCheckFailed = $false
    $frontendTypecheckFailed = $false
    try {
        try {
            Build-PostgresHostCheckIfNeeded
        } catch {
            $hostCheckFailed = $true
        }

        try {
            Wait-FrontendTypecheckJob -Job $frontendTscJob
        } catch {
            $frontendTypecheckFailed = $true
        }
    } finally {
        if ($null -ne $apiBuildJob) {
            Wait-Job $apiBuildJob | Out-Null
            try {
                Receive-Job $apiBuildJob -ErrorAction Stop | Out-Null
            } catch {
                if ((Test-Path $apiBuildLog) -and (Get-Item $apiBuildLog).Length -gt 0) {
                    & (Join-Path $PSScriptRoot 'open-api-error-page.ps1') -Title 'API Build Errors' -ErrorLogFile $apiBuildLog
                }
                if ($BuildOnly) {
                    throw 'API build failed'
                }
                $script:ApiBuildFailed = $true
                Write-Step 'API build failed; frontend will start and show unable to connect to API'
            } finally {
                Remove-Job $apiBuildJob -Force
            }
        }
    }

    if ($frontendTypecheckFailed) {
        throw 'Frontend typecheck failed'
    }

    if ($hostCheckFailed) {
        throw 'PostgresHostCheck build failed'
    }
}

function Start-DevStack([hashtable]$EnvValues) {
    $apiStarter = Join-Path $RepoRoot 'scripts/start-api-dev.ps1'
    $frontendStarter = Join-Path $RepoRoot 'scripts/start-frontend-dev.ps1'

    if (-not $SkipDocker) {
        Initialize-DevStackState -PostgresPort $EnvValues['POSTGRES_HOST_PORT'] -ServerCount 2
    }

    $env:HC_SKIP_BROWSER_OPEN = '1'

    if (-not $script:ApiBuildFailed) {
        Write-Step 'Starting API in a new terminal (http://localhost:5000)'
        $apiArgs = @('-NoExit', '-File', $apiStarter)
        if ($SkipDocker) {
            $apiArgs += '-SkipDocker'
        } else {
            $apiArgs += '-PreRegistered'
        }
        Start-DevStackPowerShellProcess -ArgumentList $apiArgs -WorkingDirectory $RepoRoot
    } else {
        Write-Step 'Skipping API start because the build failed (see API Build Errors browser tab)'
    }

    Write-Step 'Starting frontend in a new terminal (http://localhost:5173)'
    $frontendArgs = @('-NoExit', '-File', $frontendStarter)
    if (-not $SkipDocker) {
        $frontendArgs += '-PreRegistered'
    }
    Start-DevStackPowerShellProcess -ArgumentList $frontendArgs -WorkingDirectory $RepoRoot

    Write-Step 'Opening browser tabs when servers are ready'
    if (-not $script:ApiBuildFailed) {
        Start-DevStackPowerShellProcess -WindowStyle Hidden -ArgumentList @(
            '-File', (Join-Path $PSScriptRoot 'wait-and-open-browser.ps1'),
            '-Url', 'http://localhost:5000/',
            '-Label', 'API',
            '-MaxAttempts', '300'
        ) -WorkingDirectory $RepoRoot
    }
    Start-DevStackPowerShellProcess -WindowStyle Hidden -ArgumentList @(
        '-File', (Join-Path $PSScriptRoot 'wait-and-open-browser.ps1'),
        '-Url', 'http://localhost:5173/login',
        '-Label', 'Frontend',
        '-MaxAttempts', '300'
    ) -WorkingDirectory $RepoRoot

    Remove-Item Env:HC_SKIP_BROWSER_OPEN -ErrorAction SilentlyContinue

    Write-Step 'Dev stack is running in separate terminals'
    Write-Host '  Frontend: http://localhost:5173/login'
    if (-not $script:ApiBuildFailed) {
        Write-Host '  API:      http://localhost:5000'
    } else {
        Write-Host '  API:      unavailable (check API Build Errors browser tab)'
    }
    if (-not $SkipDocker) {
        Write-Host '  Postgres: localhost:' -NoNewline
        Write-Host $EnvValues['POSTGRES_HOST_PORT'] -NoNewline
        Write-Host ' (Docker; stops when both terminals are closed)'
        $fcaptchaPort = $EnvValues['FCAPTCHA_HOST_PORT']
        if ([string]::IsNullOrWhiteSpace($fcaptchaPort)) {
            $fcaptchaPort = $script:DevFCaptchaHostPort
        }
        Write-Host "  FCaptcha: localhost:$fcaptchaPort (Docker; stops when both terminals are closed)"
    }
    Write-Host 'Close both terminal windows to stop servers and free the Postgres port'
    Write-Host 'Or run: scripts/stop-dev.ps1'
}

function Get-EnvValues {
    $values = Ensure-DevEnvFile
    return (Resolve-PostgresHostPort $values)
}

function Start-RunPhase([hashtable]$EnvValues) {
    Write-Step 'Preparing dev stack (Postgres, API, frontend)'

    if (-not $SkipDocker) {
        Start-Postgres -EnvValues $EnvValues
        Start-FCaptcha -EnvValues $EnvValues
    } else {
        Write-Step 'Skipping Docker Postgres and FCaptcha (HC_SKIP_DOCKER / -SkipDocker)'
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
