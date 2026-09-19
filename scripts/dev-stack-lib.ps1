# Shared helpers for starting/stopping the local Homework Central dev stack.
# Dot-source from other scripts in this directory; do not run directly.

Set-StrictMode -Version Latest

if (-not (Get-Variable -Name RepoRoot -Scope Script -ErrorAction SilentlyContinue)) {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
}

$script:DevStackStateFile = Join-Path $script:RepoRoot '.hc-dev-stack.state'
$script:DevStackMutexName = 'Global\HomeworkCentralDevStack'
$script:DevStackComposeFile = Join-Path $script:RepoRoot 'docker-compose.yml'
$script:DevStackEnvFile = Join-Path $script:RepoRoot '.env'
$script:DevPostgresPassword = 'postgres'
$script:DevPostgresHostPort = '5434'
$script:DevFCaptchaHostPort = '3010'
$script:DevClamAvHostPort = '3310'
# Must match docker-compose.yml's `fcaptcha` service image tag.
$script:DevFCaptchaImage = 'homework-central-fcaptcha:1.12.0'
$script:DevStackServerRegistered = $false
# Last stdout/stderr from PostgresHostCheck, surfaced by readiness waits when they time out.
$script:DevPostgresHostCheckDetail = ''

# 127.0.0.1 rather than localhost: localhost prefers ::1 on Windows and burns the whole connect
# timeout while Docker Desktop has published IPv4 only.
#
# Deliberately reads only the caller's script scope, never $env:. run-dev.ps1 assigns this
# before it dot-sources this file, so the guard preserves that value; start-api-dev.ps1 does
# not, so it gets the loopback default. The probe carries the dev Postgres credentials, and the
# "already running" check clears on any answer, so neither may be aimed by the environment.
if (-not (Get-Variable -Name DevPostgresConnectHost -Scope Script -ErrorAction SilentlyContinue)) {
    $script:DevPostgresConnectHost = '127.0.0.1'
}

