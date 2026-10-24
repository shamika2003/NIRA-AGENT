using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Controls;
using NIRAAgent.Diagnostics;
using NIRAAgent.Presentation;

namespace NIRAAgent.UI.Presentation;

/// <summary>
/// Fetches only official YouTube oEmbed metadata for an already validated video.
/// Never guesses titles from search summaries, thumbnails, or unrelated videos.
/// Updates all visible captions via weak references and caches verified titles.
/// </summary>
public static class NIRAVideoTitleService
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(5) };
    private static readonly ConcurrentDictionary<string, string> Known = new();
    private static readonly ConcurrentDictionary<string, Lazy<Task<string?>>> Pending = new();
    private static readonly ConcurrentDictionary<string, DateTime> RetryAfter = new();
    private static readonly ConcurrentDictionary<string, List<WeakReference<TextBlock>>> Subscribers = new();
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NIRAAgent", "youtube-titles");

    public static bool IsPlaceholder(string? value)
    {
        var text = (value ?? "").Trim().Trim('[', ']');
        return text.Length == 0 || text.Equals("Source", StringComparison.OrdinalIgnoreCase) ||
               text.Equals("Video", StringComparison.OrdinalIgnoreCase) ||
               text.Equals("Video reference", StringComparison.OrdinalIgnoreCase) ||
               text.Equals("Watch YouTube video", StringComparison.OrdinalIgnoreCase) ||
               text.Equals("YouTube video", StringComparison.OrdinalIgnoreCase);
    }

    public static string DisplayTitle(string? supplied, NIRAVideoReference video)
    {
        if (!IsPlaceholder(supplied)) return supplied!.Trim();
        if (Known.TryGetValue(video.VideoId, out var found)) return found;
        return video.Provider == "YouTube" ? "YouTube video" : video.Provider + " video";
    }

    public static void Attach(TextBlock label, string? supplied, NIRAVideoReference video)
    {
        label.Text = DisplayTitle(supplied, video);
        if (!IsPlaceholder(supplied) || video.Provider != "YouTube" || !IsValid(video.VideoId))
            return;
        var list = Subscribers.GetOrAdd(video.VideoId, _ => new List<WeakReference<TextBlock>>());
        lock (list)
        {
            list.RemoveAll(item => !item.TryGetTarget(out _));
            if (list.Count < 128) list.Add(new WeakReference<TextBlock>(label));
        }
        _ = ResolveAsync(video.VideoId);
    }

    private static bool IsValid(string id) => id.Length == 11 &&
        id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_');

    private static async Task ResolveAsync(string id)
    {
        if (Known.ContainsKey(id) ||
            (RetryAfter.TryGetValue(id, out var next) && DateTime.UtcNow < next)) return;
        var task = Pending.GetOrAdd(id, key => new Lazy<Task<string?>>( () => FetchAsync(key)));
        try
        {
            var title = await task.Value.ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(title))
            {
                RetryAfter[id] = DateTime.UtcNow.AddMinutes(5);
                return;
            }
            Known[id] = title;
            if (Subscribers.TryGetValue(id, out var watchers))
            {
                List<TextBlock> all = new();
                lock (watchers)
                {
                    watchers.RemoveAll(x => !x.TryGetTarget(out _));
                    foreach (var item in watchers)
                        if (item.TryGetTarget(out var target)) all.Add(target);
                }
                foreach (var target in all)
                    if (!target.Dispatcher.HasShutdownStarted && !target.Dispatcher.HasShutdownFinished)
                        target.Dispatcher.BeginInvoke(new Action(() => target.Text = title));
            }
            NIRADiagnosticTrace.Event("VIDEO-TITLE", "Stage=Resolved Source=OfficialYouTubeOEmbed");
        }
        catch (Exception ex)
        {
            RetryAfter[id] = DateTime.UtcNow.AddMinutes(5);
            NIRADiagnosticTrace.Event("VIDEO-TITLE", "Stage=Unavailable Kind=" + ex.GetType().Name);
        }
        finally { Pending.TryRemove(id, out _); }
    }

    private static async Task<string?> FetchAsync(string id)
    {
        var cacheFile = Path.Combine(Folder, id + ".txt");
        try
        {
            if (File.Exists(cacheFile))
            {
                var cached = (await File.ReadAllTextAsync(cacheFile).ConfigureAwait(false)).Trim();
                if (cached.Length is >= 3 and <= 220) return cached;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        // YouTube's published oEmbed endpoint provides actual video titles,
        // independently of the LLM's generic "Source" citation label.
        var url = "https://www.youtube.com/oembed?url=https%3A%2F%2Fwww.youtube.com%2Fwatch%3Fv%3D" +
                  Uri.EscapeDataString(id) + "&format=json";
        using var response = await Client.GetAsync(url).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
        if (!json.RootElement.TryGetProperty("title", out var property)) return null;
        var title = property.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(title) || title.Length > 220) return null;
        try
        {
            Directory.CreateDirectory(Folder);
            await File.WriteAllTextAsync(cacheFile, title).ConfigureAwait(false);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return title;
    }
}
