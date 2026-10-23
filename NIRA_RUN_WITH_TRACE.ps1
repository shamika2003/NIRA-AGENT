# Run from your NIRA-AGENT folder, after closing the desktop app.
# This captures FULL model prompts/responses and selected tool evidence.
# The resulting LLM transcripts can contain personal information/API context;
# review them and remove secrets before sharing with anyone.
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
$env:NIRA_DEBUG_LLM_TRANSCRIPTS = "1"
$env:NIRA_DEBUG_TOOL_DETAILS = "1"
Write-Host "NIRA trace capture enabled for this PowerShell session." -ForegroundColor Cyan
Write-Host "Diagnostic folder: $env:LOCALAPPDATA\NIRAAgent\diagnostics"
Write-Host "Building NIRA..."
dotnet build .\NIRAAgent.UI\NIRAAgent.UI.csproj --no-incremental
if ($LASTEXITCODE -ne 0) { throw "NIRA build failed: do not launch an older binary." }
Write-Host "Running offline regression checks..."
dotnet run --project .\NIRAAgent.RegressionChecks
if ($LASTEXITCODE -ne 0) { throw "Offline regression checks failed: review the error before launching." }
Write-Host "Running NIRA with full diagnostic capture..." -ForegroundColor Green
dotnet run --project .\NIRAAgent.UI
