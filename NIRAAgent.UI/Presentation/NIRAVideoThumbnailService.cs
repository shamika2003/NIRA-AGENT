using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using NIRAAgent.Diagnostics;
using NIRAAgent.Presentation;

namespace NIRAAgent.UI.Presentation;

/// <summary>
/// Official YouTube thumbnails only (validated video IDs). Loads cached posters
/// first, tries both image hosts quickly, then accepts successful responses from
/// NIRA's existing WebView2 browser session. No image search or video downloading.
/// </summary>
public static class NIRAVideoThumbnailService
{
    private const int MaxPosterBytes = 2_400_000;
    private static readonly string CacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NIRAAgent", "youtube-posters");
    private static readonly HttpClient Client = new(new SocketsHttpHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All,
        ConnectTimeout = TimeSpan.FromSeconds(2),
        MaxConnectionsPerServer = 8
    }) { Timeout = TimeSpan.FromSeconds(4) };
    private static readonly ConcurrentDictionary<string, Lazy<Task<byte[]?>>> Loads = new();
    private static readonly ConcurrentDictionary<string, byte[]> Memory = new();
    private static readonly ConcurrentDictionary<string, DateTime> FailedUntil = new();
    private static readonly ConcurrentDictionary<string, List<WeakReference<Image>>> Subscribers = new();
    private static readonly Regex UrlId = new(@"/vi(?:_webp)?/([A-Za-z0-9_-]{11})/", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static void Attach(Grid preview, NIRAVideoReference video)
    {
        if (video.Provider != "YouTube" || !ValidId(video.VideoId)) return;
        var image = new Image
        {
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            // Some official poster variants contain encoded top/bottom matte.
            // Slight overscan trims it while the existing parent clips to 16:9.
            RenderTransform = new ScaleTransform(1.025, 1.10),
            RenderTransformOrigin = new Point(0.5, 0.5),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };
        // Put official artwork above the low-detail glyph, underneath controls.
        preview.Children.Insert(Math.Min(1, preview.Children.Count), image);
        var watchers = Subscribers.GetOrAdd(video.VideoId, _ => new List<WeakReference<Image>>());
        lock (watchers)
        {
            if (watchers.Count > 36)
                watchers.RemoveAll(w => !w.TryGetTarget(out var t) || !t.IsLoaded);
            watchers.Add(new WeakReference<Image>(image));
        }
        if (Memory.TryGetValue(video.VideoId, out var cached))
        {
            Show(image, cached);
            return;
        }
        _ = PopulateAsync(image, video.VideoId);
    }

    private static bool ValidId(string id) => id.Length == 11 &&
        id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_');

    private static async Task PopulateAsync(Image target, string id)
    {
        try
        {
            if (Memory.TryGetValue(id, out var bytes)) { Show(target, bytes); return; }
            // Local cache is available instantly on subsequent NIRA launches.
            string file = Path.Combine(CacheDirectory, id + ".jpg");
            if (File.Exists(file))
            {
                var data = await File.ReadAllBytesAsync(file).ConfigureAwait(false);
                if (data.Length is > 400 and <= MaxPosterBytes)
                {
                    Publish(id, data, "DiskCache");
                    return;
                }
            }
            if (FailedUntil.TryGetValue(id, out var expires) && DateTime.UtcNow < expires)
                return;
            var lazy = Loads.GetOrAdd(id, key =>
                new Lazy<Task<byte[]?>>(async () => await FetchFastAsync(key).ConfigureAwait(false)));
            var fetched = await lazy.Value.ConfigureAwait(false);
            if (fetched != null)
                Publish(id, fetched, "Http");
            else
                FailedUntil[id] = DateTime.UtcNow.AddMinutes(2);
            Loads.TryRemove(id, out _);
        }
        catch (Exception ex)
        {
            Loads.TryRemove(id, out _);
            NIRADiagnosticTrace.Event("VIDEO-THUMBNAIL", "Stage=Unavailable Kind=" + ex.GetType().Name);
        }
    }

    private static async Task<byte[]?> FetchFastAsync(string id)
    {
        // Parallel short probes: no sequential 9s * four-file retries.
        var urls = new[]
        {
            $"https://i.ytimg.com/vi/{id}/hqdefault.jpg",
            $"https://img.youtube.com/vi/{id}/hqdefault.jpg"
        };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var tasks = urls.Select(url => FetchEndpoint(url, cts.Token)).ToList();
        while (tasks.Count > 0)
        {
            var completed = await Task.WhenAny(tasks).ConfigureAwait(false);
            tasks.Remove(completed);
            byte[]? bytes = await completed.ConfigureAwait(false);
            if (bytes != null)
            {
                cts.Cancel();
                return bytes;
            }
        }
        NIRADiagnosticTrace.Event("VIDEO-THUMBNAIL", "Stage=HttpUnavailable BrowserRecovery=AwaitingWebView2");
        return null;
    }

    private static async Task<byte[]?> FetchEndpoint(string url, CancellationToken token)
    {
        try
        {
            using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength is > MaxPosterBytes)
                return null;
            var bytes = await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
            return bytes.Length is > 400 and <= MaxPosterBytes ? bytes : null;
        }
        catch (OperationCanceledException) { return null; }
        catch (HttpRequestException) { return null; }
    }

    /// <summary>
    /// Called by the already-open active WebView2, which has a browser network
    /// stack distinct from HttpClient. Captures ONLY known official image hosts.
    /// </summary>
    public static void ObserveBrowser(CoreWebView2 core)
    {
        core.WebResourceResponseReceived += async (_, e) =>
        {
            try
            {
                if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri) ||
                    uri.Scheme != "https" ||
                    !(uri.Host.Equals("i.ytimg.com", StringComparison.OrdinalIgnoreCase) ||
                      uri.Host.Equals("img.youtube.com", StringComparison.OrdinalIgnoreCase))) return;
                var match = UrlId.Match(uri.AbsolutePath);
                if (!match.Success) return;
                string id = match.Groups[1].Value;
                if (Memory.ContainsKey(id) || e.Response.StatusCode != 200 ||
                    !e.Response.Headers.Contains("content-type")) return;
                var contentType = e.Response.Headers.GetHeader("content-type");
                if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return;
                using var stream = await e.Response.GetContentAsync();
                if (stream == null) return;
                using var output = new MemoryStream();
                var buffer = new byte[32768];
                int read;
                while ((read = await stream.ReadAsync(buffer.AsMemory())) > 0)
                {
                    if (output.Length + read > MaxPosterBytes) return;
                    output.Write(buffer, 0, read);
                }
                if (output.Length > 400)
                    Publish(id, output.ToArray(), "WebView2");
            }
            catch (Exception ex)
            {
                NIRADiagnosticTrace.Event("VIDEO-THUMBNAIL", "Stage=BrowserCaptureSkipped Kind=" + ex.GetType().Name);
            }
        };
    }

    private static void Publish(string id, byte[] bytes, string source)
    {
        Memory[id] = bytes;
        FailedUntil.TryRemove(id, out _);
        if (Subscribers.TryGetValue(id, out var watchers))
        {
            List<Image> targets = new();
            lock (watchers)
            {
                watchers.RemoveAll(w => !w.TryGetTarget(out _));
                foreach (var weak in watchers)
                    if (weak.TryGetTarget(out var image)) targets.Add(image);
            }
            foreach (var target in targets) Show(target, bytes);
        }
        if (source != "DiskCache") _ = SaveAsync(id, bytes);
        NIRADiagnosticTrace.Event("VIDEO-THUMBNAIL", "Stage=Ready Source=" + source);
    }

    private static async Task SaveAsync(string id, byte[] bytes)
    {
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            await File.WriteAllBytesAsync(Path.Combine(CacheDirectory, id + ".jpg"), bytes);
        }
        catch (IOException) { /* Cache is optional. */ }
        catch (UnauthorizedAccessException) { /* Cache is optional. */ }
    }

    private static void Show(Image target, byte[] bytes)
    {
        if (target.Dispatcher.HasShutdownStarted || target.Dispatcher.HasShutdownFinished) return;
        target.Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                using var stream = new MemoryStream(bytes, false);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                target.Source = bitmap;
                target.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                NIRADiagnosticTrace.Event("VIDEO-THUMBNAIL", "Stage=DecodeFailed Kind=" + ex.GetType().Name);
            }
        }));
    }
}
