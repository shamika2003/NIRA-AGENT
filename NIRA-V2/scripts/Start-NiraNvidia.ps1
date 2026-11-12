[CmdletBinding()]
param(
    [string]$Model = 'nvidia/nemotron-3.5-lightning-30b-a3b',
    [switch]$SessionOnlyKey,
    [switch]$ForgetSavedKey,
    [switch]$AllowPersonalContext,
    [switch]$AllowLifecycleContext,
    [switch]$AllowGoalCreate,
    [switch]$AllowBranchCreate,
    [switch]$AllowLifecycleUpdate,
    [switch]$AllowLifecycleInspect,
    [switch]$AllowBranchCompletion,
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
$launcher = Join-Path $PSScriptRoot 'Start-NiraUnifiedPreview.ps1'
if (-not (Test-Path -LiteralPath $launcher -PathType Leaf)) {
    throw 'Original NIRA V2 unified launcher is missing.'
}
if ($Model -notmatch '^[A-Za-z0-9][A-Za-z0-9._/-]{0,159}$') {
    throw 'Invalid NVIDIA model identifier.'
}
# Default: remember the credential only as a CurrentUser DPAPI-protected
# SecureString blob OUTSIDE the project. -SessionOnlyKey never writes it.
$secretFolder = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'NIRA\Secrets'
$secretPath = Join-Path $secretFolder 'nvidia-cloud-key.dpapi'
if ($ForgetSavedKey) {
    if (Test-Path -LiteralPath $secretPath -PathType Leaf) {
        Remove-Item -LiteralPath $secretPath -Force
    }
    Write-Host '[NVIDIA] Saved credential removed. No project or NIRA state was changed.'
    return
}
function Convert-NiraSecretToPlainText([Security.SecureString]$secret) {
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}
$oldProvider = [Environment]::GetEnvironmentVariable('NIRA_V2_PROVIDER','Process')
$oldModel = [Environment]::GetEnvironmentVariable('NIRA_V2_MODEL','Process')
$oldKey = [Environment]::GetEnvironmentVariable('NIRA_NVIDIA_API_KEY','Process')
try {
    $key = [Environment]::GetEnvironmentVariable('NIRA_NVIDIA_API_KEY','Process')
    if ([string]::IsNullOrWhiteSpace($key)) {
        $key = [Environment]::GetEnvironmentVariable('NVIDIA_API_KEY','Process')
    }
    if ([string]::IsNullOrWhiteSpace($key) -and
        (Test-Path -LiteralPath $secretPath -PathType Leaf)) {
        try {
            $stored = (Get-Content -LiteralPath $secretPath -Raw -Encoding UTF8).Trim()
            $cached = ConvertTo-SecureString -String $stored -ErrorAction Stop
            try { $key = Convert-NiraSecretToPlainText $cached }
            finally { $cached.Dispose() }
            if ($key.Length -lt 16 -or $key -match '\s') {
                $key = $null
                throw 'Invalid cached credential.'
            }
            Write-Host '[NVIDIA] Reused encrypted key for this Windows user.'
        }
        catch {
            $key = $null
            Write-Warning 'Saved NVIDIA credential cannot be decrypted on this Windows profile; re-entry required.'
        }
    }
    if ([string]::IsNullOrWhiteSpace($key)) {
        $secret = Read-Host 'NVIDIA API key (hidden)' -AsSecureString
        try {
            $key = Convert-NiraSecretToPlainText $secret
            if ([string]::IsNullOrWhiteSpace($key) -or
                $key.Trim().Length -lt 16 -or $key -match '\s') {
                throw 'NVIDIA API credential format is invalid.'
            }
            if (-not $SessionOnlyKey) {
                New-Item -ItemType Directory -Path $secretFolder -Force | Out-Null
                $ciphertext = $secret | ConvertFrom-SecureString
                $temp = Join-Path $secretFolder ('nvidia-key-' + [guid]::NewGuid().ToString('N') + '.tmp')
                try {
                    [IO.File]::WriteAllText($temp, $ciphertext, [Text.UTF8Encoding]::new($false))
                    # This file is DPAPI encrypted, not a plain key. If interrupted,
                    # NIRA will ask for the credential again on the next launch.
                    Move-Item -LiteralPath $temp -Destination $secretPath -Force
                }
                finally { if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force } }
                Write-Host '[NVIDIA] Key saved encrypted for this Windows user (DPAPI).'
            }
        }
        finally { $secret.Dispose() }
    }
    if ([string]::IsNullOrWhiteSpace($key)) { throw 'NVIDIA API key is empty.' }
    [Environment]::SetEnvironmentVariable('NIRA_NVIDIA_API_KEY',$key.Trim(),'Process')
    [Environment]::SetEnvironmentVariable('NIRA_V2_PROVIDER','NVIDIA','Process')
    [Environment]::SetEnvironmentVariable('NIRA_V2_MODEL',$Model,'Process')
    $key = $null
    $forward = @{}
    foreach ($name in @('AllowPersonalContext','AllowLifecycleContext','AllowGoalCreate',
        'AllowBranchCreate','AllowLifecycleUpdate','AllowLifecycleInspect',
        'AllowBranchCompletion','AllowFileObservation','AllowExplicitTextRead',
        'AllowDirectoryListing','AllowFileHash','AllowTextCreate',
        'AllowAppObservation','AllowBrowserObservation','AllowDocxInspection',
        'AllowDocxCreate','AllowDesktopUiInspection')) {
        if ($PSBoundParameters.ContainsKey($name)) { $forward[$name] = $PSBoundParameters[$name] }
    }
    Write-Host ('[NIRA V2] NVIDIA NIM provider | Model: ' + $Model)
    Write-Host '[NIRA V2] Single identity and V2 host permissions unchanged.'
    & $launcher @forward
    if ($LASTEXITCODE -ne 0) { throw ('NIRA desktop exited with code ' + $LASTEXITCODE) }
}
finally {
    [Environment]::SetEnvironmentVariable('NIRA_V2_PROVIDER',$oldProvider,'Process')
    [Environment]::SetEnvironmentVariable('NIRA_V2_MODEL',$oldModel,'Process')
    [Environment]::SetEnvironmentVariable('NIRA_NVIDIA_API_KEY',$oldKey,'Process')
}