function New-DevRandomSecret {
    $bytes = New-Object byte[] 48
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    # URL-safe base64 avoids special characters breaking connection strings and shells.
    return [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function Read-DevEnvFile {
    $values = @{
        JWT_SECRET = ''
        FCAPTCHA_SECRET = ''
        POSTGRES_PASSWORD = ''
        POSTGRES_HOST_PORT = $script:DevPostgresHostPort
        FCAPTCHA_HOST_PORT = $script:DevFCaptchaHostPort
    }

    if (-not (Test-Path $script:DevStackEnvFile)) {
        return $values
    }

    foreach ($line in Get-Content $script:DevStackEnvFile) {
        if ($line -match '^\s*#' -or $line -notmatch '=') { continue }
        $name, $value = $line -split '=', 2
        $name = $name.Trim()
        if ($values.ContainsKey($name)) {
            $values[$name] = $value.Trim()
        }
    }

    if ([string]::IsNullOrWhiteSpace($values['POSTGRES_HOST_PORT'])) {
        $values['POSTGRES_HOST_PORT'] = $script:DevPostgresHostPort
    }

    if ([string]::IsNullOrWhiteSpace($values['FCAPTCHA_HOST_PORT'])) {
        $values['FCAPTCHA_HOST_PORT'] = $script:DevFCaptchaHostPort
    }

    return $values
}

function Update-DevEnvFileValues([hashtable]$Values) {
    $lines = Get-Content $script:DevStackEnvFile
    $newLines = @()
    $seen = @{}

    foreach ($line in $lines) {
        if ($line -match '^\s*#' -or $line -notmatch '=') {
            $newLines += $line
            continue
        }

        $name = ($line -split '=', 2)[0].Trim()
        if ($Values.ContainsKey($name)) {
            $newLines += "$name=$($Values[$name])"
            $seen[$name] = $true
        } else {
            $newLines += $line
        }
    }

    foreach ($key in $Values.Keys) {
        if (-not $seen.ContainsKey($key) -and -not [string]::IsNullOrWhiteSpace($Values[$key])) {
            $newLines += "$key=$($Values[$key])"
        }
    }

    Set-Content -Path $script:DevStackEnvFile -Value $newLines
}

function Ensure-DevEnvFile {
    param(
        [switch]$RollFCaptchaSecret
    )

    $exampleFile = Join-Path $script:RepoRoot '.env.example'
    if (-not (Test-Path $script:DevStackEnvFile)) {
        Write-Host '==> Creating .env from .env.example' -ForegroundColor DarkGray
        Copy-Item $exampleFile $script:DevStackEnvFile
    }

    $values = Read-DevEnvFile
    $updated = $false

    if ([string]::IsNullOrWhiteSpace($values['JWT_SECRET']) -or $values['JWT_SECRET'] -eq 'replace-with-a-long-random-secret') {
        $values['JWT_SECRET'] = New-DevRandomSecret
        $updated = $true
    }

    if ($RollFCaptchaSecret) {
        $values['FCAPTCHA_SECRET'] = New-DevRandomSecret
        $updated = $true
    } elseif ([string]::IsNullOrWhiteSpace($values['FCAPTCHA_SECRET']) -or $values['FCAPTCHA_SECRET'] -eq 'replace-with-a-long-random-secret') {
        $values['FCAPTCHA_SECRET'] = New-DevRandomSecret
        $updated = $true
    }

    if ([string]::IsNullOrWhiteSpace($values['POSTGRES_PASSWORD']) -or $values['POSTGRES_PASSWORD'] -ne $script:DevPostgresPassword) {
        $values['POSTGRES_PASSWORD'] = $script:DevPostgresPassword
        $updated = $true
    }

    if ($values['POSTGRES_HOST_PORT'] -eq '5432' -or $values['POSTGRES_HOST_PORT'] -eq '5433') {
        Write-Host "==> Using POSTGRES_HOST_PORT=$($script:DevPostgresHostPort) (avoids local PostgreSQL on 5432/5433)" -ForegroundColor DarkGray
        $values['POSTGRES_HOST_PORT'] = $script:DevPostgresHostPort
        $updated = $true
    }

    if ($updated) {
        Update-DevEnvFileValues $values
        if ($RollFCaptchaSecret) {
            Write-Host '==> Rolled FCAPTCHA_SECRET in .env (local only, not committed)' -ForegroundColor DarkGray
        } else {
            Write-Host '==> Generated secrets in .env (local only, not committed)' -ForegroundColor DarkGray
        }
        $values = Read-DevEnvFile
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
    if ([string]::IsNullOrWhiteSpace($values['FCAPTCHA_SECRET'])) {
        throw 'FCAPTCHA_SECRET is not set in .env'
    }
    if ($values['FCAPTCHA_SECRET'].Length -lt 32) {
        throw 'FCAPTCHA_SECRET must be at least 32 characters'
    }

    return $values
}

function Read-DevStackState {
    if (-not (Test-Path $script:DevStackStateFile)) {
        return $null
    }

    $state = @{}
    foreach ($line in Get-Content $script:DevStackStateFile) {
        if ($line -match '^\s*#' -or $line -notmatch '=') { continue }
        $name, $value = $line -split '=', 2
        $state[$name.Trim()] = $value.Trim()
    }

    return $state
}

function Write-DevStackState([hashtable]$State) {
    $lines = foreach ($key in ($State.Keys | Sort-Object)) {
        "$key=$($State[$key])"
    }
    Set-Content -Path $script:DevStackStateFile -Value $lines
}

function Get-PostgresHostCheckDll {
    return Join-Path $script:RepoRoot 'scripts/PostgresHostCheck/bin/Debug/net10.0/PostgresHostCheck.dll'
}

function Build-PostgresHostCheckIfNeeded {
    $dll = Get-PostgresHostCheckDll
    $project = Join-Path $script:RepoRoot 'scripts/PostgresHostCheck/PostgresHostCheck.csproj'
    $projectDir = Split-Path $project -Parent
    if (-not (Test-Path $dll)) {
        Build-PostgresHostCheck -Project $project
        return
    }

    # Only hand-written sources count. bin/ and obj/ hold MSBuild-generated .cs files, and a
    # Release build (CI, CodeQL) leaves them newer than this Debug output forever, which would
    # make every readiness attempt rebuild the checker.
    #
    # The bin/obj test runs on each path relative to the project directory, never the full
    # path: a checkout whose own ancestors include a directory named bin or obj would otherwise
    # filter out every hand-written source, and a Program.cs edit would never look stale.
    [System.IO.FileInfo]$built = Get-Item $dll
    [int]$projectDirLength = $projectDir.Length
    $sourceFiles = @(Get-Item $project) + @(
        Get-ChildItem -Path $projectDir -Filter '*.cs' -Recurse -File |
            Where-Object { $_.FullName.Substring($projectDirLength) -notmatch '[\\/](bin|obj)[\\/]' }
    )
    $newestSource = $sourceFiles | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if ($built.LastWriteTimeUtc -ge $newestSource.LastWriteTimeUtc) {
        return
    }

    Build-PostgresHostCheck -Project $project
}

function Build-PostgresHostCheck {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Project
    )

    # Logged only on the rebuild path: the readiness loops call the staleness check once per
    # attempt, so an "already built" line would repeat for as long as a wait runs.
    Write-Host '==> Building Postgres host check' -ForegroundColor DarkGray
    dotnet build $Project -c Debug -v q *> $null
    if ($LASTEXITCODE -ne 0) {
        throw 'PostgresHostCheck build failed'
    }
}

# Returns the checker's exit code and records its output in
# $script:DevPostgresHostCheckDetail. See scripts/PostgresHostCheck/Program.cs for the
# meaning of each code.
function Invoke-DevPostgresHostCheck([string]$Port) {
    Build-PostgresHostCheckIfNeeded
    $dll = Get-PostgresHostCheckDll
    if (-not (Test-Path $dll)) {
        $script:DevPostgresHostCheckDetail = 'PostgresHostCheck is not built'
        return 1
    }

    # $LASTEXITCODE must be read straight off the native call; a redirection or a
    # pipeline stage in between can overwrite it with the cmdlet's own status.
    $output = & dotnet $dll $Port $script:DevPostgresConnectHost 2>&1
    [int]$hostCheckExit = $LASTEXITCODE
    $script:DevPostgresHostCheckDetail = ($output | Out-String).Trim()
    return $hostCheckExit
}

function Test-DevPostgresConnection([string]$Port) {
    return (Invoke-DevPostgresHostCheck $Port) -eq 0
}

# Readiness gate for "the host can reach Docker Postgres on this published port".
# Exit code 3 (a server answered without handing back a usable master database, normally a fresh
# volume) and exit code 5 (the volume's password is not the dev one) both mean a server answered
# and refused this connection, which still proves
# the published port reaches Postgres. run-dev creates the database and resets a mismatched
# volume only after this wait, so treating either as not-ready deadlocks the wait against its
# own repair. Exit code 4 (server not accepting sessions yet) stays not-ready: it clears on
# its own.
#
# Silent, because polling loops call it once per second. One-shot callers should prefer
# Test-DevPostgresAlreadyRunning, which names the rejection.
function Test-DevPostgresHostReachable([string]$Port) {
    [int]$hostCheckExit = Invoke-DevPostgresHostCheck $Port
    return $hostCheckExit -in @(0, 3, 5)
}

# True when a server answered and rejected the dev credentials, which means the volume behind
# it was initialised with a different password.
#
# Only the host's view of the published port can establish this. A psql probe run inside the
# container connects over loopback, which initdb trusts ahead of the image's scram-sha-256
# rule, so it authenticates against no password and succeeds on a mismatched volume.
function Test-DevPostgresCredentialsRejected([string]$Port) {
    # Cast before comparing: this gates a destructive reset in run-dev, and `@(x, 5) -eq 5` would
    # be truthy if the helper ever emitted an extra object alongside its exit code.
    [int]$hostCheckExit = Invoke-DevPostgresHostCheck $Port
    return $hostCheckExit -eq 5
}

function Write-DevPostgresRejected([string]$Port) {
    [string]$detail = Get-DevPostgresHostCheckDetail
    if (-not $detail) {
        $detail = 'no detail'
    }

    Write-Host "==> Postgres answered on ${script:DevPostgresConnectHost}:${Port} but rejected the check: $detail" -ForegroundColor DarkGray
}

# One-shot "Postgres is already up on this published port" check, for the callers that skip
# starting a container. It names a rejected connection rather than swallowing it: nothing on
# this path resets a volume whose password does not match, so an unreported 28P01 would
# resurface as an opaque API startup failure well after the cause scrolled away.
function Test-DevPostgresAlreadyRunning([string]$Port) {
    [int]$hostCheckExit = Invoke-DevPostgresHostCheck $Port
    if ($hostCheckExit -in @(3, 5)) {
        Write-DevPostgresRejected $Port
    }

    return $hostCheckExit -in @(0, 3, 5)
}

function Get-DevPostgresHostCheckDetail {
    return $script:DevPostgresHostCheckDetail
}

function Start-DevStackPostgresContainer {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Port,
        [switch]$ForceRecreate
    )

    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw 'Docker CLI not found. Install Docker Desktop or run scripts/run-dev.ps1 first.'
    }

    docker info *> $null
    if ($LASTEXITCODE -ne 0) {
        throw 'Docker is not running. Start Docker Desktop and retry.'
    }

    $env:POSTGRES_PASSWORD = $script:DevPostgresPassword
    $env:POSTGRES_HOST_PORT = $Port
    $composeArgs = @('-f', $script:DevStackComposeFile, '--env-file', $script:DevStackEnvFile, 'up', '-d')
    if ($ForceRecreate) {
        $composeArgs += '--force-recreate'
    }
    $composeArgs += 'postgres'
    docker compose @composeArgs
    if ($LASTEXITCODE -ne 0) {
        throw 'docker compose up postgres failed'
    }
}

