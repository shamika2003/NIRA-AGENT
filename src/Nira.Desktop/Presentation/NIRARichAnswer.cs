using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using NIRAAgent.Presentation;
using Nira.Core;

namespace NIRAAgent.UI.Presentation;

/// <summary>Interleaves native components with selectable prose. Model data is never executable markup.</summary>
public static class NIRARichAnswer
{
    public const string Revision = "M9A-CONTRACT-STABILITY-15";
    private static readonly Regex Fence = new(@"^\s{0,3}(`{3,}|~{3,})([^\r\n]*)$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly HashSet<string> Types = new(StringComparer.Ordinal)
    { "heading", "text", "hero", "insight", "source", "video", "card", "metric", "equation", "table", "chart", "bar", "code", "details", "list", "quote", "timeline", "checklist", "progress", "tabs", "followups", "status", "calculator", "form", "column", "pie", "diagram" };

    public static FrameworkElement Create(string answer, FrameworkElement? media = null, Action<string>? followup = null, IReadOnlySet<string>? videoUrls = null, IReadOnlyDictionary<string, string>? videoTitles = null, IReadOnlyList<string>? searchFallback = null)
    {
        var root = new StackPanel();
        // Original NIRA's photo-first strip belongs above the prose, not in a generic file card.
        if (media is not null) root.Children.Add(media);
        var prose = new StringBuilder();
        void Flush()
        {
            if (prose.ToString().Trim().Length > 0)
            {
                string clean = NIRAPublicDelivery.Answer(media is not null ? WithoutImageTables(prose.ToString()) : prose.ToString());
                if (!string.IsNullOrWhiteSpace(clean)) root.Children.Add(NIRAResponseDocument.Create(clean));
            }
            prose.Clear();
        }
        var shownVideos = new HashSet<string>(StringComparer.Ordinal);
        var videos = new List<NIRARichBlock>();
        var failedVisuals = new List<FrameworkElement>();
        bool invalidVideoVisual = false;
        bool AddVideo(NIRAVideoReference reference, string? title)
        {
            if (!VideoEvidence.Allows(videoUrls, reference.OriginalUrl)) return false;
            if (shownVideos.Contains(reference.Key)) return true;
            if (videos.Count >= 6) return false;
            if (videoTitles?.TryGetValue(reference.OriginalUrl, out var recordedTitle) == true && !string.IsNullOrWhiteSpace(recordedTitle)) title = recordedTitle;
            if (string.IsNullOrWhiteSpace(title) || title.Trim().Equals("video", StringComparison.OrdinalIgnoreCase) ||
                title.Trim().Equals("video (link)", StringComparison.OrdinalIgnoreCase) || NIRAPublicDelivery.ContainsInternal(title))
                title = reference.Provider + " video";
            shownVideos.Add(reference.Key);
            videos.Add(new NIRARichBlock { Type = "video", Title = title.Trim(), Text = reference.OriginalUrl });
            return true;
        }
        // The original NIRA promoted verified links from ordinary answers to a single deck.
        // Ignore fenced code and inline code; keep all unrelated prose and fact tables.
        IReadOnlyList<NIRAVideoReference> References(string line) => NIRAVideoReferencePolicy.UserSharedReferences(
            Regex.Replace(line.Replace('|', ' '), @"`+[^`]*`+", "", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)), 12);
        string[] lines = answer.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            Match open = Fence.Match(lines[i]);
            if (!open.Success)
            {
                string trimmedLine = lines[i].Trim();
                if (trimmedLine.StartsWith("Host-recorded execution evidence:", StringComparison.OrdinalIgnoreCase) ||
                    trimmedLine.StartsWith("Recorded observations (tool/provider output):", StringComparison.OrdinalIgnoreCase) ||
                    trimmedLine.StartsWith("Recorded verification observations:", StringComparison.OrdinalIgnoreCase)) break;
                if (NIRAPublicDelivery.ContainsInternal(lines[i])) { prose.AppendLine(lines[i]); continue; }
                // Replace only a video-list table whose every row is grounded. Ordinary data tables,
                // unsupported links, and over-limit entries remain intact as prose.
                if (i + 2 < lines.Length && IsVideoListHeader(lines[i]) && IsTableSeparator(lines[i + 1]))
                {
                    int end = i + 2;
                    var table = new List<(NIRAVideoReference Video, string Title)>();
                    bool valid = true;
                    int titleColumn = TableCells(lines[i]).FindIndex(cell => cell.Equals("title", StringComparison.OrdinalIgnoreCase) || cell.Equals("video title", StringComparison.OrdinalIgnoreCase));
                    while (end < lines.Length && lines[end].TrimStart().StartsWith('|'))
                    {
                        var refs = References(lines[end]); var cells = TableCells(lines[end]);
                        if (refs.Count != 1 || !VideoEvidence.Allows(videoUrls, refs[0].OriginalUrl)) valid = false;
                        else table.Add((refs[0], titleColumn >= 0 && titleColumn < cells.Count ? cells[titleColumn].Trim('*', '_', ' ') : ""));
                        end++;
                    }
                    int additional = table.Select(row => row.Video.Key).Distinct(StringComparer.Ordinal).Count(key => !shownVideos.Contains(key));
                    if (valid && table.Count > 0 && videos.Count + additional <= 6)
                    {
                        foreach (var row in table) AddVideo(row.Video, row.Title);
                        i = end - 1; continue;
                    }
                }
                if (NIRAVideoReferencePolicy.TryParseStandaloneLine(lines[i], out var video, out var title) && video is not null && AddVideo(video, title)) continue;
                foreach (var reference in References(lines[i])) AddVideo(reference, null);
                prose.AppendLine(lines[i]); continue;
            }
            string fence = open.Groups[1].Value;
            bool rich = open.Groups[2].Value.Trim().Equals("nira-ui", StringComparison.OrdinalIgnoreCase);
            if (rich) Flush(); else prose.AppendLine(lines[i]);
            var json = new StringBuilder(); bool closed = false;
            while (++i < lines.Length)
            {
                string trimmed = lines[i].Trim();
                if (trimmed.Length >= fence.Length && trimmed.All(c => c == fence[0]))
                { closed = true; if (!rich) prose.AppendLine(lines[i]); break; }
                if (rich) { if (json.Length <= 100_000) json.AppendLine(lines[i]); }
                else prose.AppendLine(lines[i]);
            }
            if (!rich) continue;
            IReadOnlyList<NIRARichBlock> blocks = closed ? Parse(json.ToString()) : [];
            if (blocks.Count == 0)
            {
                // Recover exact observed watch URLs even if the model emitted a wrong
                // JSON envelope, unexpected field names or incomplete presentation JSON.
                string raw = json.ToString();
                bool videoLike = Regex.IsMatch(raw, @"\b(?:video|videos|video[_-]?(?:list|carousel|deck|grid)|youtube|vimeo)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
                bool recovered = false;
                if (videoLike || searchFallback is { Count: > 0 })
                {
                    foreach (var reference in NIRAVideoReferencePolicy.UserSharedReferences(raw.Replace("\\/", "/", StringComparison.Ordinal), 12))
                        recovered |= AddVideo(reference, null);
                    invalidVideoVisual = true;
                }
                if (!recovered)
                {
                    var warning = NIRAResponseDocument.Create(videoLike ? "A video preview could not be prepared from this reply." : "Some visual content could not be displayed.");
                    root.Children.Add(warning);
                    if (videoLike || searchFallback is { Count: > 0 }) failedVisuals.Add(warning);
                }
                continue;
            }
            for (int j = 0; j < blocks.Count; j++)
            {
                var block = blocks[j];
                if (block.Type is "card" or "metric")
                {
                    int end = j + 1;
                    while (end < blocks.Count && blocks[end].Type == block.Type) end++;
                    if (end > j + 1)
                    {
                        var group = blocks.Skip(j).Take(end - j);
                        root.Children.Add(block.Type == "card" ? NIRARichBlockRenderer.RenderCardGroup(group) : NIRARichBlockRenderer.RenderMetricGroup(group));
                        j = end - 1; continue;
                    }
                }
                if (block.Type == "video")
                {
                    if (NIRAVideoReferencePolicy.TryParse(block.Text, out var reference) && reference is not null && AddVideo(reference, block.Title)) continue;
                    invalidVideoVisual = true;
                    // Unsupported or unobserved links remain click-only, never embedded.
                    root.Children.Add(NIRARichBlockRenderer.Render(block, followUp: followup));
                    continue;
                }
                if (block.Type == "table")
                    foreach (var row in block.Rows)
                        foreach (var cell in row)
                            foreach (var entry in References(cell)) AddVideo(entry, null);
                root.Children.Add(NIRARichBlockRenderer.Render(block, followUp: followup));
            }
        }
        Flush();
        // No second inference or search. If the requested presentation failed entirely,
        // expose the actual recorded search references with a qualified label.
        if (invalidVideoVisual && videos.Count == 0 && searchFallback is { Count: > 0 })
        {
            foreach (string url in searchFallback)
                if (NIRAVideoReferencePolicy.TryParse(url, out var reference) && reference is not null)
                    AddVideo(reference, null);
            if (videos.Count > 0)
                root.Children.Add(NIRAResponseDocument.Create("Video search results (playback not verified)."));
        }
        if (videos.Count > 0)
            foreach (var warning in failedVisuals) root.Children.Remove(warning);
        if (videos.Count == 1) root.Children.Add(NIRAVideoPlayer.Render(videos[0]));
        else if (videos.Count > 1)
        {
            var deck = new NIRAVideoDepthDeck();
            foreach (var block in videos) deck.TryAdd(block);
            deck.Complete();
            root.Children.Add(deck.View);
        }
        if (root.Children.Count == 0) root.Children.Add(NIRAResponseDocument.Create("No readable response was available."));
        return root;
    }

