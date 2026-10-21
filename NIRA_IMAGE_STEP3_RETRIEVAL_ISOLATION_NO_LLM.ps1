# NIRA image Step 3: diagnosis ONLY. Uses the current compiled NIRA DLL and
# its exact HTTP handler. NO NIRA source edits, browser, LLM, API keys or image creation.
# The Commons-only run skips Openverse in this standalone test harness, NOT in production.
param([string]$Query = 'Hubble Space Telescope')
$ErrorActionPreference = 'Stop'
$root = (Get-Location).Path
$niraDll = Join-Path $root 'NIRAAgent\bin\Debug\net10.0\NIRAAgent.dll'
$commonsJson = Join-Path $root 'NIRA_IMAGE_STEP0_RESULTS\commons_response.json'
$results = Join-Path $root 'NIRA_IMAGE_STEP3_RESULTS'
$probe = Join-Path $results 'Probe'
$report = Join-Path $results 'RESULTS.txt'
if ($Query.Trim().Length -lt 2 -or $Query.Length -gt 160 -or $Query -match '[\x00-\x1F\x7F]') {
    throw 'Query must be 2-160 visible characters.'
}
if (-not (Test-Path -LiteralPath $niraDll -PathType Leaf)) {
    throw 'NIRAAgent.dll not found. Build existing NIRA project first.'
}
if (-not (Test-Path -LiteralPath $commonsJson -PathType Leaf)) {
    throw 'Run Step 0 first. commons_response.json was not found.'
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '.NET SDK missing.' }
New-Item -ItemType Directory -Path $probe -Force | Out-Null
$escapedDll = [System.Security.SecurityElement]::Escape($niraDll)
$project = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup><Reference Include="NIRAAgent"><HintPath>__DLL__</HintPath><Private>true</Private></Reference></ItemGroup>
</Project>
'@
$project = $project.Replace('__DLL__', $escapedDll)
Set-Content -LiteralPath (Join-Path $probe 'Probe.csproj') -Value $project -Encoding UTF8

$program = @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NIRAAgent.Images;

