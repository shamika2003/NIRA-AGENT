# Run from Windows PowerShell: .\NIRA_IMAGE_HTTPS_CHECK.ps1
# No API key. Performs two real HTTPS GETs and reports only status/timings.
$ErrorActionPreference = "Continue"
$targets = @(
  'https://api.openverse.org/v1/images/?q=Great%20Pyramid%20of%20Giza&page_size=3',
  'https://commons.wikimedia.org/w/api.php?action=query&format=json&meta=siteinfo'
)
foreach ($url in $targets) {
  $hostName = ([uri]$url).Host
  Write-Host "`nHTTPS IPv4: $hostName"
  & curl.exe -4 -L -sS --max-time 18 --output NUL --write-out 'HTTP=%{http_code} TCP=%{time_connect}s TLS=%{time_appconnect}s TOTAL=%{time_total}s\n' $url
  Write-Host "curl exit code: $LASTEXITCODE"
}
Write-Host "`nThese results test real HTTPS responses, not only DNS or TCP port 443."
