# NIRA image diagnostics, Step 2: exercise the REAL compiled NIRA image service.
# Checks (1) parsing previously successful Commons JSON, (2) live NIRA
# search/fallback, (3) retrieval using a real candidate ID from that search,
# (4) locally saved image bytes. No LLM, browser, source changes or API keys.
# Run in NIRA-AGENT repo root with PowerShell and .NET 10 SDK.
param(
    [string]$Query = 'Hubble Space Telescope'
)
$ErrorActionPreference = 'Stop'
$root = (Get-Location).Path
$existing = Join-Path $root 'NIRA_IMAGE_STEP0_RESULTS\commons_response.json'
$niraDll = Join-Path $root 'NIRAAgent\bin\Debug\net10.0\NIRAAgent.dll'
$resultDir = Join-Path $root 'NIRA_IMAGE_STEP2_RESULTS'
$projectDir = Join-Path $resultDir 'Probe'
$report = Join-Path $resultDir 'RESULTS.txt'

Write-Host 'NIRA STEP 2 - ACTUAL IMAGE SERVICE (NO LLM, NO SOURCE CHANGES)'
Write-Host "Query=$Query"
if ([string]::IsNullOrWhiteSpace($Query) -or $Query.Length -gt 160 -or $Query.Length -lt 2 -or $Query -match '[\x00-\x1F\x7F]') {
    throw 'Query must be between 2 and 160 visible characters.'
}
if (-not (Test-Path -LiteralPath $existing -PathType Leaf)) { throw 'Missing NIRA_IMAGE_STEP0_RESULTS\commons_response.json. Run Step 0 first.' }
if (-not (Test-Path -LiteralPath $niraDll -PathType Leaf)) { throw 'Missing compiled NIRAAgent.dll. Build the existing project first; do NOT modify NIRA source for this diagnostic.' }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'dotnet SDK is required.' }
New-Item -ItemType Directory -Path $projectDir -Force | Out-Null

# Reference existing compiled NIRA DLL; compiling this independent probe does
# not rebuild, replace or patch any source or binary in the NIRA repository.
$hintPath = [System.Security.SecurityElement]::Escape($niraDll)
$project = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="NIRAAgent">
      <HintPath>__NIRA_DLL__</HintPath>
      <Private>true</Private>
    </Reference>
  </ItemGroup>
</Project>
'@
$project = $project.Replace('__NIRA_DLL__', $hintPath)
Set-Content -LiteralPath (Join-Path $projectDir 'Probe.csproj') -Value $project -Encoding UTF8

$program = @'
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NIRAAgent.Images;

