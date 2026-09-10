# Start the full Homework Central local dev stack (Postgres, API, frontend).
#
# Usage:
#   scripts/run-dev.ps1              # build + run; Postgres first, FCaptcha not joined before API
#   scripts/run-dev.ps1 -Stripped    # also pause neural environments; FCaptcha not joined before API
#   scripts/run-dev.ps1 -BuildOnly   # compile only (no servers)
#   scripts/run-dev.ps1 -Help
#
# Environment:
#   HC_SKIP_DOTNET_BUILD=1  Skip dotnet build only (set by IDE after a fresh compile)
#   HC_SKIP_RUST_BUILD=1    Skip cargo build --workspace in rust/
#   HC_SKIP_DOCKER=1        Skip starting Postgres via Docker (use existing DB)
#   HC_SKIP_DEV_WARMUP=1   Skip development migrations/seeds for a known-warm local database
#   HC_DEV_STRIPPED=1       Pause leftover neural training and skip neural warmup/refresh
# Dev bypass (HC_DEV_BYPASS / VITE_HC_DEV_BYPASS) is set by start-api-dev.ps1 and start-frontend-dev.ps1.
[CmdletBinding()]
param(
    [switch]$BuildOnly,
    [switch]$SkipDocker,
    [switch]$Stripped,
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
$DevPostgresHostPort = '5434'
$DevPostgresHostPortMin = 5434
$DevPostgresHostPortMax = 5450
$DevPostgresConnectHost = '127.0.0.1'
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
  -Stripped     Pause leftover neural training and skip neural warmup/refresh
                (also set HC_DEV_STRIPPED=1). Does not change Docker start order.
  -Help         Show this help

Default start brings Postgres up with the existing helper and backgrounds
FCaptcha so a cold captcha image build is not joined before the API. The API
does not start until Postgres accepts connections on
127.0.0.1:<POSTGRES_HOST_PORT>.

For rapid restarts after a successful start, set HC_SKIP_DEV_WARMUP=1 to skip
development migrations and seeds. Unset it after pulling migrations/catalog changes
or after resetting the local database.

After startup (each server opens in its own terminal window):
  Frontend  http://localhost:5173   (Vite HMR)
  API       http://localhost:5000   (dotnet watch by default; set HC_API_WATCH=0 to disable)
  Health    http://localhost:5000/healthz

Stop:
  scripts/stop-dev.ps1
  Closing both API and frontend terminals stops Docker Postgres and frees its port.
  Restarting the API alone will auto-start Postgres if needed.

Requires: Docker (for Postgres), .NET 10 SDK, Node.js 18+, Rust stable (rustup; cargo build --workspace), PowerShell 7+ (pwsh)
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

function Test-HostPortListener([int]$Port) {
    if (Test-IsWindowsHost) {
        try {
            $listeners = @(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)
            if ($listeners.Count -gt 0) {
                return $true
            }
        } catch {
            $listeners = @()
        }

        $pattern = ":$Port\s"
        $lines = @(netstat -ano | Select-String 'LISTENING' | Select-String $pattern)
        return $lines.Count -gt 0
    }

    if (Get-Command ss -ErrorAction SilentlyContinue) {
        $ss = ss -ltn 2>$null | Out-String
        return $ss -match ":$Port(\s|$)"
    }

    if (Get-Command lsof -ErrorAction SilentlyContinue) {
        lsof -nP -iTCP:"$Port" -sTCP:LISTEN *> $null
        return $LASTEXITCODE -eq 0
    }

    try {
        $client = [System.Net.Sockets.TcpClient]::new()
        $async = $client.BeginConnect($DevPostgresConnectHost, $Port, $null, $null)
        $connected = $async.AsyncWaitHandle.WaitOne(200) -and $client.Connected
        $client.Close()
        return $connected
    } catch {
        return $false
    }
}

function Find-FreePostgresHostPort {
    param([int]$ExcludePort = 0)

    for ($port = $DevPostgresHostPortMin; $port -le $DevPostgresHostPortMax; $port++) {
        if ($port -eq $ExcludePort) {
            continue
        }

        if (Test-HostPortListener $port) {
            continue
        }

        return "$port"
    }

    return $null
}

function Get-SuggestedPostgresHostPort([int]$FailedPort) {
    $freePort = Find-FreePostgresHostPort -ExcludePort $FailedPort
    if (-not [string]::IsNullOrWhiteSpace($freePort)) {
        return $freePort
    }

    $fallback = [Math]::Min($FailedPort + 1, $DevPostgresHostPortMax)
    if ($fallback -eq $FailedPort) {
        $fallback = $DevPostgresHostPortMin
    }

    return "$fallback"
}

function Test-OurPostgresPublishedOn([string]$Port) {
    $published = Get-PostgresPublishedPort
    return $published -eq $Port
}

function Set-PostgresHostPortValue([hashtable]$Values, [string]$Port) {
    $Values['POSTGRES_HOST_PORT'] = $Port
    Update-EnvFileValues @{ POSTGRES_HOST_PORT = $Port }
    $fresh = Read-EnvFile
    foreach ($key in @($fresh.Keys)) {
        $Values[$key] = $fresh[$key]
    }
}

function Resolve-PostgresHostPort([hashtable]$Values) {
    $port = [int]$Values['POSTGRES_HOST_PORT']

    if (Test-OurPostgresPublishedOn "$port") {
        return $Values
    }

    if (-not (Test-HostPortListener $port)) {
        return $Values
    }

    Write-Step "Port $port is already in use on this machine (127.0.0.1 would not reach Docker)"
    $freePort = Find-FreePostgresHostPort -ExcludePort $port
    if ([string]::IsNullOrWhiteSpace($freePort)) {
        throw "No free Postgres host port found between $DevPostgresHostPortMin and $DevPostgresHostPortMax"
    }

    Write-Step "Using POSTGRES_HOST_PORT=$freePort instead"
    Set-PostgresHostPortValue $Values $freePort
    return $Values
}

function Set-ComposeEnv([hashtable]$EnvValues) {
    $env:POSTGRES_PASSWORD = $DevPostgresPassword
    $env:POSTGRES_HOST_PORT = $EnvValues['POSTGRES_HOST_PORT']
    $env:FCAPTCHA_HOST_PORT = $EnvValues['FCAPTCHA_HOST_PORT']
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
        Write-Step "Docker Postgres is not published on ${DevPostgresConnectHost}:$port (container maps to ${published})"
        return $false
    }

    # Same 127.0.0.1 path the API uses. Host=localhost prefers ::1 on Windows and
    # times out against Docker Desktop's IPv4-only publish.
    for ($attempt = 1; $attempt -le 10; $attempt++) {
        if (Test-DevPostgresConnection $port) {
            return $true
        }
        Start-Sleep -Seconds 1
    }

    Write-Step "Cannot connect to homework_central_master on ${DevPostgresConnectHost}:$port from the host"
    $detail = Get-DevPostgresHostCheckDetail
    if ($detail) {
        Write-Host "       $detail" -ForegroundColor DarkGray
    }
    return $false
}

