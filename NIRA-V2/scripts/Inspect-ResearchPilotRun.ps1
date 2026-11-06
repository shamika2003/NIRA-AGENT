param(
    [string]$CheckpointId,
    [ValidateRange(1,100)][int]$LastEvents = 35
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Join-Path $env:TEMP 'NIRA-V2-ResearchPilotRuns'
if (-not (Test-Path -LiteralPath $root -PathType Container)) {
    throw 'ResearchPilot state folder not found. Run the pilot first.'
}
if ([string]::IsNullOrWhiteSpace($CheckpointId)) {
    $checkpoints = @(Get-ChildItem -LiteralPath $root -File -Filter '*.json' |
        Where-Object { $_.BaseName -match '^[0-9a-fA-F]{32}$' } |
        Sort-Object LastWriteTimeUtc -Descending)
    if ($checkpoints.Count -eq 0) { throw 'No saved ResearchPilot runs found.' }
    $file = $checkpoints[0].FullName
} else {
    $id = [Guid]::Empty
    if (-not [Guid]::TryParse($CheckpointId, [ref]$id)) { throw 'Invalid checkpoint ID.' }
    $file = Join-Path $root ($id.ToString('N') + '.json')
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw 'Checkpoint not found.' }
}
$run = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
Write-Host 'NIRA V2 ResearchPilot — SAFE CHECKPOINT DIAGNOSTIC'
Write-Host ('Checkpoint: ' + $run.Id)
Write-Host ('Status: ' + $run.Status)
Write-Host ('Reason: ' + $run.LastErrorCode)
Write-Host ('Model decisions: ' + $run.DecisionCount + ' | Tool dispatches: ' + $run.ToolCallCount)
Write-Host ('Observations: ' + @($run.Observations).Count)
Write-Host 'Recent activity codes (NO prompt, response, source excerpts or API keys):'
@($run.Activity | Select-Object -Last $LastEvents) | ForEach-Object {
    Write-Host ('  ' + $_.AtUtc + ' | ' + $_.Code + ' | ' + $_.ToolId + ' | ' + $_.DetailCode)
}