function Wait-DevPostgresReady([string]$Port) {
    [int]$timeoutSeconds = 60
    [datetime]$deadline = (Get-Date).AddSeconds($timeoutSeconds)
    do {
        [int]$hostCheckExit = Invoke-DevPostgresHostCheck $Port
        if ($hostCheckExit -eq 0) {
            return
        }

        if ($hostCheckExit -in @(3, 5)) {
            # Nothing repairs the volume behind this wait — start-api-dev only starts the
            # container — so name the rejection instead of leaving the API to fail on it.
            Write-DevPostgresRejected $Port
            return
        }

        Start-Sleep -Seconds 1
    } while ((Get-Date) -lt $deadline)

    $detail = Get-DevPostgresHostCheckDetail
    $suffix = if ($detail) { ": $detail" } else { '' }
    throw "Postgres did not become ready on ${script:DevPostgresConnectHost}:${Port} within ${timeoutSeconds}s$suffix"
}

function Join-DevStackIfManaged([string]$Port) {
    if ($env:HC_DEV_STACK_PREREGISTERED -eq '1') {
        return
    }

    Invoke-DevStackStateUpdate {
        $state = Read-DevStackState
        if ($null -eq $state -or $state['managed_postgres'] -ne '1') {
            return
        }

        if ($state['postgres_port'] -ne $Port) {
            return
        }

        $refcount = [int]$state['refcount'] + 1
        $state['refcount'] = "$refcount"
        Write-DevStackState $state
        $script:DevStackServerRegistered = $true
    }
}

