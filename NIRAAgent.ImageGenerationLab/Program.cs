using System.Text;
using System.Runtime.InteropServices;
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
    Console.WriteLine("Paste the raw Workers AI API TOKEN (not its token ID). Ctrl+V, Shift+Insert, or right-click works.");
    Console.WriteLine("Each character appears as *; token text itself stays hidden. Press Enter to finish, Esc to cancel.");
    try
    {
        string token = NIRACloudflareImageGenerationService.NormalizePastedToken(MaskedTokenInput.Read());
        Console.WriteLine($"Token received: {token.Length} characters. Checking with Cloudflare (NO image generation)...");
        NIRACloudflareAuthCheck check = await NIRACloudflareImageGenerationService.CheckCredentialsAsync(accountId, token);
        Console.WriteLine("AUTH: " + (check.Success ? "PASS" : "FAILED") + " — " + check.Message);
        if (!check.Success) { Console.WriteLine("Stored credentials were NOT changed."); return 2; }
        NIRACloudflareImageGenerationService.SaveCredentials(accountId, token);
        Console.WriteLine("SETUP: PASS — Verified token saved in Windows Credential Manager. Zero images generated.");
        return 0;
    }
    catch (OperationCanceledException) { Console.WriteLine("SETUP: Cancelled. Credentials unchanged."); return 0; }
    catch (Exception ex) { Console.WriteLine("SETUP: FAILED — " + ex.Message); return 2; }
}
if (args.Contains("--check-auth", StringComparer.OrdinalIgnoreCase))
{
    try
    {
        Console.WriteLine("Checking saved token and Workers AI account access (read-only request, NO generation)...");
        var check = await NIRACloudflareImageGenerationService.CheckStoredCredentialsAsync();
        Console.WriteLine("AUTH: " + (check.Success ? "PASS" : "FAILED") + " — " + check.Message);
        return check.Success ? 0 : 2;
    }
    catch (Exception ex) { Console.WriteLine("AUTH: FAILED — " + ex.GetType().Name + ". Re-run --setup."); return 2; }
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
// Do not offer a quota-consuming operation unless the stored credential has
// already succeeded against the account's read-only Workers AI API.
try
{
    var auth = await NIRACloudflareImageGenerationService.CheckStoredCredentialsAsync();
    Console.WriteLine("AUTH: " + (auth.Success ? "PASS" : "FAILED") + " — " + auth.Message);
    if (!auth.Success) { Console.WriteLine("ZERO image generation calls. Re-run --setup."); return 2; }
}
catch (Exception ex) { Console.WriteLine("AUTH: FAILED — " + ex.GetType().Name + ". No image generated."); return 2; }
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

// Windows console paste support with a visible mask, without echoing the secret.
// Console.ReadKey(intercept: true) alone does NOT reliably handle Ctrl+V.
internal static class MaskedTokenInput
{
    private const uint CfUnicodeText = 13;
    [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool CloseClipboard();
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetClipboardData(uint format);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalLock(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalUnlock(IntPtr handle);

    private static string? PasteClipboard()
    {
        if (!OperatingSystem.IsWindows()) return null;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (!OpenClipboard(IntPtr.Zero)) { Thread.Sleep(30); continue; }
            try
            {
                IntPtr handle = GetClipboardData(CfUnicodeText);
                if (handle == IntPtr.Zero) return null;
                IntPtr ptr = GlobalLock(handle);
                if (ptr == IntPtr.Zero) return null;
                try { return Marshal.PtrToStringUni(ptr); }
                finally { GlobalUnlock(handle); }
            }
            finally { CloseClipboard(); }
        }
        return null;
    }

    public static string Read()
    {
        if (Console.IsInputRedirected)
            throw new InvalidOperationException("Use an interactive PowerShell window. Never pass tokens as command arguments.");
        Console.Write("API token [hidden]: ");
        var chars = new List<char>(128);
        void Append(string text)
        {
            string clean = text.Trim();
            foreach (char c in clean)
            {
                if (chars.Count >= 512) break;
                if (c >= 32 && c <= 126) { chars.Add(c); Console.Write('*'); }
            }
        }
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Escape) { Console.WriteLine(); throw new OperationCanceledException(); }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (chars.Count != 0) { chars.RemoveAt(chars.Count - 1); Console.Write("\b \b"); }
                continue;
            }
            if ((key.Key == ConsoleKey.V && key.Modifiers.HasFlag(ConsoleModifiers.Control)) ||
                (key.Key == ConsoleKey.Insert && key.Modifiers.HasFlag(ConsoleModifiers.Shift)))
            {
                string? copied = PasteClipboard();
                if (copied is null) Console.Write(" [Clipboard unavailable; try right-click paste] ");
                else Append(copied);
                continue;
            }
            if (key.Key == ConsoleKey.U && key.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                while (chars.Count > 0) { chars.RemoveAt(chars.Count - 1); Console.Write("\b \b"); }
                continue;
            }
            if (!char.IsControl(key.KeyChar)) Append(key.KeyChar.ToString());
        }
        Console.WriteLine();
        return new string(chars.ToArray());
    }
}
