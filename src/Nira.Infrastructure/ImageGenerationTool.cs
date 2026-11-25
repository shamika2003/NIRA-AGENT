using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows.Media.Imaging;
using Nira.Core;

namespace Nira.Infrastructure;

// Adapted from original NIRA FLUX.2 generation. No extra language-model inference.
public sealed class ImageGenerationProvider
{
    public const string Model = "@cf/black-forest-labs/flux-2-klein-4b";
    private static readonly Uri CredentialScope = new("https://api.cloudflare.com/nira-image-generation");
    private readonly string _account;
    private readonly string _token;
    public ImageGenerationProvider(string account, string token)
    {
        if (account.Length != 32 || !account.All(Uri.IsHexDigit) || token.Length is < 20 or > 512 ||
            token.Any(c => c < 33 || c > 126)) throw new ArgumentException("Invalid Cloudflare account ID or API token.");
        _account = account; _token = token;
    }
    public Uri Endpoint => new($"https://api.cloudflare.com/client/v4/accounts/{_account}/ai/run/{Model}");
    internal void Authenticate(HttpRequestMessage request) => request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
    public static void Save(string appRoot, string account, string token)
    {
        _ = new ImageGenerationProvider(account, token);
        new NiraModelCredentialStore(appRoot, CredentialScope).Save(account + "\n" + token);
    }
    public static void Clear(string appRoot) => new NiraModelCredentialStore(appRoot, CredentialScope).ClearStored();
    public static ImageGenerationProvider? Load(string appRoot)
    {
        string? stored = new NiraModelCredentialStore(appRoot, CredentialScope).ReadStored();
        if (stored is null) return null;
        string[] parts = stored.Split('\n');
        if (parts.Length != 2) return null;
        try { return new ImageGenerationProvider(parts[0], parts[1]); }
        catch (ArgumentException) { return null; }
    }
}

