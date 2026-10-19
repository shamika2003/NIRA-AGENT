using System.Diagnostics;
using NIRAAgent.Images;

// Standalone end-to-end real image search/retrieve using production service.
// NO Ollama client, no NIRAExecutive, no LLM call, no browser, no API key.
string query = string.Join(' ', args).Trim();
if (query.Length == 0) query = "Great Pyramid of Giza";
if (query.Length > 160)
{
    Console.Error.WriteLine("Query too long (160 characters max).");
    return 2;
}
Console.WriteLine("NIRA IMAGE LAB / R31");
Console.WriteLine("Cloud LLM calls: ZERO. Tests NIRA's real image.search and image.retrieve service.");
Console.WriteLine($"Search query: {query}");
using var imageService = new NIRACommonsImageDiscoveryService();
var clock = Stopwatch.StartNew();
try
{
    IReadOnlyList<NIRACommonsImageCandidate> candidates =
        await imageService.SearchAsync(query, 6);
    Console.WriteLine($"SEARCH: {(candidates.Count > 0 ? "PASS" : "NO RESULTS")}, " +
                      $"candidates={candidates.Count}, elapsed={clock.Elapsed.TotalSeconds:F1}s");
    Console.WriteLine($"SEARCH DETAIL: {imageService.LastSearchStatus}");
    foreach (var item in candidates)
        Console.WriteLine($"  - {item.Provider} | {item.Title} | CDN={new Uri(item.MediaUrl).Host} " +
                          $"| Creator={(item.Author.Length == 0 ? "See source" : item.Author)}");
    if (candidates.Count == 0) return 3;
    clock.Restart();
    NIRACommonsImageDownload downloaded =
        await imageService.RetrieveAsync(candidates[0].CandidateId);
    if (!File.Exists(downloaded.LocalPath) || new FileInfo(downloaded.LocalPath).Length != downloaded.Bytes)
        throw new IOException("Successful result did not match actual cached file bytes.");
    Console.WriteLine($"RETRIEVE: PASS, bytes={downloaded.Bytes}, elapsed={clock.Elapsed.TotalSeconds:F1}s");
    Console.WriteLine($"File: {downloaded.LocalPath}");
    Console.WriteLine($"Title: {downloaded.Title}");
    Console.WriteLine($"Attribution: {downloaded.Caption}");
    Console.WriteLine($"Source: {downloaded.SourceUri}");
    Console.WriteLine("NEXT: NIRA can display these real bytes through its existing verified-artifact gallery.");
    return 0;
}
catch (Exception exception)
{
    Console.WriteLine($"FAILED after {clock.Elapsed.TotalSeconds:F1}s: {exception.GetType().Name}");
    Console.WriteLine(exception.Message);
    Console.WriteLine($"SEARCH DETAIL: {imageService.LastSearchStatus}");
    for (Exception? inner = exception.InnerException; inner != null; inner = inner.InnerException)
        Console.WriteLine($"  Cause: {inner.GetType().Name}: {inner.Message}");
    Console.WriteLine("NO MODEL CALLS WERE MADE. Send this output to diagnose the exact failed stage.");
    return 1;
}
