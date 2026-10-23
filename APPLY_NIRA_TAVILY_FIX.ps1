param([switch]$NoLaunch)

$ErrorActionPreference = 'Stop'
$project = $PSScriptRoot
$patch = Join-Path $project '_NIRA_TAVILY_PATCH'
if (!(Test-Path (Join-Path $project 'NIRAAgent.UI\NIRAAgent.UI.csproj'))) {
    throw 'Run this script from the root of your existing NIRA-AGENT project.'
}
if (!(Test-Path $patch)) { throw 'The _NIRA_TAVILY_PATCH folder is missing. Extract the entire ZIP again.' }

# Keep this fix reversible. No unrelated files are deleted or overwritten.
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$backup = Join-Path $env:LOCALAPPDATA ("NIRAAgent\patch-backups\TavilyProvider_" + $stamp)
$patched = @()
$files = @(Get-ChildItem -Path $patch -Recurse -File)
Write-Host "NIRA Tavily provider repair: $($files.Count) reviewed source files" -ForegroundColor Cyan
Write-Host 'Stopping any running NIRA UI to prevent an old assembly being used...'
Get-Process -Name 'NIRAAgent.UI' -ErrorAction SilentlyContinue | Stop-Process -Force

try {
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($patch.Length).TrimStart([char]'\',[char]'/')
        $destination = Join-Path $project $relative
        $oldExists = Test-Path $destination
        if ($oldExists) {
            $copy = Join-Path $backup $relative
            New-Item -ItemType Directory -Path (Split-Path $copy -Parent) -Force | Out-Null
            Copy-Item -LiteralPath $destination -Destination $copy -Force
        }
        $patched += [pscustomobject]@{ File=$destination; Relative=$relative; HadOld=$oldExists }
        New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
    }

    # Source-level contracts prevent silently launching the old Commons route.
    $app = Get-Content (Join-Path $project 'NIRAAgent.UI\App.xaml.cs') -Raw
    $cap = Get-Content (Join-Path $project 'NIRAAgent\Capabilities\ImageDiscoveryCapabilityHandlers.cs') -Raw
    $planner = Get-Content (Join-Path $project 'NIRAAgent\AI\Cognition\NIRAResourceIntentPlanner.cs') -Raw
    $web = Get-Content (Join-Path $project 'NIRAAgent\Capabilities\NIRAWebSearchCapabilityHandler.cs') -Raw
    $imagePath = Join-Path $project 'NIRAAgent\Images\NIRATavilyImageDiscoveryService.cs'
    if (!(Test-Path $imagePath) -or
        $app -notmatch 'AddSingleton<INIRAImageDiscoveryService>' -or
        $app -notmatch 'GetRequiredService<NIRATavilyImageDiscoveryService>' -or
        $cap -notmatch 'INIRAImageDiscoveryService _service' -or
        $planner -notmatch 'includeImages = sharedTavily' -or
        $web -notmatch 'NIRATavilyImageDiscoveryService images') {
        throw 'The Tavily production routing contract is not present. Refusing to launch.'
    }
    Write-Host 'SOURCE ROUTE VERIFIED: image.search -> INIRAImageDiscoveryService -> Tavily' -ForegroundColor Green
    Write-Host 'SOURCE ROUTE VERIFIED: combined web+image -> one Tavily web.search receipt' -ForegroundColor Green

    $key = [Environment]::GetEnvironmentVariable('NIRA_TAVILY_API_KEY', 'Process')
    if ([string]::IsNullOrWhiteSpace($key)) {
        $key = [Environment]::GetEnvironmentVariable('NIRA_TAVILY_API_KEY', 'User')
    }
    if ([string]::IsNullOrWhiteSpace($key)) {
        $key = [Environment]::GetEnvironmentVariable('TAVILY_API_KEY', 'Process')
    }
    if ([string]::IsNullOrWhiteSpace($key)) {
        $key = [Environment]::GetEnvironmentVariable('TAVILY_API_KEY', 'User')
    }
    if ([string]::IsNullOrWhiteSpace($key)) {
        Write-Warning 'No Tavily API key was found in process/user environment. Tavily images will require the existing Tavily key.'
    } else {
        Write-Host 'Tavily credential available (value not displayed).' -ForegroundColor Green
    }

    Push-Location $project
    try {
        Write-Host 'Building all relevant projects with no incremental compile...' -ForegroundColor Cyan
        & dotnet build .\NIRAAgent.UI\NIRAAgent.UI.csproj --no-incremental
        if ($LASTEXITCODE -ne 0) { throw 'NIRA UI build failed.' }
        Write-Host 'Running regression checks including image-only and shared Tavily call...' -ForegroundColor Cyan
        $regressionOutput = @(& dotnet run --project .\NIRAAgent.RegressionChecks 2>&1)
        $exit = $LASTEXITCODE
        $regressionOutput | ForEach-Object { Write-Host $_ }
        if ($exit -ne 0) { throw 'NIRA regression checks failed.' }
        $regressionText = $regressionOutput -join "`n"
        if ($regressionText -notmatch 'PASS: TAVILY shared web and images uses ONE provider request and real bytes' -or
            $regressionText -notmatch 'PASS: TAVILY image-only discovery uses existing key without calling web.search' -or
            $regressionText -notmatch 'REGRESSION CHECKS:\s+\d+ passed, 0 failed') {
            throw 'The Tavily-specific regressions were not executed successfully.'
        }
        Write-Host 'PASS: BUILD + TAVILY IMAGE + TAVILY WEB/IMAGE + GENERAL REGRESSIONS' -ForegroundColor Green
        Write-Host "Source backup: $backup"
        if (!$NoLaunch) {
            Write-Host 'Launching the verified NIRA UI...' -ForegroundColor Cyan
            & dotnet run --project .\NIRAAgent.UI
        }
    } finally { Pop-Location }
}
catch {
    Write-Warning 'Applying/building/testing failed. Restoring original files automatically.'
    foreach ($item in $patched) {
        if ($item.HadOld) {
            $old = Join-Path $backup $item.Relative
            if (Test-Path $old) { Copy-Item -LiteralPath $old -Destination $item.File -Force }
        } elseif (Test-Path $item.File) {
            Remove-Item -LiteralPath $item.File -Force
        }
    }
    throw
}
