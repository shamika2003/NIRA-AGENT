param([Parameter(Mandatory=$true)][string]$LegacyProjectRoot)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $LegacyProjectRoot).Path
if (-not (Test-Path (Join-Path $root 'NIRAAgent/NIRAAgent.csproj'))) {
    throw 'Not a NIRA-AGENT legacy source root.'
}
Write-Host '[READ-ONLY] Legacy NIRA inventory:'
Get-Item (Join-Path $root 'NIRAAgent/Mind/NIRAExecutive.cs'),
         (Join-Path $root 'NIRAAgent/AI/Cognition/NIRACognitionService.cs'),
         (Join-Path $root 'NIRAAgent/Character/State/NIRACharacterStateService.cs') |
    Select-Object Name, Length, LastWriteTime | Format-Table
Write-Host '[READ-ONLY] Git status (no changes performed):'
git -C $root status --short
Write-Host 'Do not delete legacy files or rewrite persistent stores during foundation work.'
