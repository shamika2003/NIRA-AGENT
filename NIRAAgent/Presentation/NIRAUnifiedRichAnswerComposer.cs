using System.Text.Json;
using System.Text.RegularExpressions;
using NIRAAgent.Diagnostics;

namespace NIRAAgent.Presentation;

public sealed record NIRAUnifiedRichAnswer(string Screen, IReadOnlyList<NIRARichBlock> Blocks);

/// <summary>
/// Display-only completion boundary for NIRA's single agent loop. Preserves
/// explanatory Markdown, moves observed playable videos into one end-of-reply
/// group, and leaves photographs in their existing paragraph-anchored path.
/// Only user-provided or successfully observed URLs can become player blocks.
/// </summary>
public static class NIRAUnifiedRichAnswerComposer
{
    private static readonly Regex Appendix = new(
        @"(?:^|\r?\n)\s*```nira-blocks\s*\r?\n(?<json>\{[\s\S]*\})\s*\r?\n```\s*$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(200));
    private static readonly Regex Url = new(
        @"https://[^\s<>\]\)\('""\\]{1,1500}",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(200));
    private static readonly Regex MarkdownLink = new(
        @"\[(?<label>[^\]\r\n]{1,180})\]\((?<url>https://[^\s<>\)]{1,1900})\)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(200));
    private static readonly Regex InternalSourceLabel = new(
        @"^(?:S[1-9]\d*|Source\s+S[1-9]\d*)$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    public static NIRAUnifiedRichAnswer Compose(string answer, string userRequest,
        IReadOnlyList<string>? successfulObservationReceipts)
    {
        if (string.IsNullOrWhiteSpace(answer) || answer.Length > 120_000)
            return new NIRAUnifiedRichAnswer(answer ?? "", Array.Empty<NIRARichBlock>());

        var trustedVideos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddUserVideoUrls(userRequest, trustedVideos);
        var observedTitles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string receipt in successfulObservationReceipts ?? Array.Empty<string>())
            if (receipt.Length <= 100_000)
                AddObservedVideoSources(receipt, trustedVideos, observedTitles);

        Match appendix;
        try { appendix = Appendix.Match(answer); }
        catch (RegexMatchTimeoutException)
        { return new NIRAUnifiedRichAnswer(answer, Array.Empty<NIRARichBlock>()); }
        if (!appendix.Success || appendix.Groups["json"].Length > 26_000)
            return GroupVideosAtEnd(answer, Array.Empty<NIRARichBlock>(), trustedVideos,
                observedTitles);

        string screen = answer[..appendix.Index].TrimEnd();
        try
        {
            using JsonDocument document = JsonDocument.Parse(appendix.Groups["json"].Value,
                new JsonDocumentOptions { MaxDepth = 12 });
            var proposed = NIRARichBlockJsonReader.Read(document.RootElement);
            var accepted = new List<NIRARichBlock>();
            foreach (NIRARichBlock block in proposed.Take(18))
            {
                if (block.Type is "file" or "source") continue;
                if (block.Type == "video" &&
                    (!NIRAVideoReferencePolicy.TryParse(block.Text, out var video) ||
                     video is null || !trustedVideos.Contains(video.Key))) continue;
                accepted.Add(block);
            }
            if (string.IsNullOrWhiteSpace(screen))
                return new NIRAUnifiedRichAnswer(answer, Array.Empty<NIRARichBlock>());
            return GroupVideosAtEnd(screen, accepted, trustedVideos, observedTitles);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or
            InvalidOperationException or RegexMatchTimeoutException)
        {
            NIRADiagnosticTrace.Event("AGENT-RICH-PRESENTATION",
                "Rejected=" + ex.GetType().Name);
            return GroupVideosAtEnd(screen.Length > 0 ? screen : answer,
                Array.Empty<NIRARichBlock>(), trustedVideos, observedTitles);
        }
    }

    private static NIRAUnifiedRichAnswer GroupVideosAtEnd(string screen,
        IReadOnlyList<NIRARichBlock> blocks, HashSet<string> trustedVideos,
        IReadOnlyDictionary<string, string> observedTitles)
    {
        // One collection per answer. Deduplicate by verified provider/video ID;
        // NEVER infer a video title from the subject of a neighboring web page.
        var videos = new List<NIRARichBlock>();
        var other = new List<NIRARichBlock>(blocks.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddVideo(NIRAVideoReference video, string? label)
        {
            if (!trustedVideos.Contains(video.Key) || !seen.Add(video.Key)) return;
            string title = CleanVideoLabel(label, video);
            if (NIRAVideoTitlePlaceholder(title, video) &&
                observedTitles.TryGetValue(video.Key, out string? observed))
                title = CleanVideoLabel(observed, video);
            videos.Add(new NIRARichBlock { Type = "video", Title = title,
                Text = video.OriginalUrl });
        }
        string Rewrite(string source)
        {
            // Transform cited/watch links into readable prose; the associated
            // playable item is now in the single video group at the end.
            string linked = MarkdownLink.Replace(source, match =>
            {
                if (!NIRAVideoReferencePolicy.TryParse(match.Groups["url"].Value,
                        out var video) || video is null ||
                    !trustedVideos.Contains(video.Key)) return match.Value;
                string label = match.Groups["label"].Value.Trim();
                AddVideo(video, label);
                return InternalSourceLabel.IsMatch(label) ? "video footage below" : label;
            });
            // Also accept an actual standalone or inline plaintext watch URL.
            // Do not replace non-video web links or unsupported destinations.
            return Url.Replace(linked, match =>
            {
                string raw = match.Value.TrimEnd('.', ',', ';', ':', '!', '?', '*', '"');
                if (!NIRAVideoReferencePolicy.TryParse(raw, out var video) ||
                    video is null || !trustedVideos.Contains(video.Key)) return match.Value;
                AddVideo(video, null);
                return "video below" + match.Value[raw.Length..];
            });
        }

        try
        {
            // Preserve code fences and their literal URLs untouched.
            string[] lines = screen.Replace("\r\n", "\n").Split('\n');
            bool inCode = false;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
                { inCode = !inCode; continue; }
                if (!inCode) lines[i] = Rewrite(lines[i]);
            }
            screen = string.Join("\n", lines);
            foreach (NIRARichBlock block in blocks)
            {
                if (block.Type == "video")
                {
                    if (NIRAVideoReferencePolicy.TryParse(block.Text, out var v) && v is not null)
                        AddVideo(v, block.Title);
                }
                else if (block.Type is "card" or "text" or "insight" or "details")
                    other.Add(block with { Text = Rewrite(block.Text) });
                else other.Add(block);
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // A pathological model paragraph cannot make us display invented
            // videos, or discard the underlying answer.
            return new NIRAUnifiedRichAnswer(screen, blocks);
        }
        other.AddRange(videos);
        NIRADiagnosticTrace.Event("AGENT-RICH-PRESENTATION",
            "Accepted=" + other.Count + " Videos=" + videos.Count + " Placement=End");
        return new NIRAUnifiedRichAnswer(screen.Trim(), other);
    }

    private static string CleanVideoLabel(string? proposed, NIRAVideoReference video)
    {
        string label = (proposed ?? "").Trim();
        if (InternalSourceLabel.IsMatch(label) ||
            label.Equals("Source", StringComparison.OrdinalIgnoreCase) ||
            label.Equals("Video", StringComparison.OrdinalIgnoreCase) ||
            label.Length is < 4 or > 140)
            return video.Provider == "YouTube" ? "YouTube video" : video.Provider + " video";
        // The title is plain display text. Never pass through Markdown/HTML.
        return Regex.Replace(label, @"[\r\n\[\]<>]", " ").Trim();
    }

    private static bool NIRAVideoTitlePlaceholder(string title, NIRAVideoReference video) =>
        title.Equals(video.Provider + " video", StringComparison.OrdinalIgnoreCase);

    private static void AddUserVideoUrls(string source, HashSet<string> keys)
    {
        if (string.IsNullOrWhiteSpace(source)) return;
        try
        {
            foreach (Match match in Url.Matches(source.Length > 12000
                ? source[..12000] : source).Cast<Match>().Take(30))
            {
                string address = match.Value.TrimEnd('.', ',', ';', ':', '!', '?', '*', '"');
                if (NIRAVideoReferencePolicy.TryParse(address, out var video) && video is not null)
                    keys.Add(video.Key);
            }
        }
        catch (RegexMatchTimeoutException) { }
    }

    private static void AddObservedVideoSources(string receipt, HashSet<string> keys,
        Dictionary<string, string> titles)
    {
        // Receipts supplied by the trusted host are authoritative only about
        // their structured source URLs. Do not promote arbitrary links found
        // inside untrusted page excerpts, snippets or JSON string values.
        try
        {
            using JsonDocument outer = JsonDocument.Parse(receipt,
                new JsonDocumentOptions { MaxDepth = 20 });
            JsonElement root = outer.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;
            if (root.TryGetProperty("output", out JsonElement output) &&
                output.ValueKind == JsonValueKind.String)
            {
                using JsonDocument inner = JsonDocument.Parse(output.GetString() ?? "{}",
                    new JsonDocumentOptions { MaxDepth = 20 });
                AddSourceArray(inner.RootElement, keys, titles);
            }
            else AddSourceArray(root, keys, titles);
        }
        catch (JsonException) { }
    }

    private static void AddSourceArray(JsonElement root, HashSet<string> keys,
        Dictionary<string, string> titles)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("sources", out JsonElement sources) ||
            sources.ValueKind != JsonValueKind.Array) return;
        foreach (JsonElement item in sources.EnumerateArray().Take(60))
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("url", out JsonElement url) ||
                url.ValueKind != JsonValueKind.String ||
                !NIRAVideoReferencePolicy.TryParse(url.GetString(), out var video) ||
                video is null) continue;
            keys.Add(video.Key);
            if (!item.TryGetProperty("title", out var title) ||
                title.ValueKind != JsonValueKind.String) continue;
            string name = (title.GetString() ?? "").Trim();
            if (name.Length is >= 4 and <= 140) titles.TryAdd(video.Key, name);
        }
    }

    public static int RunSelfCheck()
    {
        const string video = "https://www.youtube.com/watch?v=dQw4w9WgXcQ";
        string valid = "Here is a full explanation.\n\n```nira-blocks\n" +
            "{\"displayBlocks\":[{\"type\":\"insight\",\"title\":\"Key idea\",\"text\":\"Observed.\"}," +
            "{\"type\":\"video\",\"title\":\"Verified footage\",\"text\":\"" + video + "\"}]}\n```";
        var trusted = Compose(valid, "Show me", new[] { "{\"sources\":[{\"url\":\"" + video + "\",\"title\":\"Verified footage\"}]}" });
        if (trusted.Blocks.Count != 2 || trusted.Screen.Contains("nira-blocks") ||
            trusted.Blocks[^1].Title != "Verified footage")
            throw new InvalidOperationException("Rich video appendix did not survive evidence filtering.");
        var untrusted = Compose(valid, "Show me", Array.Empty<string>());
        if (untrusted.Blocks.Count != 1 || untrusted.Blocks[0].Type != "insight")
            throw new InvalidOperationException("Unobserved video promoted into a trusted player.");
        const string videoAlias = "Here is the demonstration.\n\n```nira-blocks\n" +
            "{\"displayBlocks\":[{\"kind\":\"video_player\",\"title\":\"Launch and landing\",\"videoUrl\":\"" +
            video + "\"}]}\n```";
        var alias = Compose(videoAlias, "Watch the demonstration", new[] {
            "{\"sources\":[{\"url\":\"" + video + "\",\"title\":\"Launch and landing\"}]}" });
        if (alias.Blocks.Count != 1 || alias.Blocks[0].Type != "video" ||
            alias.Blocks[0].Title != "Launch and landing")
            throw new InvalidOperationException("Valid verified videoUrl block alias was discarded.");
        var aliasDenied = Compose(videoAlias, "Explain launch sequence", Array.Empty<string>());
        if (aliasDenied.Blocks.Count != 0)
            throw new InvalidOperationException("Unobserved videoUrl alias promoted into a player.");
        var plain = Compose("Regular complete Markdown answer.", "hi", Array.Empty<string>());
        if (plain.Screen != "Regular complete Markdown answer." || plain.Blocks.Count != 0)
            throw new InvalidOperationException("Plain prose was modified by presentation.");
        var inline = Compose("Here is [Falcon booster landing](" + video + ") with details.",
            "Show video", new[] { "{\"sources\":[{\"url\":\"" + video + "\"}]}" });
        if (inline.Blocks.Count != 1 || inline.Blocks[0].Title != "Falcon booster landing" ||
            inline.Screen.Contains(video, StringComparison.Ordinal))
            throw new InvalidOperationException("Inline video was not moved to end-of-reply deck.");
        var duplicate = Compose("[Landing](" + video + ") and [Landing again](" + video + ")",
            "Show videos", new[] { "{\"sources\":[{\"url\":\"" + video + "\"}]}" });
        if (duplicate.Blocks.Count != 1)
            throw new InvalidOperationException("Duplicate watch URLs created extra player cards.");
        return 3; // Three production smoke families; extended video assertions above.
    }
}
