# Wait until an HTTP endpoint responds. Treats 2xx/3xx and intentional 403 as reachable for dev probes.
param(
    [Parameter(Mandatory = $true)]
    [string]$Url,
    [string]$Label = 'server',
    [int]$MaxAttempts = 300
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
        return (($response.StatusCode -ge 200 -and $response.StatusCode -lt 400) -or $response.StatusCode -eq 403)
    } catch {
        $webResponse = $_.Exception.Response
        if ($null -eq $webResponse) {
            return $false
        }

        $statusCode = [int]$webResponse.StatusCode
        return (($statusCode -ge 200 -and $statusCode -lt 400) -or $statusCode -eq 403)
    }
}

for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
    if (Test-DevEndpointReady -TargetUrl $Url) {
        exit 0
    }

    Start-Sleep -Seconds 1
}

Write-Host "==> Timed out waiting for $Label at $Url" -ForegroundColor Yellow
exit 1