    private static List<string> TableCells(string line) => line.Trim().Trim('|').Split('|').Select(cell => cell.Trim().Trim('*', '_', ' ')).ToList();
    private static bool IsTableSeparator(string line) => line.Contains('|') && TableCells(line).All(cell => cell.Trim(':', ' ').Length >= 3 && cell.Trim(':', ' ').All(c => c == '-'));
    private static bool IsVideoListHeader(string line)
    {
        if (!line.TrimStart().StartsWith('|')) return false;
        var cells = TableCells(line).Select(cell => cell.ToLowerInvariant()).ToArray();
        return cells.Any(cell => cell is "title" or "video title") && cells.Any(cell => cell is "link" or "url" or "video" or "source") &&
            cells.All(cell => cell is "#" or "no." or "title" or "video title" or "link" or "url" or "video" or "source" or "description" or "short description");
    }

    private static string WithoutImageTables(string text)
    {
        string[] lines = text.Split('\n'); var output = new StringBuilder(); string? fence = null;
        for (int i = 0; i < lines.Length; i++)
        {
            if (fence is not null)
            {
                string trimmed = lines[i].Trim();
                if (trimmed.Length >= fence.Length && trimmed.All(c => c == fence[0])) fence = null;
                output.AppendLine(lines[i]); continue;
            }
            Match opening = Fence.Match(lines[i]);
            if (opening.Success) { fence = opening.Groups[1].Value; output.AppendLine(lines[i]); continue; }
            // The native photo viewer supplies actual source links and attribution.
            // Remove only redundant direct-image-URL tables, never ordinary fact tables.
            if (i + 1 < lines.Length && lines[i].Contains('|') && lines[i + 1].Contains("---", StringComparison.Ordinal) &&
                Regex.IsMatch(lines[i], @"thumbnail|full.resolution|direct image|media url", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)))
            { i++; while (i + 1 < lines.Length && lines[i + 1].TrimStart().StartsWith('|')) i++; continue; }
            // The receipt-backed strip is already visible. Remove standalone Markdown
            // embeds so the model cannot add a second unverified media reference.
            if (Regex.IsMatch(lines[i], @"^\s*!\[[^\]\r\n]*\](?:\([^\r\n]+\))?\s*$", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100))) continue;
            // Preserve the descriptive cells of a table while removing redundant
            // receipt-backed Markdown embeds, including paths containing parentheses.
            string line = lines[i];
            if (line.TrimStart().StartsWith('|'))
                line = Regex.Replace(line, @"!\[([^\]\r\n]*)\]\((?:[^()\r\n]|\([^()\r\n]*\))*\)", "$1", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            output.AppendLine(line);
        }
        return output.ToString().Trim();
    }

    public static IReadOnlyList<NIRARichBlock> Parse(string json)
    {
        if (json.Length > 100_000) return [];
        var result = new List<NIRARichBlock>();
        try
        {
            using var doc = ParseVisualDocument(json);
            JsonElement envelope = doc.RootElement;
            if (envelope.ValueKind == JsonValueKind.Object && !envelope.TryGetProperty("type", out _))
            {
                foreach (string key in new[] { "blocks", "components", "videos" })
                    if (envelope.TryGetProperty(key, out var array) && array.ValueKind == JsonValueKind.Array)
                    { envelope = array; break; }
            }
            IEnumerable<JsonElement> entries = envelope.ValueKind switch
            {
                JsonValueKind.Array => envelope.EnumerateArray().Take(30),
                JsonValueKind.Object => new[] { envelope },
                _ => Array.Empty<JsonElement>()
            };
            foreach (var item in entries)
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                string type = Str(item, "type", 30).ToLowerInvariant();
                if (!Types.Contains(type)) continue;
                string title = Str(item, "title", 180), text = Str(item, "text", 8000);
                if (type == "video" && !NIRAVideoReferencePolicy.TryParse(text, out _))
                    foreach (string key in new[] { "url", "videoUrl", "video_url", "watchUrl", "watch_url", "link", "src" })
                    {
                        string candidate = Str(item, key, 2048);
                        if (NIRAVideoReferencePolicy.TryParse(candidate, out _)) { text = candidate; break; }
                    }
                var items = Strings(item, "items", 32, 600); var panels = Strings(item, "panels", 6, 5000);
                var cols = Strings(item, "columns", 8, 150); var labels = Strings(item, "labels", 40, 100);
                var values = Arr(item, "values", 40).Select(v => Number(v) ?? double.NaN).ToArray();
                var series = new List<NIRAChartSeries>();
                if (type is "chart" or "column" && item.TryGetProperty("datasets", out var datasets))
                {
                    if (datasets.ValueKind != JsonValueKind.Array || datasets.GetArrayLength() is < 1 or > 4 || values.Length != 0) continue;
                    foreach (var entry in datasets.EnumerateArray())
                    {
                        if (entry.ValueKind != JsonValueKind.Object) break;
                        string name = Str(entry, "label", 100);
                        var points = Arr(entry, "data", 41).Select(v => Number(v) ?? double.NaN).ToArray();
                        if (string.IsNullOrWhiteSpace(name) || NIRAPublicDelivery.ContainsInternal(name) || points.Length != labels.Length || points.Any(v => !double.IsFinite(v) || Math.Abs(v) > 1e100)) break;
                        series.Add(new NIRAChartSeries(name, points));
                    }
                    if (series.Count != datasets.GetArrayLength() || series.Select(s => s.Label).Distinct(StringComparer.OrdinalIgnoreCase).Count() != series.Count) continue;
                    values = series[0].Values.ToArray();
                }
                var rows = Arr(item, "rows", 80).Select(row => (IReadOnlyList<string>)(row.ValueKind == JsonValueKind.Array ? row.EnumerateArray().Take(9).Select(v => Clip(v, 500)).ToArray() : [])).ToArray();
                if (type == "diagram" && !TryDiagram(item, ref items, out rows)) continue;
                double? value = Num(item, "value"), max = Num(item, "maximum");
                string formula = Str(item, "formula", 257);
                string unit = Str(item, "unit", 40), icon = Str(item, "icon", 30), color = Str(item, "color", 7), language = Str(item, "language", 30);
                if (new[] { title, text, unit, icon, color, language, formula }.Concat(items).Concat(panels).Concat(cols).Concat(labels).Concat(rows.SelectMany(r => r)).Any(NIRAPublicDelivery.ContainsInternal)) continue;
                if (type is "source" or "video")
                {
                    // Never fetch or execute from presentation. These are click-only links,
                    // explicitly labelled as not independently checked by the display layer.
                    if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0) continue;
                    title = (title.Length == 0 ? uri.Host : title) + " (link)";
                }
                if ((type is "list" or "timeline" or "checklist" or "followups") && items.Length == 0) continue;
                if (type == "tabs" && (items.Length is < 2 or > 6 || items.Length != panels.Length)) continue;
                if (type == "table" && (cols.Length < 2 || rows.Length == 0 || rows.Any(r => r.Count != cols.Length))) continue;
                if ((type is "chart" or "bar") && (labels.Length < 2 || labels.Length != values.Length || values.Any(v => !double.IsFinite(v) || Math.Abs(v) > 1e100 || (type == "bar" && v < 0)))) continue;
                if (type == "progress" && (value is null || max is null || max <= 0 || value < 0 || value > max || max > 1e100)) continue;
                if ((type is "equation" or "code" or "details") && text.Length == 0) continue;
                if (type == "metric" && (title.Length == 0 || text.Length == 0)) continue;
                var block = new NIRARichBlock { Type=type, Title=title, Text=text, Unit=unit, Language=language, Icon=icon, Color=color,
                    Items=items, Panels=panels, Columns=cols, Rows=rows, Labels=labels, Values=values, ProgressValue=value, ProgressMaximum=max, Formula=formula, Series=series };
                if (type is "column" or "pie")
                {
                    if (labels.Length < 2 || labels.Length > (type == "pie" ? 8 : 20) || labels.Length != values.Length ||
                        labels.Any(string.IsNullOrWhiteSpace) || values.Any(v => !double.IsFinite(v) || Math.Abs(v) > 1e100) ||
                        (type == "pie" && (values.Any(v => v < 0) || values.Sum() <= 0))) continue;
                }
                if (type == "calculator" && (labels.Length is < 1 or > 8 || labels.Length != values.Length ||
                    labels.Any(string.IsNullOrWhiteSpace) || !NIRAVisualArithmetic.TryEvaluate(formula, values, out _))) continue;
                if (type == "form" && (items.Length is < 1 or > 6 || items.Any(string.IsNullOrWhiteSpace) ||
                    (panels.Length != 0 && panels.Length != items.Length) || panels.Any(v => v.Length > 500))) continue;
                if (type == "diagram" && !NIRAVisualDiagram.TryLayers(block, out _)) continue;
                result.Add(block);
            }
        }
        catch (JsonException) { return []; }
        return result;
    }
    private static JsonDocument ParseVisualDocument(string json)
    {
        var options = new JsonDocumentOptions { MaxDepth = 12 };
        try { return JsonDocument.Parse(json, options); }
        catch (JsonException)
        {
            // One narrowly defined presentation-only repair: a complete object (or
            // objects) in an array with just the final outer ] missing. Never repair
            // string contents, truncated objects, malformed code or executable calls.
            string trimmed = json.Trim();
            if (trimmed.Length >= 2 && trimmed[0] == '[' && trimmed[^1] == '}')
                return JsonDocument.Parse(trimmed + "]", options);
            throw;
        }
    }
    private static bool TryDiagram(JsonElement item, ref string[] items, out IReadOnlyList<string>[] rows)
    {
        rows = [];
        // Normalize only declared graph data, never interpret Mermaid/code or execute text.
        var ids = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (item.TryGetProperty("nodes", out var nodes) && items.Length == 0)
        {
            if (nodes.ValueKind != JsonValueKind.Array || nodes.GetArrayLength() is < 2 or > 12) return false;
            var labels = new List<string>();
            foreach (var node in nodes.EnumerateArray())
            {
                if (node.ValueKind == JsonValueKind.String) labels.Add(Clip(node, 91));
                else if (node.ValueKind == JsonValueKind.Object)
                {
                    string id = Str(node, "id", 91), label = Str(node, "label", 91);
                    if (label.Length == 0) label = id;
                    if (id.Length == 0 || !ids.TryAdd(id, labels.Count + 1)) return false;
                    labels.Add(label);
                }
                else return false;
            }
            items = labels.ToArray();
        }
        if (items.Length is < 2 or > 12) return false;
        string[] names = items;
        if (!item.TryGetProperty("rows", out var links) && !item.TryGetProperty("edges", out links)) return false;
        if (links.ValueKind != JsonValueKind.Array || links.GetArrayLength() is < 1 or > 24) return false;
        int Index(JsonElement v)
        {
            if (v.ValueKind == JsonValueKind.Number) return v.TryGetInt32(out int numeric) ? numeric : -1;
            string name = Clip(v, 91);
            if (ids.TryGetValue(name, out int id)) return id;
            if (int.TryParse(name, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int index)) return index;
            return Array.FindIndex(names, n => n.Equals(name, StringComparison.OrdinalIgnoreCase)) + 1;
        }
        var result = new List<IReadOnlyList<string>>();
        foreach (var edge in links.EnumerateArray())
        {
            JsonElement a, b;
            if (edge.ValueKind == JsonValueKind.Array && edge.GetArrayLength() == 2) { a = edge[0]; b = edge[1]; }
            else if (edge.ValueKind == JsonValueKind.Object &&
                (edge.TryGetProperty("from", out a) || edge.TryGetProperty("source", out a)) &&
                (edge.TryGetProperty("to", out b) || edge.TryGetProperty("target", out b))) { }
            else return false;
            int from = Index(a), to = Index(b);
            if (from < 1 || from > items.Length || to < 1 || to > items.Length) return false;
            result.Add(new[] { from.ToString(System.Globalization.CultureInfo.InvariantCulture), to.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        }
        rows = result.ToArray(); return true;
    }
    private static string Clip(JsonElement value, int limit) => value.ValueKind == JsonValueKind.String ? (value.GetString() ?? "")[..Math.Min((value.GetString() ?? "").Length, limit)] : "";
    private static string Str(JsonElement item, string key, int limit) => item.TryGetProperty(key, out var v) ? Clip(v, limit) : "";
    private static JsonElement[] Arr(JsonElement item, string key, int limit) => item.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Take(limit).ToArray() : [];
    private static string[] Strings(JsonElement item, string key, int limit, int size) => Arr(item, key, limit).Select(v => Clip(v, size)).ToArray();
    private static double? Number(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double n) && double.IsFinite(n) ? n : null;
    private static double? Num(JsonElement item, string key) => item.TryGetProperty(key, out var v) ? Number(v) : null;
}
