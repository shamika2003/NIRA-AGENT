[CmdletBinding()]
param(
    [string]$Model = 'gemma4:31b',
    [switch]$SessionOnlyKey,
    [switch]$ForgetSavedKey,
    [switch]$AllowPersonalContext,
    [switch]$AllowLifecycleContext,
    [switch]$CaptureAgentDecisions
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$launcher = Join-Path $PSScriptRoot 'Start-NiraRuntime.ps1'
if (-not (Test-Path -LiteralPath $launcher -PathType Leaf)) {
    throw 'NIRA runtime launcher is missing.'
}
if ($Model -notmatch '^[A-Za-z0-9][A-Za-z0-9._/:-]{0,159}$') {
    throw 'Invalid OLLAMA model identifier.'
}
# Default: remember the credential only as a CurrentUser DPAPI-protected
# SecureString blob OUTSIDE the project. -SessionOnlyKey never writes it.
$secretFolder = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'NIRA\Secrets'
$secretPath = Join-Path $secretFolder 'ollama-cloud-key.dpapi'
if ($ForgetSavedKey) {
    if (Test-Path -LiteralPath $secretPath -PathType Leaf) {
        Remove-Item -LiteralPath $secretPath -Force
    }
    Write-Host '[OLLAMA] Saved credential removed. No project or NIRA state was changed.'
    return
}
function Convert-NiraSecretToPlainText([Security.SecureString]$secret) {
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}
$oldProvider = [Environment]::GetEnvironmentVariable('NIRA_MODEL_PROVIDER','Process')
$oldModel = [Environment]::GetEnvironmentVariable('NIRA_MODEL_NAME','Process')
$oldKey = [Environment]::GetEnvironmentVariable('NIRA_OLLAMA_API_KEY','Process')
$oldDecisionCapture = [Environment]::GetEnvironmentVariable('NIRA_DEBUG_AGENT_DECISIONS','Process')
$oldPublicCapture = [Environment]::GetEnvironmentVariable('NIRA_DEBUG_PUBLIC_RESPONSES','Process')
try {
    $key = [Environment]::GetEnvironmentVariable('NIRA_OLLAMA_API_KEY','Process')
    if ([string]::IsNullOrWhiteSpace($key)) {
        $key = [Environment]::GetEnvironmentVariable('OLLAMA_API_KEY','Process')
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
            Write-Host '[OLLAMA] Reused encrypted key for this Windows user.'
        }
        catch {
            $key = $null
            Write-Warning 'Saved OLLAMA credential cannot be decrypted on this Windows profile; re-entry required.'
        }
    }
    if ([string]::IsNullOrWhiteSpace($key)) {
        $secret = Read-Host 'OLLAMA API key (hidden)' -AsSecureString
        try {
            $key = Convert-NiraSecretToPlainText $secret
            if ([string]::IsNullOrWhiteSpace($key) -or
                $key.Trim().Length -lt 16 -or $key -match '\s') {
                throw 'OLLAMA API credential format is invalid.'
            }
            if (-not $SessionOnlyKey) {
                New-Item -ItemType Directory -Path $secretFolder -Force | Out-Null
                $ciphertext = $secret | ConvertFrom-SecureString
                $temp = Join-Path $secretFolder ('ollama-key-' + [guid]::NewGuid().ToString('N') + '.tmp')
                try {
                    [IO.File]::WriteAllText($temp, $ciphertext, [Text.UTF8Encoding]::new($false))
                    # This file is DPAPI encrypted, not a plain key. If interrupted,
                    # NIRA will ask for the credential again on the next launch.
                    Move-Item -LiteralPath $temp -Destination $secretPath -Force
                }
                finally { if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force } }
                Write-Host '[OLLAMA] Key saved encrypted for this Windows user (DPAPI).'
            }
        }
        finally { $secret.Dispose() }
    }
    if ([string]::IsNullOrWhiteSpace($key)) { throw 'OLLAMA API key is empty.' }
    [Environment]::SetEnvironmentVariable('NIRA_OLLAMA_API_KEY',$key.Trim(),'Process')
    [Environment]::SetEnvironmentVariable('NIRA_MODEL_PROVIDER','OLLAMA','Process')
    [Environment]::SetEnvironmentVariable('NIRA_MODEL_NAME',$Model,'Process')
    $key = $null
    if ($CaptureAgentDecisions) {
        [Environment]::SetEnvironmentVariable('NIRA_DEBUG_AGENT_DECISIONS','1','Process')
        [Environment]::SetEnvironmentVariable('NIRA_DEBUG_PUBLIC_RESPONSES','1','Process')
        Write-Warning 'Full model responses and tool arguments will be logged locally for this launch. Keep the files private.'
    }
    $forward = @{}
    if ($AllowPersonalContext) { $forward.AllowPersonalContext = $true }
    if ($AllowLifecycleContext) { $forward.AllowLifecycleContext = $true }
    Write-Host ('[NIRA] Ollama Cloud | Model: ' + $Model)
    & $launcher @forward
    if ($LASTEXITCODE -ne 0) { throw ('NIRA desktop exited with code ' + $LASTEXITCODE) }
}
finally {
    [Environment]::SetEnvironmentVariable('NIRA_MODEL_PROVIDER',$oldProvider,'Process')
    [Environment]::SetEnvironmentVariable('NIRA_MODEL_NAME',$oldModel,'Process')
    [Environment]::SetEnvironmentVariable('NIRA_OLLAMA_API_KEY',$oldKey,'Process')
    [Environment]::SetEnvironmentVariable('NIRA_DEBUG_AGENT_DECISIONS',$oldDecisionCapture,'Process')
    [Environment]::SetEnvironmentVariable('NIRA_DEBUG_PUBLIC_RESPONSES',$oldPublicCapture,'Process')
}