internal static class Program
{
    private static string Safe(Exception ex)
    {
        Exception cause = ex;
        while (cause.InnerException is not null) cause = cause.InnerException;
        string s = cause.Message.Replace('\r', ' ').Replace('\n', ' ');
        if (s.Length > 170) s = s[..170];
        return $"TYPE={ex.GetType().Name} CAUSE={cause.GetType().Name} DETAIL={s}";
    }

    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 2 || !File.Exists(args[0]))
        {
            Console.WriteLine("STEP2_ERROR=Missing Step0 JSON or query argument");
            return 2;
        }
        Console.WriteLine($"STEP2_DOTNET={Environment.Version}");
        Console.WriteLine($"STEP2_NIRA_ASSEMBLY={typeof(NIRACommonsImageDiscoveryService).Assembly.Location}");
        Console.WriteLine($"STEP2_QUERY={args[1]}");
        int offlineCandidates = -1;
        try
        {
            var sw = Stopwatch.StartNew();
            var found = NIRACommonsImageDiscoveryService.ParseCommonsSearchResponse(File.ReadAllText(args[0]), 5);
            offlineCandidates = found.Count;
            Console.WriteLine($"[OFFLINE-PARSE] {(found.Count > 0 ? "PASS" : "EMPTY")} ACCEPTED={found.Count} TIME={sw.Elapsed.TotalSeconds:F2}s");
            if (found.Count > 0)
            {
                var first = found[0];
                Console.WriteLine($"[OFFLINE-PARSE] PROVIDER={first.Provider} MEDIA_HOST={new Uri(first.MediaUrl).Host} APPROVED={NIRACommonsImageDiscoveryService.IsPermittedMediaUri(first.MediaUrl)}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("[OFFLINE-PARSE] FAIL " + Safe(ex));
        }

        using var service = new NIRACommonsImageDiscoveryService();
        System.Collections.Generic.IReadOnlyList<NIRACommonsImageCandidate>? candidates = null;
        try
        {
            Console.WriteLine("[LIVE-SEARCH] START (NIRA's actual provider selection and fallback)");
            var sw = Stopwatch.StartNew();
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(70));
            candidates = await service.SearchAsync(args[1], 5, limit.Token);
            Console.WriteLine($"[LIVE-SEARCH] {(candidates.Count > 0 ? "PASS" : "EMPTY")} ACCEPTED={candidates.Count} TIME={sw.Elapsed.TotalSeconds:F2}s");
            Console.WriteLine($"[LIVE-SEARCH] STATUS={service.LastSearchStatus}");
            for (int i = 0; i < Math.Min(candidates.Count, 5); i++)
            {
                var item = candidates[i];
                string host = Uri.TryCreate(item.MediaUrl, UriKind.Absolute, out var u) ? u.Host : "INVALID";
                Console.WriteLine($"[CANDIDATE-{i+1}] PROVIDER={item.Provider} HOST={host} APPROVED={NIRACommonsImageDiscoveryService.IsPermittedMediaUri(item.MediaUrl)} TITLE={item.Title[..Math.Min(75, item.Title.Length)].Replace('\n', ' ')}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LIVE-SEARCH] FAIL STATUS={service.LastSearchStatus} {Safe(ex)}");
        }

        if (candidates is null || candidates.Count == 0)
        {
            Console.WriteLine(offlineCandidates > 0
                ? "STEP2_RESULT=SEARCH_STAGE_SUSPECT: Previously verified Commons JSON parses, but live NIRA image search did not yield candidates."
                : "STEP2_RESULT=SEARCH_OR_PARSING_SUSPECT: Neither offline parsing nor live search produced candidates.");
            return 0;
        }
        try
        {
            Console.WriteLine("[LIVE-RETRIEVE] START (using NIRA-issued candidateId, never a fabricated URL)");
            var sw = Stopwatch.StartNew();
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            NIRACommonsImageDownload result = await service.RetrieveAsync(candidates[0].CandidateId, limit.Token);
            bool exists = File.Exists(result.LocalPath);
            var bytes = exists ? await File.ReadAllBytesAsync(result.LocalPath) : Array.Empty<byte>();
            bool jpeg = bytes.Length > 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff;
            bool png = bytes.Length > 8 && bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71;
            bool gif = bytes.Length > 6 && bytes[0] == 71 && bytes[1] == 73 && bytes[2] == 70 && bytes[3] == 56;
            string actualSha = bytes.Length > 0 ? Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() : "";
            bool verified = exists && bytes.LongLength == result.Bytes && (jpeg || png || gif) &&
                string.Equals(actualSha, result.Sha256, StringComparison.OrdinalIgnoreCase);
            Console.WriteLine($"[LIVE-RETRIEVE] {(verified ? "PASS" : "FAIL_VERIFICATION")} TIME={sw.Elapsed.TotalSeconds:F2}s SAVED={exists} BYTES={bytes.LongLength} FORMAT={(jpeg ? "JPEG" : png ? "PNG" : gif ? "GIF" : "UNKNOWN")} HASH_OK={actualSha == result.Sha256}");
            Console.WriteLine($"[LIVE-RETRIEVE] FILE={result.LocalPath}");
            Console.WriteLine(verified
                ? "STEP2_RESULT=CORE_PIPELINE_PASS: Search, candidate selection, download, and saved bytes work. Next examine tool dispatch/presentation."
                : "STEP2_RESULT=FILE_VERIFICATION_SUSPECT: Retrieve returned but saved artifact is invalid.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("[LIVE-RETRIEVE] FAIL " + Safe(ex));
            Console.WriteLine("STEP2_RESULT=RETRIEVAL_STAGE_SUSPECT: Search returns candidates, but image.retrieve fails.");
        }
        return 0;
    }
}
'@
Set-Content -LiteralPath (Join-Path $projectDir 'Program.cs') -Value $program -Encoding UTF8

Write-Host 'Testing offline parsing, NIRA live search, and NIRA image retrieval.'
Write-Host 'Expect Openverse to time out before Wikimedia fallback in the current build.'
Write-Host 'The network phase may take approximately one minute if fallbacks time out.'
$prior = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
try {
    $runOutput = (& dotnet run --project (Join-Path $projectDir 'Probe.csproj') --configuration Release -- $existing $Query 2>&1 | Out-String)
    $resultCode = $LASTEXITCODE
} finally { $ErrorActionPreference = $prior }
$runOutput | Set-Content -LiteralPath $report -Encoding UTF8
Write-Host $runOutput
Write-Host "STEP2_PROCESS_EXIT=$resultCode"
Write-Host "REPORT_FILE=$report"
if ($resultCode -ne 0) {
    Write-Host 'The independent probe failed to compile/run. Send RESULTS.txt; do not modify the main project.'
}
