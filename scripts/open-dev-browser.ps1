# Open a URL in the default browser (best effort).
param(
    [Parameter(Mandatory = $true)]
    [string]$Url
)

if ($IsWindows -or $env:OS -match '(?i)Windows') {
    Start-Process $Url
} elseif (Get-Command xdg-open -ErrorAction SilentlyContinue) {
    Start-Process xdg-open -ArgumentList $Url
} elseif (Get-Command open -ErrorAction SilentlyContinue) {
    Start-Process open -ArgumentList $Url
} else {
    Write-Host "==> Open in browser: $Url"
}
