# NIRA R30.1 - no-model API diagnostic for the existing image discovery engine.
# Reads public API responses only. No Ollama, no NIRA UI, no file changes outside %TEMP%.
# Tests precisely the R30 Openverse and Commons queries and one approved image CDN.
$ErrorActionPreference = 'Stop'
$progressPreference = 'SilentlyContinue'
$curl = (Get-Command curl.exe -ErrorAction Stop).Source
$dir = Join-Path $env:TEMP ("nira-image-diag-" + [guid]::NewGuid().ToString('N'))
New-Item -Path $dir -ItemType Directory -Force | Out-Null

function Get-HostOf($url) {
  try { if ([string]::IsNullOrWhiteSpace($url)) { return '-' }; return ([uri]$url).Host } catch { return 'INVALID' }
}
function Get-Value($obj, $name) {
  if ($null -eq $obj) { return $null }
  $p = $obj.PSObject.Properties[$name]
  if ($null -eq $p) { return $null }
  return $p.Value
}
function Read-Api($name, $url, $timeSeconds) {
  $body = Join-Path $dir ($name + '.json')
  $hdr = Join-Path $dir ($name + '.headers')
  Write-Host "`n=== $name ==="
  $metrics = & $curl -4 --compressed --silent --show-error --max-time $timeSeconds --connect-timeout 4 --max-redirs 0 `
    -H 'Accept: application/json' -A 'NIRAAgent/1.0 (desktop Wikimedia Commons reference image client)' `
    --dump-header $hdr --output $body `
    --write-out 'HTTP=%{http_code} TCP=%{time_connect}s TLS=%{time_appconnect}s TOTAL=%{time_total}s' $url
  $code = $LASTEXITCODE
  Write-Host "Result: $metrics; curlExit=$code"
  if (!(Test-Path $body)) { Write-Host 'Body: none'; return $null }
  $raw = [IO.File]::ReadAllText($body)
  Write-Host "ResponseBytes: $([IO.File]::ReadAllBytes($body).Length)"
  if ($raw.Length -eq 0) { Write-Host 'Body: empty'; return $null }
  try { return $raw | ConvertFrom-Json -ErrorAction Stop }
  catch {
    $preview = ($raw -replace '[\r\n]+', ' ')
    if ($preview.Length -gt 170) { $preview = $preview.Substring(0,170) }
    Write-Host "JSON: INVALID; responseStartsWith=$preview"
    return $null
  }
}
function Show-ApiError($data) {
  if ($null -eq $data) { return }
  $apiError = Get-Value $data 'error'
  if ($null -ne $apiError) {
    Write-Host "API_ERROR: code=$(Get-Value $apiError 'code') message=$(Get-Value $apiError 'info')"
  }
  $warnings = Get-Value $data 'warnings'
  if ($null -ne $warnings) {
    $w = $warnings | ConvertTo-Json -Compress -Depth 5
    if ($w.Length -gt 400) { $w = $w.Substring(0, 400) }
    Write-Host "WARNINGS: $w"
  }
}
function Valid-Media($address) {
  try {
    $u = [uri]$address
    if ($u.Scheme -ne 'https' -or $u.Port -ne 443 -or $u.UserInfo -ne '' -or $u.Query -ne '' -or $u.Fragment -ne '') { return $false }
    if ($u.Host -eq 'upload.wikimedia.org' -and $u.AbsolutePath.StartsWith('/wikipedia/commons/')) { return $true }
    if ($u.Host -eq 'live.staticflickr.com' -and $u.AbsolutePath.EndsWith('.jpg')) { return $true }
  } catch { }
  return $false
}
try {
  Write-Host 'NIRA R30.1 IMAGE API DIAGNOSTIC'
  Write-Host 'LLM calls: 0 | Browser: not used | NIRA: not required'
  Write-Host 'Probes public API responses using IPv4 and the R30 User-Agent.'

  $openverse = 'https://api.openverse.org/v1/images/?q=Great%20Pyramid%20of%20Giza&page_size=18&mature=false'
  $data = Read-Api 'OPENVERSE' $openverse 8
  Show-ApiError $data
  $openResults = @(Get-Value $data 'results')
  if ($null -eq $data -or $null -eq (Get-Value $data 'results')) { $openResults = @() }
  Write-Host "OpenverseResults: $($openResults.Count)"
  $accepted = @()
  foreach ($item in $openResults) {
    $thumb = Get-Value $item 'thumbnail'
    $orig = Get-Value $item 'url'
    $media = if (Valid-Media $thumb) { $thumb } else { $orig }
    $source = Get-Value $item 'foreign_landing_url'
    $licensed = (Get-Value $item 'license') -in @('by','by-sa','by-nd','by-nc','by-nc-sa','by-nc-nd','cc0','pdm','publicdomain')
    if ((Valid-Media $media) -and $licensed -and (($source -like 'https://www.flickr.com/photos/*') -or ($source -like 'https://commons.wikimedia.org/wiki/File:*'))) { $accepted += $item }
  }
  Write-Host "OpenverseLikelyAccepted: $($accepted.Count) (approximate standalone filter)"
  if ($openResults.Count -gt 0) {
    foreach ($item in @($openResults | Select-Object -First 3)) {
      Write-Host "  Sample: thumbnailHost=$(Get-HostOf (Get-Value $item 'thumbnail')) sourceHost=$(Get-HostOf (Get-Value $item 'foreign_landing_url')) license=$(Get-Value $item 'license')"
    }
  }

  $commons = 'https://commons.wikimedia.org/w/api.php?action=query&format=json&formatversion=2&generator=search&gsrnamespace=6&gsrlimit=12&gsrsearch=Great%20Pyramid%20of%20Giza&prop=imageinfo&iiprop=url%7Cmime%7Csize%7Cextmetadata&iiurlwidth=1000&iiextmetadatalanguage=en&iiextmetadatafilter=Artist%7CLicenseShortName%7CLicenseUrl%7CImageDescription%7CCredit'
  $wikidata = Read-Api 'COMMONS_R30_EXACT' $commons 8
  Show-ApiError $wikidata
  $q = Get-Value $wikidata 'query'
  $pages = @()
  if ($null -ne $q -and $null -ne (Get-Value $q 'pages')) { $pages = @(Get-Value $q 'pages') }
  $withInfo = 0
  $withMedia = 0
  $mediaCandidate = $null
  foreach($p in $pages) {
    $infos = Get-Value $p 'imageinfo'
    if ($null -eq $infos) { continue }
    $withInfo++
    $info = @($infos)[0]
    $url = Get-Value $info 'thumburl'
    if ([string]::IsNullOrEmpty($url)) { $url = Get-Value $info 'url' }
    $source = Get-Value $info 'descriptionurl'
    if ((Valid-Media $url) -and $source -like 'https://commons.wikimedia.org/*' -and (Get-Value $info 'mime') -in @('image/jpeg','image/png','image/gif')) {
      $withMedia++
      if ($null -eq $mediaCandidate) { $mediaCandidate = $url }
    }
  }
  $queryKeys = if ($null -eq $wikidata) { 'none' } else { ($wikidata.PSObject.Properties.Name -join ',') }
  Write-Host "CommonsTopLevelKeys: $queryKeys"
  Write-Host "CommonsPages: $($pages.Count) | WithImageInfo: $withInfo | R30Valid: $withMedia"
  foreach($p in @($pages | Select-Object -First 2)) {
    $infos = Get-Value $p 'imageinfo'
    if ($null -eq $infos) { Write-Host '  Page sample: no imageinfo'; continue }
    $info = @($infos)[0]
    $uri = Get-Value $info 'thumburl'
    if (!$uri) { $uri = Get-Value $info 'url' }
    Write-Host "  Page sample: mime=$(Get-Value $info 'mime') mediaHost=$(Get-HostOf $uri) sourceHost=$(Get-HostOf (Get-Value $info 'descriptionurl'))"
  }

  if ($null -eq $mediaCandidate -and $accepted.Count -gt 0) {
    $mediaCandidate = Get-Value $accepted[0] 'thumbnail'
    if (!(Valid-Media $mediaCandidate)) { $mediaCandidate = Get-Value $accepted[0] 'url' }
  }
  if ($null -ne $mediaCandidate -and (Valid-Media $mediaCandidate)) {
    $imageFile = Join-Path $dir 'image-test.bin'
    $imageHeader = Join-Path $dir 'image-test.headers'
    Write-Host "`n=== APPROVED IMAGE DOWNLOAD ==="
    Write-Host "MediaHost: $(Get-HostOf $mediaCandidate)"
    $timing = & $curl -4 --silent --show-error --max-time 8 --connect-timeout 4 --max-redirs 0 `
      --max-filesize 3000000 -A 'NIRAAgent/1.0 (desktop Wikimedia Commons reference image client)' `
      --dump-header $imageHeader --output $imageFile --write-out 'HTTP=%{http_code} TLS=%{time_appconnect}s TOTAL=%{time_total}s' $mediaCandidate
    Write-Host "ImageResult: $timing; curlExit=$LASTEXITCODE"
    if (Test-Path $imageFile) {
      $bytes = [IO.File]::ReadAllBytes($imageFile)
      $valid = (($bytes.Length -ge 3 -and $bytes[0] -eq 255 -and $bytes[1] -eq 216 -and $bytes[2] -eq 255) -or
          ($bytes.Length -ge 8 -and $bytes[0] -eq 137 -and $bytes[1] -eq 80 -and $bytes[2] -eq 78) -or
          ($bytes.Length -ge 6 -and [Text.Encoding]::ASCII.GetString($bytes,0,3) -eq 'GIF'))
      Write-Host "SavedBytes: $($bytes.Length) | ValidImageSignature: $valid"
    }
  } else { Write-Host "`nImage download skipped: neither API supplied a permitted candidate." }
  Write-Host "`nDONE. No LLM calls. Share the console output."
} finally {
  Remove-Item -LiteralPath $dir -Force -Recurse -ErrorAction SilentlyContinue
}
