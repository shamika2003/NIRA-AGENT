# NIRA image diagnostics, Step 1: exercise the EXACT current NIRA .NET HTTP handler
# against the same public Wikimedia URLs that succeeded through curl.exe in Step 0.
# No LLM, no NIRA app, no credentials, and no NIRA source-code edits.
# Run from the NIRA-AGENT repository root on Windows with .NET SDK 10 installed.

$ErrorActionPreference = 'Stop'
$projectRoot = (Get-Location).Path
$step0Json = Join-Path $projectRoot 'NIRA_IMAGE_STEP0_RESULTS\commons_response.json'
$niraDll = Join-Path $projectRoot 'NIRAAgent\bin\Debug\net10.0\NIRAAgent.dll'
$resultsDir = Join-Path $projectRoot 'NIRA_IMAGE_STEP1_RESULTS'
$probeDir = Join-Path $resultsDir 'Probe'
$report = Join-Path $resultsDir 'RESULTS.txt'

Write-Host 'NIRA STEP 1 - .NET TRANSPORT COMPARISON (NO LLM, NO SOURCE CHANGES)'
if (-not (Test-Path -LiteralPath $step0Json)) {
    throw 'Missing Step 0 commons_response.json. Run the Step 0 script first.'
}
if (-not (Test-Path -LiteralPath $niraDll)) {
    throw 'Missing compiled NIRAAgent.dll. Build NIRAAgent once or run your UI project once, then retry.'
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet SDK was not found. .NET 10 SDK is required for the exact transport comparison.'
}

$doc = Get-Content -LiteralPath $step0Json -Encoding UTF8 -Raw | ConvertFrom-Json
$mediaUrl = $null
foreach ($page in @($doc.query.pages)) {
    foreach ($info in @($page.imageinfo)) {
        foreach ($possible in @([string]$info.thumburl, [string]$info.url)) {
            if ([string]::IsNullOrWhiteSpace($possible)) { continue }
            $uri = $null
            if (-not [Uri]::TryCreate($possible, [UriKind]::Absolute, [ref]$uri)) { continue }
            if ($uri.Scheme -ne 'https' -or $uri.UserInfo -or $uri.Port -ne 443) { continue }
            if (@('thumb.wikimedia.org', 'upload.wikimedia.org') -notcontains $uri.DnsSafeHost.ToLowerInvariant()) { continue }
            $mediaUrl = $uri.AbsoluteUri
            break
        }
        if ($mediaUrl) { break }
    }
    if ($mediaUrl) { break }
}
if (-not $mediaUrl) { throw 'No verified Wikimedia photo URL found in the Step 0 Commons JSON.' }
New-Item -ItemType Directory -Path $probeDir -Force | Out-Null

# Reference the existing compiled core NIRA assembly; this does not rebuild or
# patch NIRAAgent, its WPF UI, plugins, prompts, or source files.
$xmlDllPath = [System.Security.SecurityElement]::Escape($niraDll)
$csproj = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="NIRAAgent">
      <HintPath>__NIRA_DLL_PATH__</HintPath>
      <Private>true</Private>
    </Reference>
  </ItemGroup>
</Project>
'@
$csproj = $csproj.Replace('__NIRA_DLL_PATH__', $xmlDllPath)
Set-Content -LiteralPath (Join-Path $probeDir 'Probe.csproj') -Value $csproj -Encoding UTF8

$program = @'
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NIRAAgent.Images;