# Proves that a database exists and answers a query inside the container. It does not prove
# the volume's password: initdb writes `host all all 127.0.0.1/32 trust` ahead of the image's
# scram-sha-256 rule, so this loopback session authenticates against no password and succeeds
# on a volume whose password is not the dev one. Test-DevPostgresCredentialsRejected, which
# looks in from the host, is the check for that.
function Test-PostgresDatabaseInContainer {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Database
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

    if (-not (Test-PostgresDatabaseInContainer -Database 'homework_central_master')) {
        return $false
    }

    return $true
}

function Start-CoreContainers {
    param(
        [Parameter(Mandatory = $true)]
        [hashtable]$EnvValues,
        [switch]$ForceRecreate
    )

    $expectedPort = $EnvValues['POSTGRES_HOST_PORT']
    $fcaptchaPort = $EnvValues['FCAPTCHA_HOST_PORT']
    if ([string]::IsNullOrWhiteSpace($fcaptchaPort)) {
        $fcaptchaPort = $script:DevFCaptchaHostPort
    }

    $published = Get-PostgresPublishedPort
    $recreate = $ForceRecreate -or ($published -and $published -ne $expectedPort)
    if (-not $recreate -and (Test-DevFCaptchaConnection $fcaptchaPort) -and -not (Test-DevFCaptchaSecretAligned)) {
        $recreate = $true
        Write-Step 'Recreating Docker FCaptcha (FCAPTCHA_SECRET changed in .env)'
    }

    Write-Step "Starting Postgres (${DevPostgresConnectHost}:$expectedPort); FCaptcha continues in the background"
    Start-DevStackPostgresThenFCaptchaBackground -PostgresPort $expectedPort -FCaptchaPort $fcaptchaPort -ForceRecreate:$recreate
}