public sealed class ImageGenerationTool(WorkspacePathResolver paths, HttpClient http,
    Func<ImageGenerationProvider?> resolveProvider) : IAgentTool, IToolReadiness
{
    public ImageGenerationTool(WorkspacePathResolver paths, HttpClient http, ImageGenerationProvider provider)
        : this(paths, http, () => provider) { }

    private const string MissingCredentials = "Image generation credentials are missing or unreadable. Open Settings > Model & credentials > Image generation, save your Cloudflare account ID and API token, then retry. No image request was sent.";

    private ImageGenerationProvider? CurrentProvider()
    {
        try { return resolveProvider(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException)
        { return null; }
    }

    public string? UnavailableReason() => CurrentProvider() is null ? MissingCredentials : null;

    public const string CacheFolder = ".nira-generated-cache";
    public const int MaxBytes = 16 * 1024 * 1024;
    private const int MaxResponse = 24 * 1024 * 1024;
    public ToolDescriptor Descriptor { get; } = new("image.generate",
        "Create one new AI artwork from a text prompt via Cloudflare FLUX.2 Klein 4B. External prompt transmission and possible charges are governed by host authorization. Returns saved image evidence. Optional animation=true creates a four-frame looping GIF from one generated 2x2 sprite sheet. Not video or reference-image editing.",
        ToolRisk.ReadExternal, ToolSchemas.New("""
        {"type":"object","properties":{"prompt":{"type":"string","minLength":4,"maxLength":2400},"width":{"type":"integer"},"height":{"type":"integer"},"animation":{"type":"boolean"},"frameDurationMs":{"type":"integer"}},"required":["prompt","width","height"],"additionalProperties":false}
        """), HasSideEffects: true);

    private static (string Prompt, int Width, int Height, bool Animated, int Delay) Validate(JsonElement arguments)
    {
        string prompt = arguments.GetProperty("prompt").GetString()!.Trim();
        if (prompt.Length is < 4 or > 2400 || prompt.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')) ||
            !arguments.GetProperty("width").TryGetInt32(out int w) || !arguments.GetProperty("height").TryGetInt32(out int h) ||
            w is < 256 or > 1920 || h is < 256 or > 1920 || w % 8 != 0 || h % 8 != 0)
            throw new ArgumentException("Use a 4–2400 character prompt and dimensions 256–1920 divisible by eight.");
        bool animated = arguments.TryGetProperty("animation", out var animation) && animation.GetBoolean();
        int delay = 300;
        if (arguments.TryGetProperty("frameDurationMs", out var timing) &&
            (!animated || !timing.TryGetInt32(out delay) || delay is < 100 or > 2000 || delay % 10 != 0))
            throw new ArgumentException("Animation delay must be 100–2000 ms in multiples of ten.");
        if (animated && (w % 16 != 0 || h % 16 != 0 || w < 512 || h < 512))
            throw new ArgumentException("Animated sprite sheet dimensions must be 512–1920 and divisible by sixteen.");
        return (prompt, w, h, animated, delay);
    }
    public string GetTarget(JsonElement arguments)
    {
        var options = Validate(arguments);
        return "https://api.cloudflare.com — Workers AI image generation (prompt sent externally; quota or charges may apply)" +
            (options.Animated ? " — one 2×2 sprite sheet; local four-frame GIF assembly" : "");
    }

    public async Task<ToolReceipt> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var (prompt, width, height, animated, delay) = Validate(arguments);
        // Resolve again after consent; saving or clearing credentials takes effect immediately.
        ImageGenerationProvider? provider = CurrentProvider();
        if (provider is null) return ToolReceipt.Fail("IMAGE_CREDENTIALS_MISSING", MissingCredentials);
        string? partial = null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(110));
        CancellationToken token = deadline.Token;
        try
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(animated ? NiraGifComposer.SpritePrompt(prompt) : prompt, Encoding.UTF8), "prompt");
            form.Add(new StringContent(width.ToString(CultureInfo.InvariantCulture)), "width");
            form.Add(new StringContent(height.ToString(CultureInfo.InvariantCulture)), "height");
            using var request = new HttpRequestMessage(HttpMethod.Post, provider.Endpoint) { Content = form };
            provider.Authenticate(request);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode)
                return ToolReceipt.Fail("IMAGE_GENERATION_HTTP", $"Image service returned HTTP {(int)response.StatusCode}. Check access or quota. No image saved; do not automatically retry.");
            await using var source = await response.Content.ReadAsStreamAsync(token);
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[65536];
            while (true)
            {
                int count = await source.ReadAsync(chunk.AsMemory(), token);
                if (count == 0) break;
                if (buffer.Length + count > MaxResponse) throw new IOException("Image response exceeded limit.");
                buffer.Write(chunk, 0, count);
            }
            byte[] image = ReadImage(response.Content.Headers.ContentType?.MediaType ?? "", buffer.ToArray());
            var decoded = await DecodeAsync(image, animated, delay, token);
            token.ThrowIfCancellationRequested();
            string folder = paths.Resolve(CacheFolder);
            Directory.CreateDirectory(folder);
            string path = paths.Resolve(Path.Combine(folder, Guid.NewGuid().ToString("N") + (animated ? ".gif" : ".png")));
            partial = path + ".partial";
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await output.WriteAsync(decoded.Bytes, token);
                output.Flush(flushToDisk: true);
            }
            token.ThrowIfCancellationRequested();
            File.Move(partial, path); partial = null;
            return ToolReceipt.Ok(animated ? "Four-frame AI animation created and saved locally." : "AI artwork created and saved locally.", new { path, sha256 = Convert.ToHexString(SHA256.HashData(decoded.Bytes)),
                model = ImageGenerationProvider.Model, title = prompt.Length > 120 ? prompt[..120] : prompt,
                width = decoded.Width, height = decoded.Height, bytes = decoded.Bytes.Length, generated = true, animated, frameCount = animated ? 4 : 1, frameDurationMs = animated ? delay : 0 }, changedState: true);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or IOException or JsonException or FormatException or InvalidOperationException or ArgumentException or NotSupportedException or UnauthorizedAccessException or OperationCanceledException or System.Runtime.InteropServices.COMException)
        { return ToolReceipt.Fail("IMAGE_GENERATION_UNAVAILABLE", "No verified artwork was saved. The request may have consumed quota; do not automatically retry."); }
        finally { if (partial is not null && File.Exists(partial)) File.Delete(partial); }
    }

    // Original provider envelope handling; never follow URLs or accept executable formats.
    public static byte[] ReadImage(string contentType, byte[] body)
    {
        if (body.Length is < 8 or > MaxResponse) throw new IOException("Invalid image response size.");
        byte[] image = body;
        if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase) || body[0] == (byte)'{')
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True ||
                !root.TryGetProperty("result", out var result) || !result.TryGetProperty("image", out var encoded) || encoded.ValueKind != JsonValueKind.String)
                throw new IOException("No successful image payload.");
            string value = encoded.GetString()!;
            int comma = value.IndexOf(',');
            if (value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) && comma > 0)
                value = value[(comma + 1)..];
            if (value.Length > MaxBytes * 4 / 3 + 4096) throw new IOException("Encoded image exceeds limit.");
            image = Convert.FromBase64String(value);
        }
        if (image.Length is < 8 or > MaxBytes || !(image.AsSpan().StartsWith(new byte[] {137,80,78,71,13,10,26,10}) || image.AsSpan().StartsWith(new byte[] {255,216,255})))
            throw new IOException("Expected bounded PNG or JPEG.");
        return image;
    }

    private static Task<(byte[] Bytes, int Width, int Height)> DecodeAsync(byte[] bytes, bool animated, int delay, CancellationToken ct)
    {
        var completion = new TaskCompletionSource<(byte[], int, int)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                using var source = new MemoryStream(bytes);
                var decoder = BitmapDecoder.Create(source, BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnDemand);
                var frame = decoder.Frames[0];
                int w = frame.PixelWidth, h = frame.PixelHeight;
                if (w is < 1 or > 1920 || h is < 1 or > 1920) throw new IOException("Image dimensions exceed limit.");
                if (animated)
                {
                    byte[] gif = NiraGifComposer.Compose(frame, delay, ct);
                    completion.TrySetResult((gif, w / 2, h / 2));
                    return;
                }
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(frame));
                using var output = new MemoryStream(); encoder.Save(output);
                if (output.Length > MaxBytes) throw new IOException("Encoded PNG exceeds limit.");
                completion.TrySetResult((output.ToArray(), w, h));
            }
            catch (Exception error) { completion.TrySetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return completion.Task.WaitAsync(ct);
    }
}