function Ensure-DevPostgresRunning([string]$Port) {
    # "Already running" is a reachability question, not a homework_central_master question:
    # on a freshly wiped volume the server is up before that database exists, and starting a
    # second time would skip the refcount join that keeps the container alive for both servers.
    if (Test-DevPostgresAlreadyRunning $Port) {
        Join-DevStackIfManaged -Port $Port
        return
    }

    Write-Host "==> Starting Docker Postgres on ${script:DevPostgresConnectHost}:$Port" -ForegroundColor DarkGray
    Start-DevStackPostgresContainer $Port
    Wait-DevPostgresReady $Port

    Invoke-DevStackStateUpdate {
        $state = Read-DevStackState
        if ($null -eq $state) {
            Write-DevStackState @{
                managed_postgres = '1'
                postgres_port = $Port
                refcount = '1'
            }
            $script:DevStackServerRegistered = $true
        }
    }
}

function Test-DevStackServerOwnsRef {
    return $script:DevStackServerRegistered -or $env:HC_DEV_STACK_PREREGISTERED -eq '1'
}

function Stop-DevStackPostgres([string]$Port) {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        return
    }

    $env:POSTGRES_PASSWORD = $script:DevPostgresPassword
    $env:POSTGRES_HOST_PORT = $Port
    docker compose -f $script:DevStackComposeFile --env-file $script:DevStackEnvFile stop postgres *> $null
}

# FCaptcha (see docker-compose.yml's `fcaptcha` service) is stateless — no volume, no credentials
# to reset — so unlike Postgres above it doesn't need refcounted start/stop bookkeeping in
# .hc-dev-stack.state; it's simply started alongside Postgres and stopped whenever Postgres is.
function Start-DevStackFCaptchaContainer([string]$Port, [switch]$ForceRecreate) {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw 'Docker CLI not found. Install Docker Desktop or run scripts/run-dev.ps1 first.'
    }

    docker info *> $null
    if ($LASTEXITCODE -ne 0) {
        throw 'Docker is not running. Start Docker Desktop and retry.'
    }

    $env:FCAPTCHA_HOST_PORT = $Port
    $composeArgs = @('-f', $script:DevStackComposeFile, '--env-file', $script:DevStackEnvFile, 'up', '-d')

    # '--build' used to be passed on every start. The build context is a pinned upstream tag that
    # never changes between runs, so that woke BuildKit — and the memory its daemon holds for the
    # build graph and cache — for a no-op rebuild each time the dev stack came up. Build only when
    # the image really is missing, or when HC_FCAPTCHA_REBUILD=1 forces it.
    docker image inspect $script:DevFCaptchaImage *> $null
    if ($LASTEXITCODE -ne 0 -or $env:HC_FCAPTCHA_REBUILD -eq '1') {
        $composeArgs += '--build'
    }

    if ($ForceRecreate) {
        $composeArgs += '--force-recreate'
    }
    $composeArgs += 'fcaptcha'
    docker compose @composeArgs
    if ($LASTEXITCODE -ne 0) {
        throw 'docker compose up fcaptcha failed (first run builds from github.com/WebDecoy/FCaptcha v1.12.0 — check network and Docker BuildKit)'
    }
}

