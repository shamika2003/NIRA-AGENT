[CmdletBinding()]
param([switch]$SkipDesktop, [switch]$SkipExport)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = $PSScriptRoot
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '.NET 10 SDK is required.' }

# Rebuild all projects and execute the newly compiled test assembly.
# Source changes are tracked with Git; every build recompiles the real code.
# NIRA PYTHON FULL-SOURCE EXPORT
if (-not (Test-Path -LiteralPath (Join-Path $root 'NIRA_FULL_CODE.py'))) {
    throw 'Python source exporter is missing.'
}

$projects = @(
    'src/Nira.Core/Nira.Core.csproj',
    'src/Nira.Infrastructure/Nira.Infrastructure.csproj',
    'src/Nira.Cli/Nira.Cli.csproj',
    'tests/Nira.Kernel.Tests/Nira.Kernel.Tests.csproj'
)
if (-not $SkipDesktop) { $projects += 'src/Nira.Desktop/Nira.Desktop.csproj' }
foreach ($project in $projects) {
    $projectFile = Join-Path $root $project
    & dotnet build $projectFile -c Debug -t:Rebuild --nologo
    if ($LASTEXITCODE -ne 0) { throw "Rebuild failed: $project" }
}

# Do not use 'dotnet run --no-build' here: the original bug ran a stale test
# executable even though the corrected Program.cs passed source verification.
$testDll = Join-Path $root 'tests/Nira.Kernel.Tests/bin/Debug/net10.0-windows/Nira.Kernel.Tests.dll'
if (-not (Test-Path -LiteralPath $testDll -PathType Leaf)) {
    throw "Freshly rebuilt test assembly missing: $testDll"
}
Write-Host 'NIRA executing rebuilt kernel test DLL:' -ForegroundColor Cyan
Write-Host $testDll
& dotnet $testDll
if ($LASTEXITCODE -ne 0) { throw 'Kernel tests failed.' }

# One source document, created from real files; no extra ZIP or manifest.
if (-not $SkipExport) {
    $exporter = Join-Path $root 'NIRA_FULL_CODE.py'
    if (Get-Command py -ErrorAction SilentlyContinue) {
        & py -3 $exporter --root $root
    } elseif (Get-Command python -ErrorAction SilentlyContinue) {
        & python $exporter --root $root
    } else {
        throw 'Python 3 is required for the source export.'
    }
    if ($LASTEXITCODE -ne 0) { throw 'Full code export failed.' }
}
Write-Host 'NIRA FOUNDATION BUILD + KERNEL TESTS PASS' -ForegroundColor Green
