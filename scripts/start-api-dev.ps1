# Launch the API for local development.
#
# Local Postgres credentials are fixed: postgres / postgres
# Sets HC_DEV_BYPASS=1 so localhost dev auth endpoints and the styled 403 root page are enabled.
#
# Usage:
#   scripts/start-api-dev.ps1
#   scripts/start-api-dev.ps1 -SkipDocker
#   scripts/start-api-dev.ps1 -PreRegistered
[CmdletBinding()]
param(
    [switch]$SkipDocker,
    [switch]$PreRegistered
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$ScriptRoot = $PSScriptRoot
$ApiProject = Join-Path $RepoRoot 'backend/HomeworkCentral.Api/HomeworkCentral.Api.csproj'
$EnvFile = Join-Path $RepoRoot '.env'
$DevPostgresUser = 'postgres'
$DevPostgresPassword = 'postgres'

. (Join-Path $PSScriptRoot 'dev-stack-lib.ps1')

function Read-DevSecrets {
    if (-not (Test-Path $EnvFile)) {
        throw ".env not found at $EnvFile. Run scripts/run-dev.ps1 first."
    }

    $jwtSecret = ''
    $fcaptchaSecret = ''
    $pgPort = '5434'
    $fcaptchaPort = $script:DevFCaptchaHostPort

    foreach ($line in Get-Content $EnvFile) {
        if ($line -match '^\s*#' -or $line -notmatch '=') { continue }
        $name, $value = $line -split '=', 2
        $name = $name.Trim()
        switch ($name) {
            'JWT_SECRET' { $jwtSecret = $value.Trim() }
            'FCAPTCHA_SECRET' { $fcaptchaSecret = $value.Trim() }
            'POSTGRES_HOST_PORT' {
                if (-not [string]::IsNullOrWhiteSpace($value)) {
                    $pgPort = $value.Trim()
                }
            }
            'FCAPTCHA_HOST_PORT' {
                if (-not [string]::IsNullOrWhiteSpace($value)) {
                    $fcaptchaPort = $value.Trim()
                }
            }
        }
    }

    if ([string]::IsNullOrWhiteSpace($jwtSecret)) {
        throw 'JWT_SECRET is not set in .env'
    }
    if ([string]::IsNullOrWhiteSpace($fcaptchaSecret)) {
        throw 'FCAPTCHA_SECRET is not set in .env'
    }

    return @{
        JWT_SECRET = $jwtSecret
        FCAPTCHA_SECRET = $fcaptchaSecret
        POSTGRES_HOST_PORT = $pgPort
        FCAPTCHA_HOST_PORT = $fcaptchaPort
    }
}

$envValues = Read-DevSecrets
$connectionString = "Host=localhost;Port=$($envValues['POSTGRES_HOST_PORT']);Database=homework_central_master;Username=$DevPostgresUser;Password=$DevPostgresPassword"
$adminConnectionString = "Host=localhost;Port=$($envValues['POSTGRES_HOST_PORT']);Database=postgres;Username=$DevPostgresUser;Password=$DevPostgresPassword"

$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://localhost:5000'
$env:Jwt__Secret = $envValues['JWT_SECRET']
$env:FCaptcha__Secret = $envValues['FCAPTCHA_SECRET']
$env:FCaptcha__ServerUrl = "http://localhost:$($envValues['FCAPTCHA_HOST_PORT'])"
$env:FCaptcha__PublicUrl = "http://localhost:$($envValues['FCAPTCHA_HOST_PORT'])"
# Enables DevAuthController, dev seed data, and the styled localhost root page.
$env:HC_DEV_BYPASS = '1'
$env:ConnectionStrings__MasterConnection = $connectionString
$env:ConnectionStrings__PostgresAdmin = $adminConnectionString
$env:Tenancy__ClusterEnvironment = 'dev'

Write-Host 'Homework Central API - http://localhost:5000' -ForegroundColor Cyan
Write-Host "Using Postgres user $DevPostgresUser on localhost:$($envValues['POSTGRES_HOST_PORT']) (local dev)" -ForegroundColor DarkGray
Write-Host "Note: first-run EF logs about missing __EF*MigrationsHistory tables are normal." -ForegroundColor DarkGray
Write-Host "Note: persona databases provision in the background after the API starts listening." -ForegroundColor DarkGray

$skipDocker = $SkipDocker -or $env:HC_SKIP_DOCKER -eq '1'
if ($PreRegistered) {
    $env:HC_DEV_STACK_PREREGISTERED = '1'
}

Push-Location $RepoRoot
$browserProcess = $null
try {
    if (-not $skipDocker) {
        Ensure-DevPostgresRunning -Port $envValues['POSTGRES_HOST_PORT']
        Ensure-DevFCaptchaRunning -Port $envValues['FCAPTCHA_HOST_PORT']
    }

    if ($env:HC_SKIP_BROWSER_OPEN -ne '1') {
        $browserProcess = Start-DevStackPowerShellProcess -WindowStyle Hidden -PassThru -ArgumentList @(
            '-File', (Join-Path $ScriptRoot 'wait-and-open-browser.ps1'),
            '-Url', 'http://localhost:5000/',
            '-Label', 'API',
            '-MaxAttempts', '300'
        ) -WorkingDirectory $RepoRoot
    }

    $errorLog = Join-Path ([System.IO.Path]::GetTempPath()) ("hc-api-run-errors-{0}.log" -f ([guid]::NewGuid().ToString('N')))
    if (Test-Path $errorLog) { Remove-Item $errorLog -Force }

    if ($env:HC_SKIP_DOTNET_BUILD -ne '1' -and $env:HC_SKIP_BUILD -ne '1') {
        Write-Host '==> Building API' -ForegroundColor DarkGray
        dotnet build $ApiProject -c Debug -v q
        if ($LASTEXITCODE -ne 0) {
            throw 'API build failed'
        }
    }

    dotnet run --project $ApiProject --no-build --no-launch-profile --urls http://localhost:5000 2>&1 |
        Tee-Object -FilePath $errorLog
    $exitCode = $LASTEXITCODE

    if ($exitCode -ne 0 -and (Test-Path $errorLog) -and (Get-Item $errorLog).Length -gt 0) {
        & (Join-Path $PSScriptRoot 'open-api-error-page.ps1') -Title 'API Errors' -ErrorLogFile $errorLog
        Remove-Item $errorLog -Force -ErrorAction SilentlyContinue
    }

    if ($exitCode -ne 0) {
        exit $exitCode
    }
}
finally {
    if ($null -ne $browserProcess -and -not $browserProcess.HasExited) {
        Stop-Process -Id $browserProcess.Id -Force -ErrorAction SilentlyContinue
    }
    Pop-Location
    if (-not $skipDocker) {
        Unregister-DevStackServer
    }
}
