# Wait until an HTTP endpoint responds, then open it in the browser.
param(
    [Parameter(Mandatory = $true)]
    [string]$Url,
    [string]$Label = 'server',
    [int]$MaxAttempts = 90
)

$ErrorActionPreference = 'SilentlyContinue'

function Test-DevEndpointReady {
    param([string]$TargetUrl)

    $requestParams = @{
        Uri = $TargetUrl
        UseBasicParsing = $true
        TimeoutSec = 2
    }

    if ((Get-Command Invoke-WebRequest).Parameters.ContainsKey('SkipHttpErrorCheck')) {
        $requestParams.SkipHttpErrorCheck = $true
    }

    try {
        $response = Invoke-WebRequest @requestParams
        return $response.StatusCode -ge 200 -and $response.StatusCode -lt 500
    } catch {
        $webResponse = $_.Exception.Response
        if ($null -eq $webResponse) {
            return $false
        }

        $statusCode = [int]$webResponse.StatusCode
        return $statusCode -ge 200 -and $statusCode -lt 500
    }
}

for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
    if (Test-DevEndpointReady -TargetUrl $Url) {
        & (Join-Path $PSScriptRoot 'open-dev-browser.ps1') -Url $Url
        exit 0
    }

    Start-Sleep -Seconds 1
}

Write-Host "==> Timed out waiting for $Label at $Url" -ForegroundColor Yellow
exit 1