# Postgres helper first, then the existing FCaptcha helper in the background.
# /healthz only needs the master database; login captcha can finish after Ready.
function Start-DevStackPostgresThenFCaptchaBackground {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PostgresPort,
        [Parameter(Mandatory = $true)]
        [string]$FCaptchaPort,
        [switch]$ForceRecreate
    )

    Start-DevStackPostgresContainer -Port $PostgresPort -ForceRecreate:$ForceRecreate
    $libPath = Join-Path $PSScriptRoot 'dev-stack-lib.ps1'
    Start-Job -ScriptBlock {
        param($LibPath, $Port, $Force, $Path)
        $env:Path = $Path
        . $LibPath
        if ($Force) {
            Start-DevStackFCaptchaContainer -Port $Port -ForceRecreate
        }
        else {
            Start-DevStackFCaptchaContainer -Port $Port
        }
    } -ArgumentList $libPath, $FCaptchaPort, [bool]$ForceRecreate, $env:Path | Out-Null
}

function Get-DevFCaptchaContainerSecret {
    $containerId = docker compose -f $script:DevStackComposeFile --env-file $script:DevStackEnvFile ps -q fcaptcha 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($containerId)) {
        return $null
    }

    $lines = docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' $containerId 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $lines) {
        return $null
    }

    foreach ($line in $lines) {
        if ($line -like 'FCAPTCHA_SECRET=*') {
            return ($line -split '=', 2)[1]
        }
    }

    return $null
}

function Test-DevFCaptchaSecretAligned {
    $values = Read-DevEnvFile
    $expected = $values['FCAPTCHA_SECRET']
    if ([string]::IsNullOrWhiteSpace($expected)) {
        return $false
    }

    $actual = Get-DevFCaptchaContainerSecret
    if ([string]::IsNullOrWhiteSpace($actual)) {
        return $false
    }

    return $actual -eq $expected
}

function Test-DevFCaptchaConnection([string]$Port) {
    try {
        $response = Invoke-WebRequest -Uri "http://127.0.0.1:$Port/fcaptcha.js" -UseBasicParsing -TimeoutSec 2
        return $response.StatusCode -eq 200 -and $response.Content.Length -gt 0
    } catch {
        return $false
    }
}

function Wait-DevFCaptchaReady([string]$Port) {
    for ($attempt = 1; $attempt -le 30; $attempt++) {
        if (Test-DevFCaptchaConnection $Port) {
            return
        }
        Start-Sleep -Seconds 1
    }

    throw "FCaptcha did not become ready on localhost:$Port within 30s"
}

function Ensure-DevStackCoreRunning {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PostgresPort,
        [Parameter(Mandatory = $true)]
        [string]$FCaptchaPort
    )

    # Reachability, not homework_central_master: see Ensure-DevPostgresRunning.
    if (Test-DevPostgresAlreadyRunning $PostgresPort) {
        if ((Test-DevFCaptchaConnection $FCaptchaPort) -and (Test-DevFCaptchaSecretAligned)) {
            Join-DevStackIfManaged -Port $PostgresPort
            return
        }

        $libPath = Join-Path $PSScriptRoot 'dev-stack-lib.ps1'
        if (Test-DevFCaptchaConnection $FCaptchaPort) {
            Write-Host '==> Recreating Docker FCaptcha (FCAPTCHA_SECRET changed in .env)' -ForegroundColor DarkGray
            Start-Job -ScriptBlock {
                param($LibPath, $Port, $Path)
                $env:Path = $Path
                . $LibPath
                Start-DevStackFCaptchaContainer -Port $Port -ForceRecreate
            } -ArgumentList $libPath, $FCaptchaPort, $env:Path | Out-Null
        }
        else {
            Start-Job -ScriptBlock {
                param($LibPath, $Port, $Path)
                $env:Path = $Path
                . $LibPath
                Start-DevStackFCaptchaContainer -Port $Port
            } -ArgumentList $libPath, $FCaptchaPort, $env:Path | Out-Null
        }

        Join-DevStackIfManaged -Port $PostgresPort
        return
    }

    Write-Host "==> Starting Docker Postgres on 127.0.0.1:$PostgresPort (FCaptcha continues in the background)" -ForegroundColor DarkGray
    Start-DevStackPostgresThenFCaptchaBackground -PostgresPort $PostgresPort -FCaptchaPort $FCaptchaPort
    Wait-DevPostgresReady $PostgresPort
    Invoke-DevStackStateUpdate {
        $state = Read-DevStackState
        if ($null -eq $state) {
            Write-DevStackState @{
                managed_postgres = '1'
                postgres_port    = $PostgresPort
                refcount         = '1'
            }
            $script:DevStackServerRegistered = $true
        }
    }
}