internal sealed class CommonsOnlyDiagnosticHandler(HttpMessageHandler trustedInner) : DelegatingHandler(trustedInner)
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        // Bypass Openverse in this DIAGNOSTIC instance only; send all real
        // Wikimedia requests through NIRA's own security-preserving handler.
        if (request.RequestUri?.Host.Equals("api.openverse.org", StringComparison.OrdinalIgnoreCase) == true)
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = request });
        return base.SendAsync(request, token);
    }
}
internal static class Program
{
    static string Str(JsonElement obj, string name) => obj.ValueKind == JsonValueKind.Object &&
        obj.TryGetProperty(name, out var val) && val.ValueKind == JsonValueKind.String ? val.GetString() ?? "" : "";
    static string ExceptionInfo(Exception ex)
    {
        Exception root = ex;
        while (root.InnerException != null) root = root.InnerException;
        string msg = root.Message.Replace('\r',' ').Replace('\n',' ');
        if (msg.Length > 160) msg = msg[..160];
        return $"TYPE={ex.GetType().Name} ROOT={root.GetType().Name} DETAIL={msg}";
    }
    static HttpMessageHandler NiraHandler() => (HttpMessageHandler)(
        typeof(NIRACommonsImageDiscoveryService).GetMethod("CreateImageHttpHandler", BindingFlags.NonPublic | BindingFlags.Static)?
        .Invoke(null, null) ?? throw new InvalidOperationException("Compiled NIRA HTTP factory missing"));
    static bool Raster(byte[] bytes) =>
        bytes.Length > 64 &&
        ((bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255) ||
        (bytes.Length >= 8 && bytes.Take(8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10})) ||
        (bytes[0] == 71 && bytes[1] == 73 && bytes[2] == 70 && bytes[3] == 56));
    static async Task ProbeMediaAsync(string label, string media)
    {
        if (!NIRACommonsImageDiscoveryService.IsPermittedMediaUri(media))
        {
            Console.WriteLine($"[{label}] SKIP INVALID_OR_MISSING_MEDIA_URL");
            return;
        }
        var uri = new Uri(media);
        var sw = Stopwatch.StartNew();
        try
        {
            using var handler = NiraHandler();
            using var client = new HttpClient(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NIRAAgent/1.0 (desktop Wikimedia Commons reference image client)");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(9));
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var mime = response.Content.Headers.ContentType?.MediaType ?? "unknown";
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[{label}] FAIL HOST={uri.Host} HTTP={(int)response.StatusCode} MIME={mime} TIME={sw.Elapsed.TotalSeconds:F2}s");
                return;
            }
            if (response.Content.Headers.ContentLength is long size && size > 10_000_000)
            {
                Console.WriteLine($"[{label}] SKIP OVERSIZE_FILE HOST={uri.Host}");
                return;
            }
            byte[] body = await response.Content.ReadAsByteArrayAsync(timeout.Token);
            Console.WriteLine($"[{label}] {(Raster(body) ? "PASS" : "INVALID_IMAGE")} HOST={uri.Host} HTTP=200 BYTES={body.Length} MIME={mime} TIME={sw.Elapsed.TotalSeconds:F2}s");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{label}] FAIL HOST={uri.Host} TIME={sw.Elapsed.TotalSeconds:F2}s {ExceptionInfo(ex)}");
        }
    }
    static void OfflineParse(string filename)
    {
        try
        {
            string json = File.ReadAllText(filename);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("query", out var query) ||
                !query.TryGetProperty("pages", out var pages) || pages.ValueKind != JsonValueKind.Array)
            {
                Console.WriteLine("[COMMONS-OFFLINE] FAIL NO_QUERY_PAGES");
                return;
            }
            int n = 0, infoPresent = 0, mimeGood = 0, mediaGood = 0, sourceGood = 0, allGood = 0;
            var mimes = new SortedDictionary<string,int>(StringComparer.OrdinalIgnoreCase);
            var hosts = new SortedDictionary<string,int>(StringComparer.OrdinalIgnoreCase);
            foreach (var page in pages.EnumerateArray())
            {
                n++;
                if (!page.TryGetProperty("imageinfo", out var arr) || arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0) continue;
                infoPresent++;
                var info = arr[0];
                string mime = Str(info, "mime");
                mimes[mime] = mimes.GetValueOrDefault(mime) + 1;
                bool goodMime = mime is "image/jpeg" or "image/png" or "image/gif";
                if (goodMime) mimeGood++;
                string url = Str(info, "thumburl");
                if (!NIRACommonsImageDiscoveryService.IsPermittedMediaUri(url)) url = Str(info, "url");
                bool goodMedia = NIRACommonsImageDiscoveryService.IsPermittedMediaUri(url);
                if (goodMedia) mediaGood++;
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) hosts[uri.Host] = hosts.GetValueOrDefault(uri.Host) + 1;
                string source = Str(info, "descriptionurl");
                bool goodSource = Uri.TryCreate(source, UriKind.Absolute, out var src) && src.Scheme == "https" && src.Host == "commons.wikimedia.org";
                if (goodSource) sourceGood++;
                if (goodMime && goodMedia && goodSource && !string.IsNullOrWhiteSpace(Str(page,"title"))) allGood++;
            }
            var parsed = NIRACommonsImageDiscoveryService.ParseCommonsSearchResponse(json, 8);
            Console.WriteLine($"[COMMONS-OFFLINE] PAGES={n} INFO_PRESENT={infoPresent} MIME_ALLOWED={mimeGood} MEDIA_ALLOWED={mediaGood} SOURCE_ALLOWED={sourceGood} ALL_FIELDS_OK={allGood} NIRA_ACCEPTED={parsed.Count}");
            Console.WriteLine("[COMMONS-OFFLINE] MIMES=" + string.Join(",", mimes.Select(e => $"{(e.Key.Length==0 ? "MISSING" : e.Key)}:{e.Value}")));
            Console.WriteLine("[COMMONS-OFFLINE] IMAGE_HOSTS=" + string.Join(",", hosts.Select(e => $"{e.Key}:{e.Value}")));
        }
        catch (Exception ex) { Console.WriteLine("[COMMONS-OFFLINE] FAIL " + ExceptionInfo(ex)); }
    }
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 2 || !File.Exists(args[0])) return 2;
        Console.WriteLine("STEP3_DOTNET=" + Environment.Version);
        Console.WriteLine("STEP3_ASSEMBLY=" + typeof(NIRACommonsImageDiscoveryService).Assembly.Location);
        Console.WriteLine("STEP3_QUERY=" + args[1]);
        Console.WriteLine("STEP3_MODE=READ_ONLY_NO_LLM_NO_PRODUCTION_CHANGE");
        OfflineParse(args[0]);

        // Discover provider-issued URLs. Never feed arbitrary user or model URLs into HTTP.
        try
        {
            using var svc = new NIRACommonsImageDiscoveryService();
            using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(28));
            var sw = Stopwatch.StartNew();
            var candidates = await svc.SearchAsync(args[1], 3, ct.Token);
            Console.WriteLine($"[NORMAL-SEARCH] COUNT={candidates.Count} TIME={sw.Elapsed.TotalSeconds:F2}s STATUS={svc.LastSearchStatus}");
            if (candidates.Count > 0)
            {
                var first = candidates[0];
                Console.WriteLine($"[NORMAL-SEARCH] PROVIDER={first.Provider} MEDIA_HOST={(Uri.TryCreate(first.MediaUrl,UriKind.Absolute,out var u) ? u.Host : "INVALID")} ALT_HOST={(Uri.TryCreate(first.AlternateMediaUrl,UriKind.Absolute,out var a) ? a.Host : "NONE")}");
                await ProbeMediaAsync("PROVIDER-PRIMARY-HTTP", first.MediaUrl);
                await ProbeMediaAsync("PROVIDER-ALTERNATE-HTTP", first.AlternateMediaUrl);
            }
        }
        catch(Exception ex) { Console.WriteLine("[NORMAL-SEARCH] FAIL " + ExceptionInfo(ex)); }

        // Force the Commons *branch* inside a completely separate service instance.
        // This is not an application setting or a patch to NIRA's provider order.
        try
        {
            using var inner = NiraHandler();
            using var testHandler = new CommonsOnlyDiagnosticHandler(inner);
            using var client = new HttpClient(testHandler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
            using var svc = new NIRACommonsImageDiscoveryService(client);
            using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var sw = Stopwatch.StartNew();
            var commons = await svc.SearchAsync(args[1], 4, ct.Token);
            Console.WriteLine($"[COMMONS-ONLY-SEARCH] COUNT={commons.Count} TIME={sw.Elapsed.TotalSeconds:F2}s STATUS={svc.LastSearchStatus}");
            if (commons.Count > 0)
            {
                Console.WriteLine($"[COMMONS-ONLY-SEARCH] FIRST_PROVIDER={commons[0].Provider} FIRST_HOST={(Uri.TryCreate(commons[0].MediaUrl,UriKind.Absolute,out var u) ? u.Host : "INVALID")}");
                using var downloadLimit = new CancellationTokenSource(TimeSpan.FromSeconds(34));
                var ds = Stopwatch.StartNew();
                try
                {
                    var saved = await svc.RetrieveAsync(commons[0].CandidateId, downloadLimit.Token);
                    bool file = File.Exists(saved.LocalPath);
                    byte[] bytes = file ? await File.ReadAllBytesAsync(saved.LocalPath) : Array.Empty<byte>();
                    bool sha = file && bytes.LongLength == saved.Bytes && Convert.ToHexString(SHA256.HashData(bytes)).Equals(saved.Sha256, StringComparison.OrdinalIgnoreCase);
                    Console.WriteLine($"[COMMONS-ONLY-RETRIEVE] {(sha && Raster(bytes) ? "PASS" : "FAIL_VALIDATION")} SAVED={file} BYTES={bytes.LongLength} HASH_OK={sha} TIME={ds.Elapsed.TotalSeconds:F2}s");
                    if (sha && Raster(bytes)) Console.WriteLine("STEP3_RESULT=COMMONS_BRANCH_FULLY_WORKS: Consider a small source-grounded provider-selection change after reviewing the result.");
                }
                catch (Exception ex) { Console.WriteLine($"[COMMONS-ONLY-RETRIEVE] FAIL TIME={ds.Elapsed.TotalSeconds:F2}s {ExceptionInfo(ex)}"); }
            }
        }
        catch (Exception ex) { Console.WriteLine("[COMMONS-ONLY-SEARCH] FAIL " + ExceptionInfo(ex)); }
        Console.WriteLine("STEP3_COMPLETE=YES (read all labeled results above)");
        return 0;
    }
}
'@
Set-Content -LiteralPath (Join-Path $probe 'Program.cs') -Value $program -Encoding UTF8
Write-Host 'NIRA STEP 3 - IMAGE RETRIEVAL ISOLATION (NO LLM / NO NIRA SOURCE CHANGES)'
Write-Host 'Comparing provider media URLs, Commons parser and independent Commons branch.'
Write-Host 'This can take up to ~90 seconds if multiple media hosts are unreachable.'
$old = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
try {
    $output = (& dotnet run --project (Join-Path $probe 'Probe.csproj') --configuration Release -- $commonsJson $Query 2>&1 | Out-String)
    $exitCode = $LASTEXITCODE
} finally { $ErrorActionPreference = $old }
$output | Set-Content -LiteralPath $report -Encoding UTF8
Write-Host $output
Write-Host "STEP3_PROCESS_EXIT=$exitCode"
Write-Host "REPORT_FILE=$report"
if ($exitCode -ne 0) { Write-Host 'Probe compilation/execution failed. Send the report; do NOT change NIRA source.' }