# Removes the Postgres container and the volume behind its data directory, leaving the caller to
# start it again. `docker compose down -v` would also take llmdata, uploads, and miniodata, which
# costs a developer their Ollama models and attachment blobs for a fault that lives in the Postgres
# data directory alone; scripts\reset-dev-db.ps1 stays the way to ask for the wider wipe.
#
# The volume name is read off the container rather than composed from the project name, because
# Compose derives that name from the checkout directory.
function Reset-PostgresVolume {
    Write-Step 'Recreating Postgres Docker volume (reset to postgres/postgres credentials)'

    [string]$container = (docker compose -f $ComposeFile --env-file $EnvFile ps -q postgres 2>$null | Out-String).Trim()
    if ([string]::IsNullOrWhiteSpace($container)) {
        throw 'Cannot identify the Postgres container to reset. Run: scripts\reset-dev-db.ps1 -Yes'
    }

    [string]$volume = (docker inspect -f '{{range .Mounts}}{{if eq .Destination "/var/lib/postgresql/data"}}{{.Name}}{{end}}{{end}}' $container 2>$null | Out-String).Trim()
    if ([string]::IsNullOrWhiteSpace($volume)) {
        throw 'Cannot identify the Postgres data volume to reset. Run: scripts\reset-dev-db.ps1 -Yes'
    }

    docker compose -f $ComposeFile --env-file $EnvFile rm --stop --force postgres *> $null
    docker volume rm $volume *> $null
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to remove Postgres Docker volume $volume"
    }
}

function Wait-CoreBeforeApi([hashtable]$EnvValues) {
    Write-Step 'Waiting for Postgres (FCaptcha continues in the background)'
    return Wait-ForPostgres -EnvValues $EnvValues
}

# Recreates the Postgres volume when the server rejects the dev credentials. The password is
# initialised into the volume, so neither recreating the container nor moving POSTGRES_HOST_PORT
# can change it — both only republish the same 28P01 on a new port.
#
# Confined to a port our own container publishes. A foreign Postgres answering there has a
# volume that is not ours to destroy, and it is Repair-PostgresHostReachability's port
# relocation that gets run-dev off it.
function Reset-StalePostgresVolume([hashtable]$EnvValues) {
    $port = $EnvValues['POSTGRES_HOST_PORT']
    if (-not (Test-DevPostgresCredentialsRejected $port)) {
        return
    }

    if (-not (Test-OurPostgresPublishedOn $port)) {
        Write-Step "Postgres on ${DevPostgresConnectHost}:$port rejects the dev credentials but is not our container; leaving its volume alone"
        return
    }

    Write-Step "Postgres rejected postgres/postgres on ${DevPostgresConnectHost}:$port (stale Docker volume with a different password)"
    Reset-PostgresVolume
    Start-CoreContainers -EnvValues $EnvValues
    $null = Wait-CoreBeforeApi -EnvValues $EnvValues
    if (Test-DevPostgresCredentialsRejected $port) {
        throw 'Postgres still rejects postgres/postgres after recreating the Docker volume'
    }
}

function Ensure-PostgresReady([hashtable]$EnvValues) {
    Set-ComposeEnv $EnvValues

    Start-CoreContainers -EnvValues $EnvValues
    if ((Wait-CoreBeforeApi -EnvValues $EnvValues) -ne 'Ready') {
        Repair-PostgresHostReachability $EnvValues
    }

    Reset-StalePostgresVolume $EnvValues

    if (-not (Prepare-HomeworkCentralDatabase)) {
        Write-Step 'Postgres volume is unhealthy (collation mismatch); recreating'
        Reset-PostgresVolume
        Start-CoreContainers -EnvValues $EnvValues
        $null = Wait-CoreBeforeApi -EnvValues $EnvValues

        if (-not (Prepare-HomeworkCentralDatabase)) {
            throw 'Failed to prepare homework_central_master inside the Docker Postgres container'
        }
    }

    if (-not (Test-PostgresHostConnection $EnvValues)) {
        Repair-PostgresHostReachability $EnvValues
    }
}

