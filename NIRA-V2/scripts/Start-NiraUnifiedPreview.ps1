param(
    [switch]$LegacyOnly,
    [switch]$AllowPersonalContext,
    [switch]$AllowLifecycleContext,
    [switch]$AllowGoalCreate,
    [switch]$AllowBranchCreate,
    [switch]$AllowLifecycleUpdate,
    [switch]$AllowLifecycleInspect,
    [switch]$AllowFileObservation,
    [switch]$AllowExplicitTextRead,
    [switch]$AllowDirectoryListing,
    [switch]$AllowFileHash,
    [switch]$AllowTextCreate,
    [switch]$AllowAppObservation,
    [switch]$AllowBrowserObservation,
    [switch]$AllowDocxInspection,
    [switch]$AllowDocxCreate,
    [switch]$AllowDesktopUiInspection
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
    'NIRA_V2_LIFECYCLE_CONTEXT', 'NIRA_V2_GOAL_CREATE', 'NIRA_V2_BRANCH_CREATE', 'NIRA_V2_LIFECYCLE_MUTATION', 'NIRA_V2_LIFECYCLE_INSPECT', 'NIRA_V2_FILE_OBSERVATION', 'NIRA_V2_PC_OBSERVATION', 'NIRA_V2_LEGACY_TEXT_READ', 'NIRA_V2_LEGACY_DIRECTORY_LIST', 'NIRA_V2_LEGACY_FILE_HASH', 'NIRA_V2_LEGACY_TEXT_CREATE',
    'NIRA_V2_G4_BROWSER', 'NIRA_V2_G4_DOCX_INSPECT', 'NIRA_V2_G4_DOCX_CREATE', 'NIRA_V2_G4_DESKTOP_UI')
