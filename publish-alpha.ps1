[CmdletBinding()]
param([switch]$SkipZip)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = $PSScriptRoot
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '.NET 10 SDK is required.' }
if (-not [Environment]::Is64BitOperatingSystem) { throw 'This Alpha candidate targets Windows x64.' }
$parent = Join-Path $root 'artifacts'
New-Item -ItemType Directory -Path $parent -Force | Out-Null
if ((Get-Item -LiteralPath $parent).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Refusing linked artifacts directory.' }
$name = 'NIRA-V2-Alpha-win-x64-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8)
$stage = Join-Path $parent ('.nira-publish-' + [guid]::NewGuid().ToString('N'))
$final = Join-Path $parent $name
$zip = "$final.zip"
$zipTemp = "$stage.zip"
$published = $false
try {
    Write-Progress -Activity 'NIRA Alpha release candidate' -Status 'Rebuild and kernel regression tests' -PercentComplete 10
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'build.ps1') -SkipExport
    if ($LASTEXITCODE -ne 0) { throw 'Debug build or regression acceptance failed.' }
    New-Item -ItemType Directory -Path $stage | Out-Null
    foreach ($entry in @(@{Project='src/Nira.Desktop/Nira.Desktop.csproj'; Folder='Desktop'; Percent=35}, @{Project='src/Nira.Cli/Nira.Cli.csproj'; Folder='CLI'; Percent=60})) {
        Write-Progress -Activity 'NIRA Alpha release candidate' -Status ('Publish ' + $entry.Folder) -PercentComplete $entry.Percent
        $dest = Join-Path $stage $entry.Folder
        & dotnet publish (Join-Path $root $entry.Project) -c Release -r win-x64 --self-contained false -o $dest --nologo
        if ($LASTEXITCODE -ne 0) { throw ('Publish failed: ' + $entry.Project) }
    }
    foreach ($relative in @('Desktop/NIRA.exe','Desktop/NIRA.dll','Desktop/NIRA.runtimeconfig.json','CLI/Nira.Cli.exe','CLI/Nira.Cli.dll','CLI/Nira.Cli.runtimeconfig.json')) {
        if (-not (Test-Path -LiteralPath (Join-Path $stage $relative) -PathType Leaf)) { throw "Published application missing: $relative" }
    }
    $assetSource = Join-Path $root 'src/Nira.Desktop/Assets'
    foreach ($asset in @(Get-ChildItem -LiteralPath $assetSource -File)) {
        $dest = Join-Path (Join-Path $stage 'Desktop/Assets') $asset.Name
        if (-not (Test-Path -LiteralPath $dest -PathType Leaf)) { throw "Published artwork missing: $($asset.Name)" }
        if ((Get-FileHash -LiteralPath $dest).Hash -ne (Get-FileHash -LiteralPath $asset.FullName).Hash) { throw "Published artwork mismatch: $($asset.Name)" }
    }
    # App data is never staged. Publish outputs only; also reject accidental private data files.
    foreach ($file in @(Get-ChildItem -LiteralPath $stage -Recurse -Force -File)) {
        if ($file.Extension -in @('.db','.db-wal','.db-shm','.dpapi','.pfx','.log') -or $file.Name -match '^(\.env($|\.)|credentials\.json$|secrets\.json$|settings\.local\.json$)') { throw "Private/runtime file in publish output: $($file.Name)" }
    }
    @'
$ErrorActionPreference = 'Stop'
if (-not $env:NIRA_VISION_ENDPOINT -and -not $env:NIRA_VISION_MODEL) {
    $env:NIRA_VISION_ENDPOINT = 'https://ollama.com/v1/'
    $env:NIRA_VISION_MODEL = 'gemma4:cloud'
}
& (Join-Path $PSScriptRoot 'Desktop/NIRA.exe')
'@ | Set-Content -LiteralPath (Join-Path $stage 'Start-NIRA.ps1') -Encoding UTF8
    @'
$ErrorActionPreference = 'Stop'
if (-not $env:NIRA_VISION_ENDPOINT -and -not $env:NIRA_VISION_MODEL) {
    $env:NIRA_VISION_ENDPOINT = 'https://ollama.com/v1/'
    $env:NIRA_VISION_MODEL = 'gemma4:cloud'
}
& (Join-Path $PSScriptRoot 'CLI/Nira.Cli.exe')
if ($LASTEXITCODE -ne 0) { throw 'NIRA CLI exited with an error.' }
'@ | Set-Content -LiteralPath (Join-Path $stage 'Start-CLI.ps1') -Encoding UTF8
    @'
NIRA V2 Alpha — local release candidate
Requires Windows x64 and .NET 10 Desktop Runtime (x64). Model access requires a configured provider and valid credentials. Local OCR uses the existing per-user OCR runtime or supported Windows OCR fallback; browser tools require Microsoft Edge. The package does not include these external dependencies or private data. Start-NIRA.ps1 and Start-CLI.ps1 use the current Windows user's existing NIRA-Clean application data and workspace. Do not run the development app and candidate simultaneously against the same data. Build acceptance is not live UI acceptance; perform a real smoke test before treating the candidate as accepted.
'@ | Set-Content -LiteralPath (Join-Path $stage 'RELEASE-NOTES.txt') -Encoding UTF8
    Write-Progress -Activity 'NIRA Alpha release candidate' -Status 'Hashing published files' -PercentComplete 85
    $prefix = $stage.Length + 1
    $files = @(Get-ChildItem -LiteralPath $stage -Recurse -Force -File | Sort-Object FullName | ForEach-Object {
        [PSCustomObject]@{ path=$_.FullName.Substring($prefix).Replace('\','/'); bytes=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    [PSCustomObject]@{ release=$name; framework='net10.0-windows'; runtime='win-x64'; selfContained=$false; liveAcceptance='pending'; files=$files } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $stage 'RELEASE-MANIFEST.json') -Encoding UTF8
    if (-not $SkipZip) {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [IO.Compression.ZipFile]::CreateFromDirectory($stage, $zipTemp, [IO.Compression.CompressionLevel]::Optimal, $false)
        $archive = [IO.Compression.ZipFile]::OpenRead($zipTemp)
        try {
            foreach ($file in $files) {
                $entry = $archive.GetEntry($file.path)
                if ($null -eq $entry) { $entry = $archive.GetEntry($file.path.Replace('/','\')) }
                if ($null -eq $entry -or $entry.Length -ne $file.bytes) { throw "Archive entry mismatch: $($file.path)" }
                $stream = $entry.Open()
                $hasher = [Security.Cryptography.SHA256]::Create()
                try { $digest = [BitConverter]::ToString($hasher.ComputeHash($stream)).Replace('-','').ToLowerInvariant() }
                finally { $stream.Dispose(); $hasher.Dispose() }
                if ($digest -ne $file.sha256) { throw "Archive hash mismatch: $($file.path)" }
            }
        } finally { $archive.Dispose() }
    }
    Move-Item -LiteralPath $stage -Destination $final
    if (-not $SkipZip) { Move-Item -LiteralPath $zipTemp -Destination $zip }
    $published = $true
    Write-Host "RELEASE CANDIDATE READY: $final" -ForegroundColor Green
    if (-not $SkipZip) { Write-Host "ZIP: $zip"; Write-Host ('SHA256: ' + (Get-FileHash -LiteralPath $zip).Hash.ToLowerInvariant()) }
    Write-Host 'Live acceptance pending. Close development NIRA, then start the published candidate.' -ForegroundColor Yellow
} finally {
    Write-Progress -Activity 'NIRA Alpha release candidate' -Completed
    if (-not $published) {
        if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
        if (Test-Path -LiteralPath $zipTemp) { Remove-Item -LiteralPath $zipTemp -Force }
    }
}
