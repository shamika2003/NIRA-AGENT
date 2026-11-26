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
if (-not $SkipDesktop) { $projects += @('src/Nira.Desktop/Nira.Desktop.csproj', 'tests/Nira.Desktop.Tests/Nira.Desktop.Tests.csproj') }
# Let MSBuild manage project references, including WPF markup compilation.

Write-Host "`n===== STOP BUILD SERVERS =====" -ForegroundColor Cyan

& dotnet build-server shutdown

if ($LASTEXITCODE -ne 0) {
    throw "Build server shutdown failed."
}

Write-Host "`n===== CLEAN GENERATED OUTPUTS =====" -ForegroundColor Cyan

foreach ($project in $projects) {

    $directory = Split-Path -Parent (Join-Path $root $project)

    foreach ($folder in @("bin", "obj")) {

        $generated = Join-Path $directory $folder

        if (Test-Path -LiteralPath $generated) {

            if ((Get-Item -LiteralPath $generated).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Unexpected linked build directory: $generated"
            }

            Remove-Item -LiteralPath $generated -Recurse -Force

            Write-Host "Cleaned: $generated"
        }
    }
}

Write-Host "`n===== RESTORE DEPENDENCIES =====" -ForegroundColor Cyan

foreach ($project in $projects) {

    $projectFile = Join-Path $root $project

    & dotnet restore $projectFile --disable-parallel

    if ($LASTEXITCODE -ne 0) {
        throw "Restore failed: $project"
    }
}

Write-Host "`n===== DEPENDENCY-AWARE COMPILATION =====" -ForegroundColor Cyan

foreach ($project in $projects) {

    $projectFile = Join-Path $root $project

    Write-Host "`nBuilding: $project" -ForegroundColor Cyan

    & dotnet build $projectFile `
        -c Debug `
        --no-restore `
        --nologo `
        -m:1 `
        -nr:false `
        -p:BuildInParallel=false `
        -p:UseSharedCompilation=false

    if ($LASTEXITCODE -ne 0) {
        throw "Build failed: $project"
    }
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
if (-not $SkipDesktop) {
    $desktopTestDll = Join-Path $root 'tests/Nira.Desktop.Tests/bin/Debug/net10.0-windows/Nira.Desktop.Tests.dll'
    if (-not (Test-Path -LiteralPath $desktopTestDll -PathType Leaf)) { throw 'Fresh desktop test assembly missing.' }
    & dotnet $desktopTestDll
    if ($LASTEXITCODE -ne 0) { throw 'Desktop renderer tests failed.' }
}

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