function Ensure-DevFCaptchaRunning([string]$Port) {
    $needsStart = -not (Test-DevFCaptchaConnection $Port)
    $needsRecreate = -not $needsStart -and -not (Test-DevFCaptchaSecretAligned)

    if ($needsRecreate) {
        Write-Host '==> Recreating Docker FCaptcha (FCAPTCHA_SECRET changed in .env)' -ForegroundColor DarkGray
    }

    if ($needsStart -or $needsRecreate) {
        if ($needsStart) {
            Write-Host "==> Starting Docker FCaptcha on localhost:$Port" -ForegroundColor DarkGray
        }
        Start-DevStackFCaptchaContainer -Port $Port -ForceRecreate:($needsRecreate)
        Wait-DevFCaptchaReady $Port
    }
}

function Stop-DevStackFCaptcha {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        return
    }

    docker compose -f $script:DevStackComposeFile --env-file $script:DevStackEnvFile stop fcaptcha *> $null
}

# ClamAV (see docker-compose.yml's `clamav` service) backs upload malware scanning
# (appsettings.Development.json enables it). Like FCaptcha it is stateless from the app's
# point of view, so it is started alongside Postgres and stopped whenever Postgres is.
# Unlike FCaptcha, readiness is best-effort: the first run downloads virus signatures
# (minutes), and the API scanner fails open (NotScanned) while clamd is unreachable, so a
# slow ClamAV must never block the dev stack.
function Start-DevStackClamAvContainer([string]$Port) {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw 'Docker CLI not found. Install Docker Desktop or run scripts/run-dev.ps1 first.'
    }

    docker info *> $null
    if ($LASTEXITCODE -ne 0) {
        throw 'Docker is not running. Start Docker Desktop and retry.'
    }

    $env:CLAMAV_HOST_PORT = $Port
    docker compose -f $script:DevStackComposeFile --env-file $script:DevStackEnvFile up -d clamav
    if ($LASTEXITCODE -ne 0) {
        throw 'docker compose up clamav failed'
    }
}

function Test-DevClamAvConnection([string]$Port) {
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        if (-not $client.ConnectAsync('127.0.0.1', [int]$Port).Wait(2000)) {
            return $false
        }
        $stream = $client.GetStream()
        $stream.ReadTimeout = 2000
        $ping = [System.Text.Encoding]::ASCII.GetBytes("PING`n")
        $stream.Write($ping, 0, $ping.Length)
        $buffer = New-Object byte[] 16
        $read = $stream.Read($buffer, 0, $buffer.Length)
        return $read -gt 0 -and ([System.Text.Encoding]::ASCII.GetString($buffer, 0, $read)).Trim() -eq 'PONG'
    } catch {
        return $false
    } finally {
        $client.Dispose()
    }
}

# ClamAV is opt-in for local dev: clamd keeps ~1.2-1.5g of signatures resident, which is a
# lot on small machines, and the API scanner fails open (NotScanned) when it's absent.
function Test-DevClamAvOptedIn {
    return $env:HC_ENABLE_CLAMAV -eq '1'
}

function Ensure-DevClamAvRunning([string]$Port) {
    if (-not (Test-DevClamAvOptedIn)) {
        return
    }

    if (Test-DevClamAvConnection $Port) {
        return
    }

    Write-Host "==> Starting Docker ClamAV on localhost:$Port" -ForegroundColor DarkGray
    Start-DevStackClamAvContainer $Port

    for ($attempt = 1; $attempt -le 30; $attempt++) {
        if (Test-DevClamAvConnection $Port) {
            return
        }
        Start-Sleep -Seconds 1
    }

    Write-Host '==> ClamAV is still loading virus signatures (first run downloads them; can take minutes).' -ForegroundColor DarkGray
    Write-Host '==> Uploads scan as NotScanned (fail-open) until clamd is ready; check: docker compose logs clamav' -ForegroundColor DarkGray
}

function Stop-DevStackClamAv {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        return
    }

    docker compose -f $script:DevStackComposeFile --env-file $script:DevStackEnvFile stop clamav *> $null
}

# The local API defaults to the in-memory cache (appsettings.Development.json blanks the
# Redis connection string), but docker-compose.yml defines a `redis` service a developer may
# have started by hand. Stop it with the rest of the stack; a no-op when it was never started.
function Stop-DevStackRedis {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        return
    }

    docker compose -f $script:DevStackComposeFile --env-file $script:DevStackEnvFile stop redis *> $null
}

