# NIRA Image Step 0: independent LIVE network/image-byte diagnostic.
# No NIRA executable, LLM, API keys, source edits or project build is involved.
# Windows PowerShell 5.1 and PowerShell 7 compatible.
param(
    [string]$Query = 'Hubble Space Telescope'
)

$ErrorActionPreference = 'Stop'
$root = Join-Path (Get-Location).Path 'NIRA_IMAGE_STEP0_RESULTS'
New-Item -ItemType Directory -Path $root -Force | Out-Null
$reportPath = Join-Path $root 'RESULTS.txt'
$lines = New-Object 'System.Collections.Generic.List[string]'
$script:validImages = 0
$script:apiGood = 0
$script:attemptedMedia = 0
$script:successKinds = @()

function Log([string]$text) {
    Write-Host $text
    $lines.Add($text)
}
function Check-HttpsMedia([string]$value) {
    try {
        $u = [Uri]$value
        if (-not $u.IsAbsoluteUri -or $u.Scheme -ne 'https' -or $u.UserInfo -or $u.Port -ne 443) { return $false }
        return @('api.openverse.org','live.staticflickr.com','upload.wikimedia.org','thumb.wikimedia.org') -contains $u.DnsSafeHost.ToLowerInvariant()
    } catch { return $false }
}
function Curl-Get([string]$label,[string]$url,[string]$destination,[int]$maxBytes) {
    Remove-Item -LiteralPath $destination -Force -ErrorAction SilentlyContinue
    # An explicit IPv4 HTTP/1.1 probe, intentionally outside NIRA's .NET transport.
    # No redirect following: the diagnostic must not silently contact a new host.
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $result = & curl.exe -4 --http1.1 --silent --show-error --connect-timeout 6 --max-time 18 --retry 0 --max-filesize $maxBytes `
            --user-agent 'NIRAImageStep0/1.0 (owner-run image connectivity test)' `
            --output $destination --write-out 'HTTP=%{http_code} TIME=%{time_total}s IP=%{remote_ip}' `
            $url 2>&1 | Out-String
        $exitCode = $LASTEXITCODE
    } finally { $ErrorActionPreference = $old }
    $short = ($result -replace '[\r\n]+', ' ').Trim()
    if ($short.Length -gt 300) { $short = $short.Substring(0,300) }
    $size = 0
    if (Test-Path -LiteralPath $destination) { $size = (Get-Item -LiteralPath $destination).Length }
    Log "[$label] CURL_EXIT=$exitCode BYTES=$size $short"
    return [pscustomobject]@{ OK=($exitCode -eq 0 -and $size -gt 0 -and $short -match 'HTTP=200'); Path=$destination; Bytes=$size }
}
function Test-Magic([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return '' }
    $file = [System.IO.File]::OpenRead($path)
    try {
        if ($file.Length -lt 64) { return '' }
        $bytes = New-Object byte[] 8
        [void]$file.Read($bytes,0,8)
        if ($bytes[0] -eq 0xFF -and $bytes[1] -eq 0xD8 -and $bytes[2] -eq 0xFF) { return 'JPEG' }
        if ($bytes[0] -eq 137 -and $bytes[1] -eq 80 -and $bytes[2] -eq 78 -and $bytes[3] -eq 71 -and
            $bytes[4] -eq 13 -and $bytes[5] -eq 10 -and $bytes[6] -eq 26 -and $bytes[7] -eq 10) { return 'PNG' }
        if ($bytes[0] -eq 71 -and $bytes[1] -eq 73 -and $bytes[2] -eq 70 -and $bytes[3] -eq 56) { return 'GIF' }
        return ''
    } finally { $file.Dispose() }
}
function Try-Media([string]$label,[string]$url) {
    if (-not (Check-HttpsMedia $url)) { Log "[$label] SKIPPED: URL not on test-media allowlist"; return $false }
    $script:attemptedMedia++
    $target = Join-Path $root ("image_attempt_{0}.bin" -f $script:attemptedMedia)
    $outcome = Curl-Get $label $url $target 8000000
    if (-not $outcome.OK) { Remove-Item -LiteralPath $target -Force -ErrorAction SilentlyContinue; return $false }
    $kind = Test-Magic $target
    if ($kind -eq '') { Log "[$label] INVALID_IMAGE: downloaded bytes are not a supported image"; return $false }
    # Prove the image can actually be decoded as pixels, not just a header.
    $picture = $null
    try {
        Add-Type -AssemblyName System.Drawing -ErrorAction Stop
        $picture = [System.Drawing.Image]::FromFile($target)
        if ($picture.Width -lt 16 -or $picture.Height -lt 16) {
            Log "[$label] INVALID_IMAGE: unexpected image dimensions"
            return $false
        }
        Log "[$label] PIXELS=$($picture.Width)x$($picture.Height) DECODE=OK"
    } catch {
        Log "[$label] INVALID_IMAGE: image decode failed: $($_.Exception.Message)"
        return $false
    } finally {
        if ($null -ne $picture) { $picture.Dispose() }
    }
    $extension = switch ($kind) { 'JPEG' { 'jpg' } 'PNG' { 'png' } 'GIF' { 'gif' } }
    $final = Join-Path $root ("verified_photo_{0}.{1}" -f ($script:validImages + 1), $extension)
    Move-Item -LiteralPath $target -Destination $final -Force
    $script:validImages++
    $script:successKinds += $label
    Log "[$label] VERIFIED_IMAGE=$kind SAVED=$final"
    return $true
}

Log 'NIRA STEP 0 - NO LLM / NO NIRA / NO SOURCE CHANGES'
Log "Query=$Query"
Log "Output=$root"
Log "Started=$(Get-Date -Format o)"
Log "ProxyEnvironmentPresent=$( [bool]($env:HTTPS_PROXY -or $env:HTTP_PROXY -or $env:ALL_PROXY) ) (no proxy values logged)"
if (-not (Get-Command curl.exe -ErrorAction SilentlyContinue)) {
    Log 'ERROR: curl.exe is missing. Windows 11 normally includes curl.exe.'
    $lines | Set-Content -LiteralPath $reportPath -Encoding UTF8
    exit 2
}
$escapedQuery = [Uri]::EscapeDataString($Query)
$openverseUrl = 'https://api.openverse.org/v1/images/?q=' + $escapedQuery + '&page_size=5&mature=false'
$commonsUrl = 'https://commons.wikimedia.org/w/api.php?action=query&format=json&formatversion=2&generator=search&gsrnamespace=6&gsrlimit=10&gsrsearch=' + $escapedQuery + '&prop=imageinfo&iiprop=url%7Cmime%7Csize%7Cextmetadata&iiurlwidth=800'

# A: Check whether independent PUBLIC image-search APIs return real JSON.
$openverseFile = Join-Path $root 'openverse_response.json'
$commonsFile = Join-Path $root 'commons_response.json'
$openverse = Curl-Get 'API OPENVERSE' $openverseUrl $openverseFile 1200000
$commons = Curl-Get 'API COMMONS' $commonsUrl $commonsFile 1200000
$openverseResults = @()
$commonsPages = @()
if ($openverse.OK) {
    try {
        $data = Get-Content -LiteralPath $openverseFile -Raw -Encoding UTF8 | ConvertFrom-Json
        $openverseResults = @($data.results | Where-Object { $null -ne $_ })
        Log "[API OPENVERSE] VALID_JSON=YES RESULTS=$($openverseResults.Count)"
        $script:apiGood++
    } catch { Log "[API OPENVERSE] INVALID_JSON: $($_.Exception.Message)" }
}
if ($commons.OK) {
    try {
        $data = Get-Content -LiteralPath $commonsFile -Raw -Encoding UTF8 | ConvertFrom-Json
        $commonsPages = @($data.query.pages | Where-Object { $null -ne $_ })
        Log "[API COMMONS] VALID_JSON=YES PAGES=$($commonsPages.Count)"
        $script:apiGood++
    } catch { Log "[API COMMONS] INVALID_JSON: $($_.Exception.Message)" }
}

# B: Try REAL media bytes. Prefer separate sources. No pre-set pictures or invented URLs.
foreach ($item in $openverseResults) {
    if ($script:validImages -ge 1) { break }
    $uuid = [Guid]::Empty
    if ([Guid]::TryParse([string]$item.id, [ref]$uuid) -and $uuid -ne [Guid]::Empty) {
        $proxyImage = 'https://api.openverse.org/v1/images/' + $uuid.ToString('D') + '/thumb/'
        if (Try-Media 'IMAGE OPENVERSE-THUMB' $proxyImage) { break }
    }
    if ($item.thumbnail -and (Try-Media 'IMAGE OPENVERSE-ORIGINAL-THUMB' ([string]$item.thumbnail))) { break }
    if ($item.url -and (Try-Media 'IMAGE OPENVERSE-ORIGINAL' ([string]$item.url))) { break }
}
foreach ($page in $commonsPages) {
    if ($script:validImages -ge 2 -and $script:successKinds -match 'COMMONS') { break }
    $info = @($page.imageinfo | Where-Object { $null -ne $_ } | Select-Object -First 1)
    if ($info.Count -eq 0) { continue }
    if ($info[0].thumburl -and (Try-Media 'IMAGE COMMONS-THUMB' ([string]$info[0].thumburl))) { break }
    if ($info[0].url -and (Try-Media 'IMAGE COMMONS-ORIGINAL' ([string]$info[0].url))) { break }
}

Log "STEP0_SEARCH_APIS_OK=$script:apiGood/2"
Log "STEP0_VERIFIED_IMAGES=$script:validImages"
if ($script:validImages -gt 0) {
    Log 'STEP0_RESULT=PASS: Real image pixels downloaded outside NIRA/LLM. Next diagnose NIRA .NET image transport.'
} elseif ($script:apiGood -gt 0) {
    Log 'STEP0_RESULT=PARTIAL: Image search JSON works, but NO valid image file downloaded. Investigate media-host connectivity.'
} else {
    Log 'STEP0_RESULT=FAIL: Neither public search API delivered parseable JSON. Investigate DNS/proxy/TLS/firewall/provider access.'
}
Log "Finished=$(Get-Date -Format o)"
$lines | Set-Content -LiteralPath $reportPath -Encoding UTF8
Log "REPORT_FILE=$reportPath"