internal static class Program
{
    private static async Task<bool> ProbeAsync(string label, string url, bool expectImage, bool useNiraHandler)
    {
        var timer = Stopwatch.StartNew();
        try
        {
            using HttpMessageHandler handler = useNiraHandler
                ? (HttpMessageHandler)(typeof(NIRACommonsImageDiscoveryService).GetMethod(
                    "CreateImageHttpHandler", BindingFlags.Static | BindingFlags.NonPublic)
                    ?.Invoke(null, null) ?? throw new InvalidOperationException("NIRA private handler factory missing"))
                : new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    UseCookies = false,
                    UseProxy = true,
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
                };
            using var client = new HttpClient(handler, disposeHandler: false);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NIRAAgent/1.0 (desktop Wikimedia Commons reference image client)");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                Console.WriteLine($"[{label}] FAIL HTTP={(int)response.StatusCode} TIME={timer.Elapsed.TotalSeconds:F2}s");
                return false;
            }
            if (response.Content.Headers.ContentLength is long announced && announced > 8_000_000)
                throw new InvalidDataException("Excessive response size");
            byte[] bytes = await response.Content.ReadAsByteArrayAsync(timeout.Token);
            if (bytes.Length < 64 || bytes.Length > 8_000_000)
                throw new InvalidDataException("Missing/oversized content");
            if (expectImage)
            {
                bool jpeg = bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
                bool png = bytes.Length > 8 && bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71;
                if (!jpeg && !png) throw new InvalidDataException("No JPEG/PNG signature");
                Console.WriteLine($"[{label}] PASS HTTP=200 BYTES={bytes.Length} IMAGE={(jpeg ? "JPEG" : "PNG")} TIME={timer.Elapsed.TotalSeconds:F2}s");
            }
            else
            {
                using var json = JsonDocument.Parse(bytes);
                if (!json.RootElement.TryGetProperty("query", out var query) ||
                    !query.TryGetProperty("pages", out var pages) || pages.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("Not a Commons page-results JSON response");
                Console.WriteLine($"[{label}] PASS HTTP=200 BYTES={bytes.Length} PAGES={pages.GetArrayLength()} TIME={timer.Elapsed.TotalSeconds:F2}s");
            }
            return true;
        }
        catch (Exception ex)
        {
            Exception root = ex;
            while (root.InnerException != null) root = root.InnerException;
            string message = root.Message.Replace('\r', ' ').Replace('\n', ' ');
            if (message.Length > 180) message = message[..180];
            Console.WriteLine($"[{label}] FAIL TIME={timer.Elapsed.TotalSeconds:F2}s TYPE={ex.GetType().Name} CAUSE={root.GetType().Name} DETAIL={message}");
            return false;
        }
    }

    private static async Task<int> Main(string[] args)
    {
        if (args.Length != 1 || !Uri.TryCreate(args[0], UriKind.Absolute, out var mediaUrl) ||
            mediaUrl.Scheme != "https" ||
            (mediaUrl.Host != "thumb.wikimedia.org" && mediaUrl.Host != "upload.wikimedia.org"))
        {
            Console.WriteLine("ERROR: Missing verified Wikimedia media URL");
            return 2;
        }
        var apiFactory = typeof(NIRACommonsImageDiscoveryService).GetMethod(
            "CommonsSearchUrl", BindingFlags.Static | BindingFlags.NonPublic);
        string apiUrl = (string)(apiFactory?.Invoke(null, new object[] { "Hubble Space Telescope", 5 })
            ?? throw new InvalidOperationException("NIRA private Commons API URL factory missing"));
        Console.WriteLine("STEP1_DOTNET=" + Environment.Version);
        Console.WriteLine("STEP1_NIRA_ASSEMBLY=" + typeof(NIRACommonsImageDiscoveryService).Assembly.Location);
        Console.WriteLine("STEP1_SITE=commons.wikimedia.org, image host=" + mediaUrl.Host);
        bool defaultApi = await ProbeAsync("DEFAULT-API", apiUrl, false, false);
        bool niraApi = await ProbeAsync("NIRA-API", apiUrl, false, true);
        bool defaultImage = await ProbeAsync("DEFAULT-IMAGE", mediaUrl.AbsoluteUri, true, false);
        bool niraImage = await ProbeAsync("NIRA-IMAGE", mediaUrl.AbsoluteUri, true, true);
        if (defaultApi && defaultImage && (!niraApi || !niraImage))
            Console.WriteLine("STEP1_RESULT=NIRA_TRANSPORT_SUSPECT: Default .NET works but NIRA custom handler fails.");
        else if (!defaultApi && !defaultImage && !niraApi && !niraImage)
            Console.WriteLine("STEP1_RESULT=DOTNET_NETWORK_FAILURE: curl works, but both .NET handler paths fail.");
        else if (defaultApi && defaultImage && niraApi && niraImage)
            Console.WriteLine("STEP1_RESULT=BOTH_TRANSPORTS_PASS: Next isolate NIRA image.search provider ordering and timeout logic.");
        else
            Console.WriteLine("STEP1_RESULT=MIXED: Inspect individual API/image results before deciding on changes.");
        return 0;
    }
}
'@
Set-Content -LiteralPath (Join-Path $probeDir 'Program.cs') -Value $program -Encoding UTF8

Write-Host 'Running regular .NET HTTP versus NIRA exact HTTP transport against Commons API and a real JPEG...'
Write-Host 'This may take up to about one minute if requests time out.'
$old = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
try {
    $output = (& dotnet run --project (Join-Path $probeDir 'Probe.csproj') --configuration Release -- $mediaUrl 2>&1 | Out-String)
    $exitCode = $LASTEXITCODE
} finally {
    $ErrorActionPreference = $old
}
$output | Set-Content -LiteralPath $report -Encoding UTF8
Write-Host $output
Write-Host "STEP1_PROCESS_EXIT=$exitCode"
Write-Host "REPORT_FILE=$report"
if ($exitCode -ne 0) { Write-Host 'The standalone .NET test could not run. Send RESULTS.txt to diagnose the test runner.' }
