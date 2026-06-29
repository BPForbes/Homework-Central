# Wait until an HTTP endpoint responds, then open it in the browser.
param(
    [Parameter(Mandatory = $true)]
    [string]$Url,
    [string]$Label = 'server',
    [int]$MaxAttempts = 90
)

$ErrorActionPreference = 'SilentlyContinue'

for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
  try {
    $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 2
    if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 500) {
      & (Join-Path $PSScriptRoot 'open-dev-browser.ps1') -Url $Url
      exit 0
    }
  } catch {
    # keep waiting
  }
  Start-Sleep -Seconds 1
}

Write-Host "==> Timed out waiting for $Label at $Url" -ForegroundColor Yellow
exit 1