function Get-PostgresHostFailureMessage([string]$Port) {
    $example = Get-SuggestedPostgresHostPort ([int]$Port)
    $hint = ''
    if (Test-OurPostgresPublishedOn $Port) {
        $hint = "`nDocker published ${DevPostgresConnectHost}:$Port but the host still cannot open homework_central_master."
    } elseif (Test-HostPortListener ([int]$Port)) {
        $hint = "`nPort $Port is already in use on this machine, so ${DevPostgresConnectHost} does not reach the Docker container."
    }

    return @"
Failed to reach homework_central_master on ${DevPostgresConnectHost}:$Port.$hint
Pick a free port in .env (for example POSTGRES_HOST_PORT=$example), then run:
  docker compose down -v
  pwsh .\scripts\run-dev.ps1
"@
}

function Repair-PostgresHostReachability([hashtable]$EnvValues) {
    $port = $EnvValues['POSTGRES_HOST_PORT']

    # Our own volume initialised with a different password answers every port the same way, so
    # recreating the container and relocating POSTGRES_HOST_PORT cannot repair it. Leave that fault
    # to Reset-StalePostgresVolume, which the caller runs next. A foreign Postgres rejecting the
    # same credentials still belongs here, because relocating off its port is the only repair.
    if ((Test-DevPostgresCredentialsRejected $port) -and (Test-OurPostgresPublishedOn $port)) {
        return
    }

    if (Test-OurPostgresPublishedOn $port) {
        Write-Step "Host cannot reach Docker Postgres on ${DevPostgresConnectHost}:$port; recreating the container"
        Start-CoreContainers -EnvValues $EnvValues -ForceRecreate
        $null = Wait-CoreBeforeApi -EnvValues $EnvValues
        if (-not (Prepare-HomeworkCentralDatabase)) {
            throw 'Failed to prepare homework_central_master after recreating Docker Postgres'
        }

        if (Test-PostgresHostConnection $EnvValues) {
            return
        }
    }

    $freePort = Find-FreePostgresHostPort -ExcludePort ([int]$port)
    if ([string]::IsNullOrWhiteSpace($freePort)) {
        throw (Get-PostgresHostFailureMessage $port)
    }

    Write-Step "Host cannot reach homework_central_master on ${DevPostgresConnectHost}:$port. Using POSTGRES_HOST_PORT=$freePort instead"
    Set-PostgresHostPortValue $EnvValues $freePort
    Set-ComposeEnv $EnvValues
    Start-CoreContainers -EnvValues $EnvValues -ForceRecreate
    $null = Wait-CoreBeforeApi -EnvValues $EnvValues
    if (-not (Prepare-HomeworkCentralDatabase)) {
        throw 'Failed to prepare homework_central_master after changing POSTGRES_HOST_PORT'
    }

    if (-not (Test-PostgresHostConnection $EnvValues)) {
        throw (Get-PostgresHostFailureMessage $freePort)
    }
}

function Ensure-EnvFile {
    return Ensure-DevEnvFile
}

