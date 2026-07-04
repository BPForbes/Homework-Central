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
$script:DevStackServerRegistered = $false

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
    if (Test-Path $dll) {
        return
    }

    $project = Join-Path $script:RepoRoot 'scripts/PostgresHostCheck/PostgresHostCheck.csproj'
    dotnet build $project -c Debug -v q *> $null
    if ($LASTEXITCODE -ne 0) {
        throw 'PostgresHostCheck build failed'
    }
}

function Test-DevPostgresConnection([string]$Port) {
    Build-PostgresHostCheckIfNeeded
    $dll = Get-PostgresHostCheckDll
    if (-not (Test-Path $dll)) {
        return $false
    }

    dotnet $dll $Port *> $null
    return $LASTEXITCODE -eq 0
}

function Start-DevStackPostgresContainer([string]$Port) {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw 'Docker CLI not found. Install Docker Desktop or run scripts/run-dev.ps1 first.'
    }

    docker info *> $null
    if ($LASTEXITCODE -ne 0) {
        throw 'Docker is not running. Start Docker Desktop and retry.'
    }

    $env:POSTGRES_PASSWORD = $script:DevPostgresPassword
    $env:POSTGRES_HOST_PORT = $Port
    docker compose -f $script:DevStackComposeFile --env-file $script:DevStackEnvFile up -d postgres
    if ($LASTEXITCODE -ne 0) {
        throw 'docker compose up postgres failed'
    }
}

function Wait-DevPostgresReady([string]$Port) {
    for ($attempt = 1; $attempt -le 30; $attempt++) {
        if (Test-DevPostgresConnection $Port) {
            return
        }
        Start-Sleep -Seconds 1
    }

    throw "Postgres did not become ready on localhost:$Port within 30s"
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
    if (Test-DevPostgresConnection $Port) {
        Join-DevStackIfManaged -Port $Port
        return
    }

    Write-Host "==> Starting Docker Postgres on localhost:$Port" -ForegroundColor DarkGray
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
    $composeArgs = @('-f', $script:DevStackComposeFile, '--env-file', $script:DevStackEnvFile, 'up', '-d', '--build')
    if ($ForceRecreate) {
        $composeArgs += '--force-recreate'
    }
    $composeArgs += 'fcaptcha'
    docker compose @composeArgs
    if ($LASTEXITCODE -ne 0) {
        throw 'docker compose up fcaptcha failed (first run builds from github.com/WebDecoy/FCaptcha v1.12.0 — check network and Docker BuildKit)'
    }
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
