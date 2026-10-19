using System.Text;
using NIRAAgent.Images;

Console.WriteLine("NIRA R37 IMAGE GENERATION LAB — ZERO Ollama / LLM CALLS");
Console.WriteLine("Model: " + NIRACloudflareImageGenerationService.ModelId);
Console.WriteLine("Cloudflare configured: " + (NIRACloudflareImageGenerationService.IsConfigured ? "YES" : "NO"));
var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/BGAAAAAASUVORK5CYII=");
var wrapped = Encoding.UTF8.GetBytes("{\"success\":true,\"result\":{\"image\":\"" + Convert.ToBase64String(png) + "\"}}");
bool validated = NIRACloudflareImageGenerationService.ReadVerifiedImage("application/json", wrapped).SequenceEqual(png);
Console.WriteLine("OFFLINE IMAGE DECODER: " + (validated ? "PASS" : "FAIL"));
if (!validated) return 2;
if (args.Contains("--setup", StringComparer.OrdinalIgnoreCase))
{
    Console.Write("Cloudflare Account ID (32 hex characters): ");
    string accountId = (Console.ReadLine() ?? "").Trim();
    Console.Write("Cloudflare Workers AI API token (hidden): ");
    var secretChars = new List<char>();
    while (true)
    {
        ConsoleKeyInfo key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter) break;
        if (key.Key == ConsoleKey.Backspace) { if (secretChars.Count > 0) secretChars.RemoveAt(secretChars.Count - 1); continue; }
        if (!char.IsControl(key.KeyChar) && secretChars.Count < 2500) secretChars.Add(key.KeyChar);
    }
    Console.WriteLine();
    try
    {
        NIRACloudflareImageGenerationService.SaveCredentials(accountId, new string(secretChars.ToArray()));
        Console.WriteLine("SETUP: PASS — Cloudflare token saved in Windows Credential Manager (no images generated).");
        return 0;
    }
    catch (Exception ex)
    {
        Console.WriteLine("SETUP: FAILED " + ex.Message);
        return 2;
    }
    finally { secretChars.Clear(); }
}
if (!args.Contains("--live", StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine("NO LIVE REQUEST MADE. To spend quota on ONE real image, run:");
    Console.WriteLine("dotnet run --project .\\NIRAAgent.ImageGenerationLab -- --live");
    return 0;
}
if (!NIRACloudflareImageGenerationService.IsConfigured)
{
    Console.WriteLine("Run: dotnet run --project .\\NIRAAgent.ImageGenerationLab -- --setup first.");
    return 3;
}
Console.Write("One Cloudflare generation will consume quota. Type GENERATE to continue: ");
if (Console.ReadLine() != "GENERATE") { Console.WriteLine("Cancelled. Zero API requests."); return 0; }
try
{
    using var service = new NIRACloudflareImageGenerationService();
    var result = await service.GenerateAsync(new NIRAImageGenerationRequest(
        "A cinematic ancient Egyptian temple at sunrise, warm sandstone, monumental columns, intricate hieroglyphics, realistic architectural photography, natural atmospheric lighting, no words or watermarks", 768, 768));
    Console.WriteLine($"GENERATE: PASS | {result.Bytes} bytes | {result.Width}x{result.Height}");
    Console.WriteLine("File: " + result.LocalPath);
    Console.WriteLine("SHA256: " + result.Sha256);
    return 0;
}
catch (Exception ex)
{
    Console.WriteLine("GENERATE: FAILED | " + ex.GetType().Name + " | " + ex.Message);
    Console.WriteLine("No automatic retries or further LLM calls.");
    return 1;
}
