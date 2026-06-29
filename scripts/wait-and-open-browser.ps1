# Wait until an HTTP endpoint responds, then open it in the browser.
# Treats 403 as healthy for the API root page (intentional dev forbidden landing).
param(
    [Parameter(Mandatory = $true)]
    [string]$Url,
    [string]$Label = 'server',
    [int]$MaxAttempts = 90
)

$ErrorActionPreference = 'SilentlyContinue'

& (Join-Path $PSScriptRoot 'wait-for-dev-server.ps1') -Url $Url -Label $Label -MaxAttempts $MaxAttempts
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& (Join-Path $PSScriptRoot 'open-dev-browser.ps1') -Url $Url