# Returns 'Ready' or 'HostUnreachable'; throws when Postgres never came up inside the
# container, which no caller can repair.
#
# Readiness here is host reachability, not a usable homework_central_master: this wait runs
# before Prepare-HomeworkCentralDatabase creates that database and before
# Reset-StalePostgresVolume recreates a volume whose password does not match, so requiring
# either would never clear.
function Wait-ForPostgres {
    param([hashtable]$EnvValues)

    $port = $DevPostgresHostPort
    if ($null -ne $EnvValues -and -not [string]::IsNullOrWhiteSpace($EnvValues['POSTGRES_HOST_PORT'])) {
        $port = $EnvValues['POSTGRES_HOST_PORT']
    }

    [int]$timeoutSeconds = 60
    [datetime]$deadline = (Get-Date).AddSeconds($timeoutSeconds)
    # Sticky: once Postgres has answered inside the container, a later pg_isready blip does not
    # turn this into a container problem. The remaining fault is host reachability, which is the
    # one Repair-PostgresHostReachability can act on.
    [bool]$everReadyInContainer = $false
    do {
        docker compose -f $ComposeFile exec -T postgres pg_isready -U postgres -d postgres *> $null
        if ($LASTEXITCODE -eq 0) {
            $everReadyInContainer = $true
            if (Test-DevPostgresHostReachable $port) {
                return 'Ready'
            }
        }
        Start-Sleep -Seconds 1
    } while ((Get-Date) -lt $deadline)

    if (-not $everReadyInContainer) {
        throw "Postgres did not become ready inside the Docker container within ${timeoutSeconds}s. Check: docker compose logs postgres"
    }

    # In-container Postgres is up but the published port does not reach it. Report instead of
    # throwing: Ensure-PostgresReady hands this to Repair-PostgresHostReachability, which
    # recreates the container or moves POSTGRES_HOST_PORT to a free port.
    $detail = Get-DevPostgresHostCheckDetail
    $suffix = if ($detail) { ": $detail" } else { '' }
    Write-Step "Postgres is ready in the container but ${DevPostgresConnectHost}:${port} does not reach it$suffix"
    return 'HostUnreachable'
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
    Write-Step "Starting Postgres (Docker) on ${DevPostgresConnectHost}:$($EnvValues['POSTGRES_HOST_PORT'])"

    Assert-DockerRunning

    Ensure-PostgresReady $EnvValues
}

function Start-ClamAv {
    if (-not (Test-DevClamAvOptedIn)) {
        Write-Step 'Skipping ClamAV (set HC_ENABLE_CLAMAV=1 to scan uploads; scans fail open without it)'
        return
    }

    Write-Step "Starting ClamAV (Docker) on localhost:$($script:DevClamAvHostPort)"

    Assert-DockerRunning

    Ensure-DevClamAvRunning -Port $script:DevClamAvHostPort
}

function Build-Projects {
    $script:ApiBuildFailed = $false
    $skipDotnet = $env:HC_SKIP_DOTNET_BUILD -eq '1' -or $env:HC_SKIP_BUILD -eq '1'
    $apiBuildLog = Join-Path ([System.IO.Path]::GetTempPath()) 'hc-api-build-errors.log'
    $apiBuildJob = $null
    $rustBuildJob = $null

    Ensure-FrontendDependencies -FrontendDir $FrontendDir

    if ($env:HC_SKIP_RUST_BUILD -ne '1' -and $env:HC_SKIP_BUILD -ne '1') {
        Require-RustCargo
        $rustBuildJob = Start-Job -ScriptBlock {
            param($ScriptRoot, $Path)
            $env:Path = $Path
            . (Join-Path $ScriptRoot 'dev-stack-lib.ps1')
            Build-RustWorkspace
        } -ArgumentList $PSScriptRoot, $env:Path
    }

    if ($skipDotnet) {
        Write-Step 'Skipping API build (HC_SKIP_DOTNET_BUILD=1)'
    } else {
        Write-Step 'Building API (parallel with frontend typecheck and Rust)'
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
    $rustBuildFailed = $false
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

        if ($null -ne $rustBuildJob) {
            try {
                Wait-RustWorkspaceJob -Job $rustBuildJob
            } catch {
                Write-Host $_.Exception.Message
                $rustBuildFailed = $true
            }
        }
    }

    if ($frontendTypecheckFailed) {
        throw 'Frontend typecheck failed'
    }

    if ($rustBuildFailed) {
        throw 'Rust cargo build --workspace failed'
    }

    if ($hostCheckFailed) {
        throw 'PostgresHostCheck build failed'
    }
}