function Initialize-DevStackState {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PostgresPort,
        [Parameter(Mandatory = $true)]
        [int]$ServerCount
    )

    Invoke-DevStackStateUpdate {
        $existing = Read-DevStackState
        if ($null -ne $existing -and $existing['managed_postgres'] -eq '1') {
            Write-Host '==> Stopping previous dev stack Postgres session' -ForegroundColor DarkGray
            Stop-DevStackPostgres -Port $existing['postgres_port']
            Stop-DevStackFCaptcha
            Stop-DevStackClamAv
            Stop-DevStackRedis
        }

        Write-DevStackState @{
            managed_postgres = '1'
            postgres_port = $PostgresPort
            refcount = "$ServerCount"
        }
    }
}

function Invoke-DevStackStateUpdate([scriptblock]$Action) {
    $mutex = New-Object System.Threading.Mutex($false, $script:DevStackMutexName)
    $acquired = $false
    try {
        $acquired = $mutex.WaitOne(15000)
        if (-not $acquired) {
            throw 'Timed out waiting for dev stack state lock'
        }

        & $Action
    }
    finally {
        if ($acquired) {
            $mutex.ReleaseMutex()
        }
        $mutex.Dispose()
    }
}

function Unregister-DevStackServer {
    if (-not (Test-DevStackServerOwnsRef)) {
        return
    }

    Invoke-DevStackStateUpdate {
        $state = Read-DevStackState
        if ($null -eq $state -or $state['managed_postgres'] -ne '1') {
            return
        }

        $refcount = [int]$state['refcount'] - 1
        if ($refcount -gt 0) {
            $state['refcount'] = "$refcount"
            Write-DevStackState $state
            return
        }

        $port = $state['postgres_port']
        Remove-Item $script:DevStackStateFile -Force -ErrorAction SilentlyContinue
        Stop-DevStackPostgres -Port $port
        Stop-DevStackFCaptcha
        Stop-DevStackClamAv
        Stop-DevStackRedis
        Write-Host '==> Stopped Docker Postgres and freed localhost port' -ForegroundColor DarkGray
    }

    $script:DevStackServerRegistered = $false
}

function Stop-DevStack {
    Invoke-DevStackStateUpdate {
        $state = Read-DevStackState
        if ($null -ne $state -and $state['managed_postgres'] -eq '1') {
            Stop-DevStackPostgres -Port $state['postgres_port']
            Stop-DevStackFCaptcha
            Stop-DevStackClamAv
            Stop-DevStackRedis
        }

        Remove-Item $script:DevStackStateFile -Force -ErrorAction SilentlyContinue
    }
}

function Get-DevStackPowerShellExe {
    $candidates = @(
        (Get-Command pwsh -ErrorAction SilentlyContinue),
        (Get-Command 'C:\Program Files\PowerShell\7\pwsh.exe' -ErrorAction SilentlyContinue),
        (Get-Command powershell -ErrorAction SilentlyContinue)
    ) | Where-Object { $_ -ne $null }

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate.Source) {
            return $candidate.Source
        }
    }

    throw 'PowerShell executable not found (install PowerShell 7+ or use Windows PowerShell).'
}

function Start-DevStackPowerShellProcess {
    param(
        [string[]]$ArgumentList,
        [string]$WorkingDirectory = $script:RepoRoot,
        [System.Diagnostics.ProcessWindowStyle]$WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Normal,
        [switch]$PassThru
    )

    $exe = Get-DevStackPowerShellExe
    $args = @('-NoProfile', '-ExecutionPolicy', 'Bypass') + $ArgumentList
    $process = Start-Process -FilePath $exe -ArgumentList $args -WorkingDirectory $WorkingDirectory -WindowStyle $WindowStyle -PassThru
    if ($PassThru) {
        return $process
    }
}

function Start-FrontendTypecheckJob([string]$FrontendDir) {
    return Start-Job -ScriptBlock {
        param($Dir)
        Push-Location $Dir
        try {
            $output = npx tsc -b --pretty 2>&1
            if ($LASTEXITCODE -ne 0) {
                if ($output) {
                    $output | Write-Output
                }
                throw "frontend typecheck failed with exit code $LASTEXITCODE"
            }
        } finally {
            Pop-Location
        }
    } -ArgumentList $FrontendDir
}

function Test-FrontendDependenciesStale([string]$FrontendDir) {
    $nodeModules = Join-Path $FrontendDir 'node_modules'
    if (-not (Test-Path $nodeModules)) {
        return $true
    }

    $lockFile = Join-Path $FrontendDir 'package-lock.json'
    $stampFile = Join-Path $nodeModules '.package-lock.json'
    if (-not (Test-Path $stampFile)) {
        return $true
    }

    return (Get-Item $lockFile).LastWriteTimeUtc -gt (Get-Item $stampFile).LastWriteTimeUtc
}

function Ensure-FrontendDependencies([string]$FrontendDir) {
    if (Test-FrontendDependenciesStale $FrontendDir) {
        Write-Step 'Installing frontend dependencies'
        npm ci --prefix $FrontendDir
        if ($LASTEXITCODE -ne 0) { throw 'npm ci failed' }
    } else {
        Write-Step 'Frontend dependencies already installed'
    }
}

