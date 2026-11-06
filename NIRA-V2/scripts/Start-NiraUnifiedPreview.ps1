param(
    [switch]$LegacyOnly,
    [switch]$AllowPersonalContext,
    [switch]$AllowFileObservation,
    [switch]$AllowExplicitTextRead,
    [switch]$AllowDirectoryListing,
    [switch]$AllowAppObservation
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$ui = Join-Path $project 'NIRAAgent.UI\NIRAAgent.UI.csproj'
if (-not (Test-Path -LiteralPath $ui -PathType Leaf)) {
    throw 'Run this script from its installed NIRA-V2/scripts location.'
}
if ($null -eq (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET 10 SDK is not available.'
}
# Scope consents to this process. Never persist changes with setx or log credentials.
$names = @('NIRA_AGENT_RUNTIME', 'NIRA_V2_PERSONAL_CONTEXT',
    'NIRA_V2_FILE_OBSERVATION', 'NIRA_V2_PC_OBSERVATION', 'NIRA_V2_LEGACY_TEXT_READ', 'NIRA_V2_LEGACY_DIRECTORY_LIST')
$before = @{}
foreach ($name in $names) {
    $before[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
try {
    [Environment]::SetEnvironmentVariable('NIRA_AGENT_RUNTIME',
        $(if ($LegacyOnly) { 'LEGACY_ONLY' } else { 'V2_DEFAULT_READONLY' }), 'Process')
    [Environment]::SetEnvironmentVariable('NIRA_V2_PERSONAL_CONTEXT',
        $(if (-not $LegacyOnly -and $AllowPersonalContext) { 'ALLOW_READONLY_CONTEXT' } else { $null }), 'Process')
    [Environment]::SetEnvironmentVariable('NIRA_V2_FILE_OBSERVATION',
        $(if (-not $LegacyOnly -and $AllowFileObservation) { 'ALLOW_REQUESTED_PATH_METADATA' } else { $null }), 'Process')
    [Environment]::SetEnvironmentVariable('NIRA_V2_LEGACY_TEXT_READ',
        $(if (-not $LegacyOnly -and $AllowExplicitTextRead) { 'ALLOW_EXPLICIT_TEXT_READ' } else { $null }), 'Process')
    [Environment]::SetEnvironmentVariable('NIRA_V2_LEGACY_DIRECTORY_LIST',
        $(if (-not $LegacyOnly -and $AllowDirectoryListing) { 'ALLOW_EXPLICIT_DIRECTORY_LIST' } else { $null }), 'Process')
    [Environment]::SetEnvironmentVariable('NIRA_V2_PC_OBSERVATION',
        $(if (-not $LegacyOnly -and $AllowAppObservation) { 'ALLOW_APP_NAMES' } else { $null }), 'Process')
    Write-Host ('[NIRA HOST] ' + $(if ($LegacyOnly) { 'Legacy-only safe mode' } else { 'V2 default read-only preview' }))
    Write-Host '[SECURITY] No PC writes are enabled by this runtime selection.'
    if ($AllowExplicitTextRead -and -not $LegacyOnly) {
        Write-Warning 'Text excerpts from the exact user-named file may be sent to Ollama Cloud after legacy NIRA authorization and audit.'
    }
    if ($AllowDirectoryListing -and -not $LegacyOnly) {
        Write-Warning 'Visible names of up to 30 immediate entries of one explicitly named directory may be sent to Ollama Cloud after NIRA authorization and audit.'
    }
    Write-Host '[CLOUD] The general model sees submitted requests and any independently consented observations.'
    Push-Location $project
    try {
        & dotnet run --project $ui -c Debug
        if ($LASTEXITCODE -ne 0) { throw ('WPF exited with code ' + $LASTEXITCODE) }
    } finally { Pop-Location }
} finally {
    foreach ($name in $names) {
        [Environment]::SetEnvironmentVariable($name, $before[$name], 'Process')
    }
}
