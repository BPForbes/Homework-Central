# Launch the API for local development.
#
# Local Postgres credentials are fixed: postgres / postgres
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
$DevPostgresUser = 'postgres'
$DevPostgresPassword = 'postgres'

function Read-JwtSecret {
    if (-not (Test-Path $EnvFile)) {
        throw ".env not found at $EnvFile. Run scripts/run-dev.ps1 first."
    }

    $jwtSecret = ''
    $pgPort = '5433'

    foreach ($line in Get-Content $EnvFile) {
        if ($line -match '^\s*#' -or $line -notmatch '=') { continue }
        $name, $value = $line -split '=', 2
        $name = $name.Trim()
        if ($name -eq 'JWT_SECRET') {
            $jwtSecret = $value.Trim()
        }
        if ($name -eq 'POSTGRES_HOST_PORT' -and -not [string]::IsNullOrWhiteSpace($value)) {
            $pgPort = $value.Trim()
        }
    }

    if ([string]::IsNullOrWhiteSpace($jwtSecret)) {
        throw 'JWT_SECRET is not set in .env'
    }

    return @{
        JWT_SECRET = $jwtSecret
        POSTGRES_HOST_PORT = $pgPort
    }
}

$envValues = Read-JwtSecret
$connectionString = "Host=localhost;Port=$($envValues['POSTGRES_HOST_PORT']);Database=homework_central;Username=$DevPostgresUser;Password=$DevPostgresPassword"

$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://localhost:5000'
$env:Jwt__Secret = $envValues['JWT_SECRET']
$env:ConnectionStrings__DefaultConnection = $connectionString

Write-Host 'Homework Central API - http://localhost:5000' -ForegroundColor Cyan
Write-Host "Using Postgres user $DevPostgresUser on localhost:$($envValues['POSTGRES_HOST_PORT']) (local dev)" -ForegroundColor DarkGray

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
