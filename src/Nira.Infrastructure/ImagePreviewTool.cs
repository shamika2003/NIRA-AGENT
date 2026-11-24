using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Windows.Media.Imaging;
using Nira.Core;

namespace Nira.Infrastructure;

/// <summary>Approved, receipt-bound public image downloads. Only bounded local raster previews are rendered.</summary>
public sealed class ImagePreviewTool(WorkspacePathResolver paths) : IAgentTool
{
    public const int MaxBytes = 8 * 1024 * 1024;
    public const string CacheFolder = ".nira-image-cache";
    public ToolDescriptor Descriptor { get; } = new("image.retrieve",
        "Retrieve and display up to six image previews using exact ThumbnailUrl (preferred) or MediaUrl from this task's successful image.search results. Requires network approval. Not authenticity or license verification.",
        ToolRisk.ReadExternal, ToolSchemas.New("""
        {"type":"object","properties":{"urls":{"type":"array","items":{"type":"string","minLength":12,"maxLength":2048}}},"required":["urls"],"additionalProperties":false}
        """));
    private static string[] Urls(JsonElement args)
    {
        var array = args.GetProperty("urls");
        if (array.GetArrayLength() is < 1 or > 6) throw new ArgumentException("Select one to six images.");
        return array.EnumerateArray().Select(x => x.GetString()!).Distinct(StringComparer.Ordinal).ToArray();
    }
    public string GetTarget(JsonElement arguments) => string.Join("\n", Urls(arguments).Select(url => PublicBrowserUrlPolicy.Parse(url).AbsoluteUri));

    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        IPAddress[] addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
        PublicBrowserUrlPolicy.RequirePublicDns(addresses);
        // Connect to the validated address itself, not a second DNS resolution.
        var address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
            ?? throw new HttpRequestException("No public IPv4 address available for this preview.");
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try { await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), ct); return new NetworkStream(socket, ownsSocket: true); }
        catch { socket.Dispose(); throw; }
    }

    public async Task<ToolReceipt> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        _ = GetTarget(arguments);
        using var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false, ConnectCallback = ConnectAsync };
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var previews = new List<object>(); int failed = 0;
        foreach (string url in Urls(arguments))
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, PublicBrowserUrlPolicy.Parse(url));
                request.Headers.UserAgent.ParseAdd("NIRA/1.0 (ELVARA desktop image preview)");
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxBytes ||
                    response.Content.Headers.ContentType?.MediaType is not ("image/jpeg" or "image/png" or "image/gif")) { failed++; continue; }
                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                using var buffer = new MemoryStream(); byte[] chunk = new byte[16384]; int n;
                while ((n = await stream.ReadAsync(chunk.AsMemory(), timeout.Token)) != 0)
                {
                    if (buffer.Length + n > MaxBytes) throw new IOException("Image exceeds preview limit.");
                    buffer.Write(chunk, 0, n);
                }
                byte[] bytes = buffer.ToArray();
                // Decode on a dedicated STA thread; do not depend on the agent's continuation apartment.
                byte[] preview = await DecodeAsync(bytes, timeout.Token);
                string folder = paths.Resolve(CacheFolder); Directory.CreateDirectory(folder);
                string path = paths.Resolve(Path.Combine(folder, Guid.NewGuid().ToString("N") + ".png"));
                string temp = path + ".tmp";
                try
                {
                    await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        await output.WriteAsync(preview, timeout.Token);
                    timeout.Token.ThrowIfCancellationRequested();
                    File.Move(temp, path);
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
                previews.Add(new { url, path, sha256 = Convert.ToHexString(SHA256.HashData(preview)) });
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or IOException or InvalidOperationException or ArgumentException or NotSupportedException or UnauthorizedAccessException or OperationCanceledException or System.Runtime.InteropServices.COMException)
            { failed++; }
        }
        return previews.Count == 0 ? ToolReceipt.Fail("IMAGE_PREVIEW_UNAVAILABLE", "No image preview could be opened. Source links may still be useful.") :
            ToolReceipt.Ok($"{previews.Count} image preview(s) retrieved; {failed} unavailable. Content identity and rights not independently verified.", new { previews, failed, visualIdentityVerified = false });
    }

    private static Task<byte[]> DecodeAsync(byte[] bytes, CancellationToken ct)
    {
        var completion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                // Only JPEG/PNG/GIF signatures; no SVG, HTML, or arbitrary installed codecs.
                bool jpeg = bytes.Length > 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255;
                bool png = bytes.AsSpan().StartsWith(new byte[] {137,80,78,71,13,10,26,10});
                bool gif = bytes.AsSpan().StartsWith("GIF87a"u8) || bytes.AsSpan().StartsWith("GIF89a"u8);
                if (!jpeg && !png && !gif) throw new IOException("Unsupported raster format.");
                using var source = new MemoryStream(bytes);
                var decoder = BitmapDecoder.Create(source, BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnDemand);
                var frame = decoder.Frames[0];
                if (frame.PixelWidth < 1 || frame.PixelHeight < 1 || frame.PixelWidth > 10000 || frame.PixelHeight > 10000 ||
                    (long)frame.PixelWidth * frame.PixelHeight > 24_000_000) throw new IOException("Image dimensions exceed preview limit.");
                source.Position = 0;
                var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = source;
                if (frame.PixelWidth >= frame.PixelHeight) image.DecodePixelWidth = Math.Min(1200, frame.PixelWidth);
                else image.DecodePixelHeight = Math.Min(1200, frame.PixelHeight);
                image.EndInit();
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                using var output = new MemoryStream(); encoder.Save(output);
                if (output.Length > MaxBytes) throw new IOException("Preview exceeds limit.");
                completion.TrySetResult(output.ToArray());
            }
            catch (Exception ex) { completion.TrySetException(ex); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return completion.Task.WaitAsync(ct);
    }
}
