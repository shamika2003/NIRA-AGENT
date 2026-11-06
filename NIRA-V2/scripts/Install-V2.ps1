param([Parameter(Mandatory=$true)][string]$LegacyProjectRoot,
      [switch]$RunTests)
$ErrorActionPreference = 'Stop'
$legacy = (Resolve-Path -LiteralPath $LegacyProjectRoot).Path
if (-not (Test-Path -LiteralPath (Join-Path $legacy 'NIRAAgent/NIRAAgent.csproj')) -or
    -not (Test-Path -LiteralPath (Join-Path $legacy 'NIRAAgent.UI/NIRAAgent.UI.csproj'))) {
    throw 'The supplied path is not a NIRA-AGENT source project root.'
}
$src = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$dst = Join-Path $legacy 'NIRA-V2'
if (Test-Path -LiteralPath $dst) {
    throw 'NIRA-V2 already exists. Refusing to overwrite work. No files changed.'
}
Write-Host '[NIRA V2] Installing as a NEW folder only.'
try {
    Copy-Item -LiteralPath $src -Destination $dst -Recurse -ErrorAction Stop
} catch {
    if (Test-Path -LiteralPath $dst) { Remove-Item -LiteralPath $dst -Recurse -Force }
    throw
}
Write-Host ('[CREATED] ' + $dst)
Write-Host '[PROTECTED] Existing NIRAAgent, NIRAAgent.UI, identity, memory and bridge files untouched.'
if ($RunTests) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $dst 'scripts/Verify-Foundation.ps1')
    if ($LASTEXITCODE -ne 0) {
        Write-Warning 'V2 test gate failed. Existing NIRA was NOT changed. Review the new NIRA-V2 folder.'
        exit $LASTEXITCODE
    }
}
Write-Host '[NEXT] Work in NIRA-V2. Do not delete legacy NIRA until parity gates are passed.'
