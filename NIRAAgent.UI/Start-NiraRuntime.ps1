[CmdletBinding()]
param(
    [switch]$AllowPersonalContext,
    [switch]$AllowLifecycleContext
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$ui = Join-Path $PSScriptRoot 'NIRAAgent.UI.csproj'
if (-not (Test-Path -LiteralPath $ui -PathType Leaf)) {
    throw 'NIRA desktop project is missing.'
}
if ($null -eq (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET 10 SDK is not available.'
}
$names = @('NIRA_PERSONAL_CONTEXT','NIRA_LIFECYCLE_CONTEXT')
$before = @{}
foreach ($name in $names) {
    $before[$name] = [Environment]::GetEnvironmentVariable($name,'Process')
}
try {
    [Environment]::SetEnvironmentVariable('NIRA_PERSONAL_CONTEXT',
        $(if ($AllowPersonalContext) { 'ALLOW_READONLY_CONTEXT' } else { $null }),'Process')
    [Environment]::SetEnvironmentVariable('NIRA_LIFECYCLE_CONTEXT',
        $(if ($AllowPersonalContext -and $AllowLifecycleContext) {
            'ALLOW_LIFECYCLE_SNAPSHOT'
        } else { $null }),'Process')
    if ($AllowLifecycleContext -and -not $AllowPersonalContext) {
        Write-Warning 'Lifecycle context requires personal-context consent.'
    }
    Write-Host '[NIRA] Starting single agent runtime.'
    Write-Host '[SECURITY] Actions use the existing NIRA capability permission and audit service.'
    if ($AllowPersonalContext) {
        Write-Warning 'Personal context can be shared with the selected cloud model. Sensitive data should be handled with care.'
    }
    Push-Location $project
    try {
        & dotnet run --project $ui -c Debug
        if ($LASTEXITCODE -ne 0) { throw ('NIRA desktop exited with code ' + $LASTEXITCODE) }
    } finally { Pop-Location }
} finally {
    foreach ($name in $names) {
        [Environment]::SetEnvironmentVariable($name,$before[$name],'Process')
    }
}
