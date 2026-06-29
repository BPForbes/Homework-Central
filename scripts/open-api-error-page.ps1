# Write a temporary HTML page listing API errors and open it in the browser.
param(
    [Parameter(Mandatory = $true)]
    [string]$Title,
    [Parameter(Mandatory = $true)]
    [string]$ErrorLogFile
)

if (-not (Test-Path $ErrorLogFile)) {
    throw "Error log not found: $ErrorLogFile"
}

$encodedTitle = [System.Net.WebUtility]::HtmlEncode($Title)
$encodedBody = [System.Net.WebUtility]::HtmlEncode((Get-Content -Raw -Path $ErrorLogFile))
$htmlPath = Join-Path ([System.IO.Path]::GetTempPath()) ("hc-api-errors-{0}.html" -f ([guid]::NewGuid().ToString('N')))

@"

<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8" />
  <title>$encodedTitle</title>
  <style>
    body { margin: 0; font-family: "Segoe UI", Tahoma, Geneva, Verdana, sans-serif; background: #f5f5f5; }
    .header { background: #808080; color: #ffffff; padding: 0.5rem 1rem; font-size: 1.25rem; font-weight: 600; }
    pre { margin: 0; padding: 1rem; background: #ffffff; color: #000000; white-space: pre-wrap; word-break: break-word; font-size: 0.9rem; line-height: 1.4; }
  </style>
</head>
<body>
  <div class="header">$encodedTitle</div>
  <pre>$encodedBody</pre>
</body>
</html>
"@ | Set-Content -Path $htmlPath -Encoding UTF8

& (Join-Path $PSScriptRoot 'open-dev-browser.ps1') -Url $htmlPath
