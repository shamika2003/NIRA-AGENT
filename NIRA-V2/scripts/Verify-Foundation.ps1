param([switch]$IncludeDemo)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($null -eq $dotnet) { throw 'The .NET SDK 10 is required. Install it, then retry.' }
$tests = @(
    (Join-Path $root 'tests/Nira.V2.Tests/Nira.V2.Tests.csproj'),
    (Join-Path $root 'tests/Nira.V2.AgentLoop.Tests/Nira.V2.AgentLoop.Tests.csproj')
)
Write-Host '[NIRA V2] Independent build. Legacy NIRA files will not be changed.'
foreach ($project in $tests) {
    Write-Host ('[BUILD] ' + $project)
    & dotnet build $project -c Debug -t:Rebuild --nologo --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw ('Build failed: ' + $project) }
    Write-Host ('[TEST] ' + $project)
    & dotnet run --no-build -c Debug --project $project
    if ($LASTEXITCODE -ne 0) { throw ('Tests failed: ' + $project) }
}
if ($IncludeDemo) {
    & dotnet run -c Debug --project (Join-Path $root 'apps/Nira.V2.Demo/Nira.V2.Demo.csproj')
    if ($LASTEXITCODE -ne 0) { throw 'Offline demo failed.' }
}
Write-Host '[PASS] NIRA V2 offline foundation checks passed; legacy runtime unchanged.'
