# Launch the API for local development using secrets from .env.
#
# Usage:
#   scripts/start-api-dev.ps1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$ApiProject = Join-Path $RepoRoot 'backend/HomeworkCentral.Api/HomeworkCentral.Api.csproj'
$EnvFile = Join-Path $RepoRoot '.env'

function Read-DevEnv {
    if (-not (Test-Path $EnvFile)) {
        throw ".env not found at $EnvFile. Run scripts/run-dev.ps1 first."
    }

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

    if ([string]::IsNullOrWhiteSpace($values['JWT_SECRET'])) {
        throw 'JWT_SECRET is not set in .env'
    }
    if ([string]::IsNullOrWhiteSpace($values['POSTGRES_PASSWORD'])) {
        throw 'POSTGRES_PASSWORD is not set in .env'
    }
    if ([string]::IsNullOrWhiteSpace($values['POSTGRES_HOST_PORT'])) {
        $values['POSTGRES_HOST_PORT'] = '5432'
    }

    return $values
}

function Format-PostgresConnectionString([hashtable]$EnvValues) {
    $pgPort = $EnvValues['POSTGRES_HOST_PORT']
    $quotedPassword = $EnvValues['POSTGRES_PASSWORD'].Replace("'", "''")
    return "Host=localhost;Port=$pgPort;Database=homework_central;Username=postgres;Password='$quotedPassword'"
}

$envValues = Read-DevEnv

$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://localhost:5000'
$env:Jwt__Secret = $envValues['JWT_SECRET']
$env:ConnectionStrings__DefaultConnection = Format-PostgresConnectionString $envValues

Write-Host 'Homework Central API - http://localhost:5000' -ForegroundColor Cyan
Write-Host 'Using database credentials from .env' -ForegroundColor DarkGray

Push-Location $RepoRoot
try {
    dotnet run --project $ApiProject --no-build --no-launch-profile --urls http://localhost:5000
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
finally {
    Pop-Location
}
