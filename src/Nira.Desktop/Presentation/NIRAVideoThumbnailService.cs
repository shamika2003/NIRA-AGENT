using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nira.Core;
using Microsoft.Web.WebView2.Core;

namespace NIRAAgent.UI.Presentation;

// The old NIRA poster strategy: eager loading, two official CDNs and shared cache.
// Only validated video IDs can generate URLs; no iframe is started by a poster.
public static class NIRAVideoThumbnailService
{
    public static bool DisableNetworkForOfflineAcceptance { get; set; }
    private const int MaxBytes = 2_400_000;
    private static readonly HttpClient Client = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false, UseCookies = false,
        AutomaticDecompression = System.Net.DecompressionMethods.All,
        ConnectTimeout = TimeSpan.FromSeconds(4), MaxConnectionsPerServer = 4
    }) { Timeout = TimeSpan.FromSeconds(9) };
    private static readonly ConcurrentDictionary<string, Lazy<Task<byte[]?>>> Pending = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, byte[]> Cache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, DateTime> FailedUntil = new(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<string, List<WeakReference<Image>>> Subscribers = new(StringComparer.Ordinal);

    internal static bool ValidId(string id) => id.Length == 11 &&
        id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    // The old NIRA browser-network recovery, limited to this deck's validated IDs.
    // The browser requests only official poster URLs, never additional video streams.
    internal static string PosterMarkup(IEnumerable<string> ids) => string.Join("\n", ids
        .Where(ValidId).Distinct(StringComparer.Ordinal).Take(6)
        .SelectMany(id => new[] { "i.ytimg.com", "img.youtube.com" }.Select(host =>
            $"<img loading=\"eager\" src=\"https://{host}/vi/{id}/hqdefault.jpg\" alt=\"\">")));

    internal static bool TryPosterId(string address, IReadOnlySet<string> allowed, out string id)
    {
        id = "";
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !uri.IsDefaultPort || uri.UserInfo.Length != 0 ||
            uri.Host is not ("i.ytimg.com" or "img.youtube.com")) return false;
        var parts = uri.AbsolutePath.Split('/');
        if (parts.Length != 4 || parts[1] != "vi" || !ValidId(parts[2]) ||
            parts[3] is not ("hqdefault.jpg" or "mqdefault.jpg") || !allowed.Contains(parts[2])) return false;
        id = parts[2]; return true;
    }

    internal static void ObserveBrowser(CoreWebView2 core, IEnumerable<string> ids)
    {
        var allowed = ids.Where(ValidId).ToHashSet(StringComparer.Ordinal);
        core.WebResourceResponseReceived += async (_, e) =>
        {
            try
            {
                if (!TryPosterId(e.Request.Uri, allowed, out string id) || Cache.ContainsKey(id) ||
                    e.Response.StatusCode != 200 || !e.Response.Headers.Contains("Content-Type") ||
                    !e.Response.Headers.GetHeader("Content-Type").StartsWith("image/jpeg", StringComparison.OrdinalIgnoreCase)) return;
                using var stream = await e.Response.GetContentAsync();
                if (stream is null) return;
                using var output = new MemoryStream();
                byte[] buffer = new byte[16384]; int n;
                while ((n = await stream.ReadAsync(buffer.AsMemory())) > 0)
                { if (output.Length + n > MaxBytes) return; output.Write(buffer, 0, n); }
                Publish(id, output.ToArray());
            }
            catch (Exception error) { System.Diagnostics.Debug.WriteLine("NIRA browser poster: " + error.GetType().Name); }
        };
    }

    private static void Publish(string id, byte[] bytes)
    {
        var bitmap = DecodePoster(bytes);
        if (Cache.Count >= 64) Cache.Clear();
        Cache[id] = bytes;
        FailedUntil.TryRemove(id, out _);
        if (!Subscribers.TryGetValue(id, out var watchers)) return;
        lock (watchers)
        {
            watchers.RemoveAll(w => !w.TryGetTarget(out _));
            foreach (var weak in watchers)
                if (weak.TryGetTarget(out var image) && !image.Dispatcher.HasShutdownStarted)
                    image.Dispatcher.BeginInvoke(new Action(() => { image.Source = bitmap; image.Opacity = 1; }));
        }
    }

    public static void Attach(Grid preview, NIRAVideoReference video)
    {
        if (DisableNetworkForOfflineAcceptance) return;
        AttachCore(preview, video, GetAsync);
        if (!ValidId(video.VideoId) || video.Provider != "YouTube" || preview.Children.OfType<Image>().FirstOrDefault() is not Image image) return;
        if (Subscribers.Count >= 128)
            foreach (var entry in Subscribers)
                lock (entry.Value)
                {
                    entry.Value.RemoveAll(w => !w.TryGetTarget(out _));
                    if (entry.Value.Count == 0) Subscribers.TryRemove(entry.Key, out _);
                }
        var watchers = Subscribers.GetOrAdd(video.VideoId, _ => new List<WeakReference<Image>>());
        lock (watchers)
        {
            watchers.RemoveAll(w => !w.TryGetTarget(out _));
            if (watchers.Count >= 96) watchers.RemoveAt(0);
            watchers.Add(new WeakReference<Image>(image));
        }
        if (Cache.TryGetValue(video.VideoId, out var cached))
        {
            try { image.Source = DecodePoster(cached); image.Opacity = 1; }
            catch (Exception error) { System.Diagnostics.Debug.WriteLine(error); }
        }
    }

    // Injectable loader for offline acceptance; production uses only fixed official CDNs.
    private static void AttachCore(Grid preview, NIRAVideoReference video, Func<string, Task<byte[]?>> loader)
    {
        if (video.Provider != "YouTube" || video.VideoId.Length != 11 ||
            video.VideoId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_')) return;
        var image = new Image
        {
            Stretch = Stretch.UniformToFill, IsHitTestVisible = false, Opacity = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
            RenderTransform = new ScaleTransform(1.025, 1.10), RenderTransformOrigin = new Point(.5, .5)
        };
        // Above the placeholder glyph and behind the gradient.
        preview.Children.Insert(Math.Min(1, preview.Children.Count), image);
        bool loading = false;
        async Task PopulateAsync()
        {
            if (loading || image.Source is not null) return;
            loading = true;
            try
            {
                var bytes = await loader(video.VideoId);
                if (bytes is null || image.Dispatcher.HasShutdownStarted || image.Dispatcher.HasShutdownFinished) return;
                var bitmap = DecodePoster(bytes);
                image.Source = bitmap;
                image.Opacity = 1;
            }
            catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException or NotSupportedException or System.Runtime.InteropServices.COMException)
            { System.Diagnostics.Debug.WriteLine("NIRA video poster: " + error.GetType().Name); }
            finally { loading = false; }
        }
        // Do not depend on Loaded firing on a collapsed child. Do not discard a
        // valid result if the visual has not yet been mounted or is offstage.
        preview.Loaded += async (_, _) => await PopulateAsync();
        _ = PopulateAsync();
    }

    private static BitmapSource DecodePoster(byte[] bytes)
    {
        if (bytes.Length is < 4 or > MaxBytes || bytes[0] != 255 || bytes[1] != 216 || bytes[2] != 255)
            throw new IOException("Invalid poster JPEG.");
        using var headerStream = new MemoryStream(bytes, writable: false);
        var decoder = new JpegBitmapDecoder(headerStream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnDemand);
        var frame = decoder.Frames[0];
        if (frame.PixelWidth is < 1 or > 1920 || frame.PixelHeight is < 1 or > 1080)
            throw new IOException("Poster dimensions exceed limits.");
        using var bitmapStream = new MemoryStream(bytes, writable: false);
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = bitmapStream; bitmap.DecodePixelWidth = Math.Min(640, frame.PixelWidth); bitmap.EndInit(); bitmap.Freeze();
        return bitmap;
    }

    private static async Task<byte[]?> GetAsync(string id)
    {
        if (Cache.TryGetValue(id, out var bytes)) return bytes;
        if (FailedUntil.TryGetValue(id, out var expires) && expires > DateTime.UtcNow) return null;
        var pending = Pending.GetOrAdd(id, key => new Lazy<Task<byte[]?>>(() => FetchAsync(key)));
        try { return await pending.Value.ConfigureAwait(false); }
        finally { Pending.TryRemove(new KeyValuePair<string, Lazy<Task<byte[]?>>>(id, pending)); }
    }

    private static async Task<byte[]?> FetchAsync(string id)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(9));
        // Restore both original CDN endpoints; first valid JPEG wins. If a high
        // quality variant is unavailable, try the smaller official poster too.
        foreach (string variant in new[] { "hqdefault.jpg", "mqdefault.jpg" })
        {
            var requests = new[] { "i.ytimg.com", "img.youtube.com" }.Select(host =>
                FetchEndpointAsync(new Uri($"https://{host}/vi/{id}/{variant}"), timeout.Token)).ToList();
            while (requests.Count > 0)
            {
                var finished = await Task.WhenAny(requests).ConfigureAwait(false); requests.Remove(finished);
                var bytes = await finished.ConfigureAwait(false);
                if (bytes is null) continue;
                // Validate the compressed image before placing it in the shared cache.
                try { Publish(id, bytes); }
                catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException or NotSupportedException or System.Runtime.InteropServices.COMException)
                { continue; }
                timeout.Cancel();
                await Task.WhenAll(requests).ConfigureAwait(false);
                return bytes;
            }
            if (timeout.IsCancellationRequested) break;
        }
        if (FailedUntil.Count >= 128) FailedUntil.Clear();
        if (Cache.TryGetValue(id, out var recovered)) return recovered;
        FailedUntil[id] = DateTime.UtcNow.AddSeconds(20);
        System.Diagnostics.Debug.WriteLine("NIRA video poster: official CDN unavailable.");
        return null;
    }

    private static async Task<byte[]?> FetchEndpointAsync(Uri url, CancellationToken token)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("NIRA/1.0");
            request.Headers.Accept.ParseAdd("image/jpeg");
            using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxBytes) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var output = new MemoryStream(); byte[] buffer = new byte[16384]; int n;
            while ((n = await stream.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
            { if (output.Length + n > MaxBytes) return null; output.Write(buffer, 0, n); }
            byte[] bytes = output.ToArray();
            return bytes.Length >= 4 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255 ? bytes : null;
        }
        catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException) { return null; }
    }
}