# Builds rust/ including libhc_kernels for EmbedText, store cosine, GEMV, and related kernels.
# The API loads that library at runtime; C# remains the fallback so the
# Docker image does not need rustc.
function Add-RustupBinToPath {
    $cargoBin = Join-Path $env:USERPROFILE '.cargo\bin'
    if (-not (Test-Path $cargoBin)) {
        return
    }

    $pathEntries = $env:Path -split ';'
    if ($pathEntries -contains $cargoBin) {
        return
    }

    $env:Path = "$cargoBin;$env:Path"
}

function Require-RustCargo {
    Add-RustupBinToPath

    if (-not (Get-Command cargo -ErrorAction SilentlyContinue)) {
        throw 'cargo is required to compile rust/. Install rustup from https://rustup.rs/, then: rustup default stable. Add %USERPROFILE%\.cargo\bin to PATH (open a new PowerShell window). Set HC_SKIP_RUST_BUILD=1 to skip cargo build.'
    }

    if (-not (Get-Command rustc -ErrorAction SilentlyContinue)) {
        throw 'rustc is required to compile rust/. cargo is on PATH but rustc is not — run: rustup default stable. Windows also needs the MSVC linker (Visual Studio Build Tools, Desktop development with C++). Set HC_SKIP_RUST_BUILD=1 to skip cargo build.'
    }
}

function Build-RustWorkspace {
    if ($env:HC_SKIP_RUST_BUILD -eq '1') {
        Write-Host '==> Skipping Rust build (HC_SKIP_RUST_BUILD=1)'
        return
    }
    if ($env:HC_SKIP_BUILD -eq '1') {
        Write-Host '==> Skipping Rust build (HC_SKIP_BUILD=1)'
        return
    }

    # Start-Job runspaces do not always inherit rustup PATH; cargo also writes
    # progress to stderr, which some pwsh hosts treat as a terminating error.
    if (Get-Variable -Name PSNativeCommandUseErrorActionPreference -ErrorAction SilentlyContinue) {
        $PSNativeCommandUseErrorActionPreference = $false
    }

    Require-RustCargo

    Write-Host '==> Building Rust workspace (cargo build --workspace)'
    Push-Location (Join-Path $script:RepoRoot 'rust')
    try {
        & cargo build --workspace
        $cargoExitCode = $LASTEXITCODE
        if ($null -eq $cargoExitCode) {
            if ($?) { $cargoExitCode = 0 } else { $cargoExitCode = 1 }
        }
        if ($cargoExitCode -ne 0) {
            throw "cargo build --workspace failed with exit code $cargoExitCode"
        }
        Copy-HcKernelsNative
    } finally {
        Pop-Location
    }
}

function Copy-HcKernelsNative {
    $destination = Join-Path $script:RepoRoot 'backend/HomeworkCentral.Api/native'
    $debugDirectory = Join-Path $script:RepoRoot 'rust/target/debug'
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    foreach ($name in @('libhc_kernels.so', 'hc_kernels.dll', 'libhc_kernels.dylib')) {
        $source = Join-Path $debugDirectory $name
        if (Test-Path $source) {
            Copy-Item $source (Join-Path $destination $name) -Force
            Write-Host "==> Copied $name into backend/HomeworkCentral.Api/native"
        }
    }
}

function Write-BackgroundJobStreams($Job) {
    $jobErrors = @()
    $output = Receive-Job $Job -ErrorAction SilentlyContinue -ErrorVariable jobErrors
    foreach ($item in @($output)) {
        if ($null -ne $item) {
            Write-Host $item
        }
    }
    foreach ($errorRecord in $jobErrors) {
        Write-Host $errorRecord.ToString()
        if ($null -ne $errorRecord.Exception -and $errorRecord.Exception.Message) {
            Write-Host $errorRecord.Exception.Message
        }
    }
}

function Wait-RustWorkspaceJob($Job) {
    Wait-Job $Job | Out-Null
    $failed = $Job.State -eq 'Failed'
    Write-BackgroundJobStreams -Job $Job
    Remove-Job $Job -Force
    if ($failed) {
        throw 'Rust cargo build --workspace failed'
    }
}

function Wait-FrontendTypecheckJob($Job) {
    Wait-Job $Job | Out-Null
    if ($Job.State -eq 'Failed') {
        $output = Receive-Job $Job
        Remove-Job $Job -Force
        if ($output) {
            $output | Write-Host
        }
        throw 'frontend typecheck failed'
    }

    Receive-Job $Job | Out-Null
    Remove-Job $Job -Force
}