$before = @{}
foreach ($name in $names) {
    $before[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
try {
    [Environment]::SetEnvironmentVariable('NIRA_AGENT_RUNTIME',
        $(if ($LegacyOnly) { 'LEGACY_ONLY' } else { 'V2_DEFAULT_READONLY' }), 'Process')
    [Environment]::SetEnvironmentVariable('NIRA_V2_PERSONAL_CONTEXT',
        $(if (-not $LegacyOnly -and $AllowPersonalContext) { 'ALLOW_READONLY_CONTEXT' } else { $null }), 'Process')
    [Environment]::SetEnvironmentVariable('NIRA_V2_LIFECYCLE_CONTEXT',
        $(if (-not $LegacyOnly -and $AllowPersonalContext -and $AllowLifecycleContext) { 'ALLOW_LIFECYCLE_SNAPSHOT' } else { $null }), 'Process')
    [Environment]::SetEnvironmentVariable('NIRA_V2_GOAL_CREATE',
        $(if (-not $LegacyOnly -and $AllowPersonalContext -and $AllowLifecycleContext -and $AllowGoalCreate) { 'ALLOW_EXPLICIT_GOAL_CREATE' } else { $null }), 'Process')
    [Environment]::SetEnvironmentVariable('NIRA_V2_BRANCH_CREATE',
        $(if (-not $LegacyOnly -and $AllowPersonalContext -and $AllowLifecycleContext -and $AllowBranchCreate) { 'ALLOW_EXPLICIT_BRANCH_CREATE' } else { $null }), 'Process')
    [Environment]::SetEnvironmentVariable('NIRA_V2_LIFECYCLE_MUTATION',
        $(if (-not $LegacyOnly -and $AllowPersonalContext -and $AllowLifecycleContext -and $AllowLifecycleUpdate) { 'ALLOW_EXPLICIT_LIFECYCLE_MUTATION' } else { $null }), 'Process')
    [Environment]::SetEnvironmentVariable('NIRA_V2_LIFECYCLE_INSPECT',
        $(if (-not $LegacyOnly -and $AllowPersonalContext -and $AllowLifecycleContext -and $AllowLifecycleInspect) { 'ALLOW_EXACT_LIFECYCLE_READBACK' } else { $null }), 'Process')
    [Environment]::SetEnvironmentVariable('NIRA_V2_FILE_OBSERVATION',
        $(if (-not $LegacyOnly -and $AllowFileObservation) { 'ALLOW_REQUESTED_PATH_METADATA' } else { $null }), 'Process')
    [Environment]::SetEnvironmentVariable('NIRA_V2_LEGACY_TEXT_READ',
        $(if (-not $LegacyOnly -and $AllowExplicitTextRead) { 'ALLOW_EXPLICIT_TEXT_READ' } else { $null }), 'Process')
    [Environment]::SetEnvironmentVariable('NIRA_V2_LEGACY_DIRECTORY_LIST',
        $(if (-not $LegacyOnly -and $AllowDirectoryListing) { 'ALLOW_EXPLICIT_DIRECTORY_LIST' } else { $null }), 'Process')
    [Environment]::SetEnvironmentVariable('NIRA_V2_LEGACY_FILE_HASH',
        $(if (-not $LegacyOnly -and $AllowFileHash) { 'ALLOW_EXPLICIT_FILE_HASH' } else { $null }), 'Process')
    [Environment]::SetEnvironmentVariable('NIRA_V2_LEGACY_TEXT_CREATE',
        $(if (-not $LegacyOnly -and $AllowTextCreate) { 'ALLOW_EXPLICIT_TEXT_CREATE' } else { $null }), 'Process')
    [Environment]::SetEnvironmentVariable('NIRA_V2_PC_OBSERVATION',
        $(if (-not $LegacyOnly -and $AllowAppObservation) { 'ALLOW_APP_NAMES' } else { $null }), 'Process')
    [Environment]::SetEnvironmentVariable('NIRA_V2_G4_BROWSER',
        $(if (-not $LegacyOnly -and $AllowBrowserObservation) { 'ALLOW_EXPLICIT_BROWSER_OBSERVATION' } else { $null }), 'Process')
    [Environment]::SetEnvironmentVariable('NIRA_V2_G4_DOCX_INSPECT',
        $(if (-not $LegacyOnly -and $AllowDocxInspection) { 'ALLOW_EXPLICIT_DOCX_INSPECTION' } else { $null }), 'Process')
    [Environment]::SetEnvironmentVariable('NIRA_V2_G4_DOCX_CREATE',
        $(if (-not $LegacyOnly -and $AllowDocxCreate) { 'ALLOW_EXPLICIT_DOCX_CREATE' } else { $null }), 'Process')
    [Environment]::SetEnvironmentVariable('NIRA_V2_G4_DESKTOP_UI',
        $(if (-not $LegacyOnly -and $AllowDesktopUiInspection) { 'ALLOW_EXPLICIT_DESKTOP_UI_INSPECTION' } else { $null }), 'Process')
    Write-Host ('[NIRA HOST] ' + $(if ($LegacyOnly) { 'Legacy-only safe mode' } else { 'V2 default read-only preview' }))
    Write-Host '[SECURITY] Read-only by default. Text-create requires -AllowTextCreate AND a fresh explicit owner approval.'
    if ($AllowLifecycleContext -and -not $LegacyOnly) {
        if (-not $AllowPersonalContext) {
            Write-Warning 'Lifecycle state requires BOTH -AllowPersonalContext and -AllowLifecycleContext. No lifecycle snapshot will be read.'
        } else {
            Write-Warning 'An exact bounded snapshot of your existing goals, work branches and active commitments may reach Ollama Cloud for lifecycle-related requests. Personal-state writes remain disabled unless -AllowGoalCreate or -AllowBranchCreate is also explicitly enabled.'
        }
    }
    if ($AllowBranchCreate -and -not $LegacyOnly) {
        Write-Warning 'An exact goal-linked branch can be created with current user text and an explicit fresh Allow Once approval. REQUIRED branch starts WAITING; no work is dispatched. Never choose Remember.'
    }
    if ($AllowLifecycleUpdate -and -not $LegacyOnly) {
        Write-Warning 'Only direct-user exact progress, Waiting and Blocked changes are enabled, with fresh Allow Once and SQLite readback. No activation or automatic work.'
    }
    if ($AllowLifecycleInspect -and -not $LegacyOnly) {
        Write-Warning 'An exact saved goal/branch ID may be inspected from existing SQLite and shared in bounded form with Ollama Cloud. No state changes.'
    }
    if ($AllowGoalCreate -and -not $LegacyOnly) {
        Write-Warning 'Persistent goal creation is restricted to the exact Create goal ... with success criterion ... syntax. The existing owner must approve Allow Once; goal starts WAITING, not executing.'
    }
    if ($AllowExplicitTextRead -and -not $LegacyOnly) {
        Write-Warning 'Text excerpts from the exact user-named file may be sent to Ollama Cloud after legacy NIRA authorization and audit.'
    }
    if ($AllowDirectoryListing -and -not $LegacyOnly) {
        Write-Warning 'Visible names of up to 30 immediate entries of one explicitly named directory may be sent to Ollama Cloud after NIRA authorization and audit.'
    }
    if ($AllowFileHash -and -not $LegacyOnly) {
        Write-Warning 'The SHA-256 digest and size of one explicitly named text file may be sent to Ollama Cloud after NIRA authorization. File contents are not sent by this tool.'
    }
    if ($AllowTextCreate -and -not $LegacyOnly) { Write-Warning 'NIRA may propose ONE exact new .txt file from the current user message. The owner approval toast MUST approve this exact path and content first. Never accept Remember for this profile.' }
    if ($AllowBrowserObservation -and -not $LegacyOnly) {
        Write-Warning 'Managed browser pages can load external HTTPS resources; bounded page text and tab metadata may reach Ollama Cloud. No browser clicks or form submissions are enabled by this G batch.'
    }
    if ($AllowDocxInspection -and -not $LegacyOnly) {
        Write-Warning 'A bounded preview of a user-named .docx document may reach Ollama Cloud. Keep sensitive documents out of this pilot.'
    }
    if ($AllowDocxCreate -and -not $LegacyOnly) {
        Write-Warning 'New .docx creation requires an exact current-turn path and quoted content, then owner Allow Once. Never approve without reviewing Details.'
    }
    if ($AllowDesktopUiInspection -and -not $LegacyOnly) {
        Write-Warning 'Windows UI Automation can expose application window/control text to Ollama Cloud. No desktop click or typing is enabled.'
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