function Start-DevStack([hashtable]$EnvValues) {
    $apiStarter = Join-Path $RepoRoot 'scripts/start-api-dev.ps1'
    $frontendStarter = Join-Path $RepoRoot 'scripts/start-frontend-dev.ps1'

    $env:HC_SKIP_BROWSER_OPEN = '1'

    Write-Step 'Starting frontend in a new terminal (http://localhost:5173)'
    $frontendArgs = @('-NoExit', '-File', $frontendStarter)
    if (-not $SkipDocker) {
        $frontendArgs += '-PreRegistered'
    }
    Start-DevStackPowerShellProcess -ArgumentList $frontendArgs -WorkingDirectory $RepoRoot

    if (-not $script:ApiBuildFailed) {
        Write-Step 'Starting API in a new terminal (http://localhost:5000)'
        $apiArgs = @('-NoExit', '-File', $apiStarter)
        if ($SkipDocker) {
            $apiArgs += '-SkipDocker'
        } else {
            $apiArgs += '-PreRegistered'
        }

        # The parent has just completed the API build, so avoid rebuilding it in the child
        # process before Kestrel can bind. Preserve an explicitly supplied value afterwards.
        $previousSkipDotnetBuild = $env:HC_SKIP_DOTNET_BUILD
        $previousSkipRustBuild = $env:HC_SKIP_RUST_BUILD
        $previousStripped = $env:HC_DEV_STRIPPED
        $env:HC_SKIP_DOTNET_BUILD = '1'
        $env:HC_SKIP_RUST_BUILD = '1'
        if ($Stripped -or $env:HC_DEV_STRIPPED -eq '1') {
            $env:HC_DEV_STRIPPED = '1'
        }
        try {
            Start-DevStackPowerShellProcess -ArgumentList $apiArgs -WorkingDirectory $RepoRoot
        } finally {
            if ($null -eq $previousSkipDotnetBuild) {
                Remove-Item Env:HC_SKIP_DOTNET_BUILD -ErrorAction SilentlyContinue
            } else {
                $env:HC_SKIP_DOTNET_BUILD = $previousSkipDotnetBuild
            }
            if ($null -eq $previousSkipRustBuild) {
                Remove-Item Env:HC_SKIP_RUST_BUILD -ErrorAction SilentlyContinue
            } else {
                $env:HC_SKIP_RUST_BUILD = $previousSkipRustBuild
            }
            if ($null -eq $previousStripped) {
                Remove-Item Env:HC_DEV_STRIPPED -ErrorAction SilentlyContinue
            } else {
                $env:HC_DEV_STRIPPED = $previousStripped
            }
        }
    } else {
        Write-Step 'Skipping API start because the build failed (see API Build Errors browser tab)'
    }

    Write-Step 'Opening the frontend when Vite is ready (API root is a 403 landing page, not the app)'
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
        Write-Host "  Postgres: ${DevPostgresConnectHost}:" -NoNewline
        Write-Host $EnvValues['POSTGRES_HOST_PORT'] -NoNewline
        Write-Host ' (Docker; stops when both terminals are closed)'
        $fcaptchaPort = $EnvValues['FCAPTCHA_HOST_PORT']
        if ([string]::IsNullOrWhiteSpace($fcaptchaPort)) {
            $fcaptchaPort = $script:DevFCaptchaHostPort
        }
        Write-Host "  FCaptcha: localhost:$fcaptchaPort (Docker; stops when both terminals are closed)"
        if (Test-DevClamAvOptedIn) {
            Write-Host "  ClamAV:   localhost:$($script:DevClamAvHostPort) (Docker; upload scanning, fail-open while loading)"
        } else {
            Write-Host '  ClamAV:   off (uploads scan as NotScanned; set HC_ENABLE_CLAMAV=1 to enable)'
        }
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
        # Stop a leftover managed session *before* Start-Postgres waits on the published
        # port. Doing this afterwards tears down the container that wait just cleared,
        # and the API child used to skip its own wait because -PreRegistered was set.
        Initialize-DevStackState -PostgresPort $EnvValues['POSTGRES_HOST_PORT'] -ServerCount 2
        Start-Postgres -EnvValues $EnvValues
        Start-ClamAv
    } else {
        Write-Step 'Skipping Docker Postgres, FCaptcha and ClamAV (HC_SKIP_DOCKER / -SkipDocker)'
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

if ($env:HC_DEV_STRIPPED -eq '1') {
    $Stripped = $true
}

Push-Location $RepoRoot
try {
    if ($Stripped) {
        $env:HC_DEV_STRIPPED = '1'
        Write-Step 'Stripped mode: leftover neural training sessions will be paused; neural warmup/refresh will not start; FCaptcha is not joined before the API'
    }

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
