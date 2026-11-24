using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using NIRAAgent.Presentation;

namespace NIRAAgent.UI.Presentation;

/// <summary>Interleaves native components with selectable prose. Model data is never executable markup.</summary>
public static class NIRARichAnswer
{
    public const string Revision = "M6E-IMAGE-2";
    private static readonly Regex Fence = new(@"^\s{0,3}(`{3,}|~{3,})([^\r\n]*)$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly HashSet<string> Types = new(StringComparer.Ordinal)
    { "heading", "text", "hero", "insight", "source", "video", "card", "metric", "equation", "table", "chart", "bar", "code", "details", "list", "quote", "timeline", "checklist", "progress", "tabs", "followups", "status" };

    public static FrameworkElement Create(string answer, FrameworkElement? media = null, Action<string>? followup = null)
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
        string[] lines = answer.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            Match open = Fence.Match(lines[i]);
            if (!open.Success) { prose.AppendLine(lines[i]); continue; }
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
                root.Children.Add(NIRAResponseDocument.Create("Some visual content could not be displayed."));
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
                root.Children.Add(NIRARichBlockRenderer.Render(block, followUp: followup));
            }
        }
        Flush();
        if (root.Children.Count == 0) root.Children.Add(NIRAResponseDocument.Create("No readable response was available."));
        return root;
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
            if (Regex.IsMatch(lines[i], @"^\s*!\[[^\]\r\n]*\]\(https?://[^\r\n]*\)\s*$", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100))) continue;
            output.AppendLine(lines[i]);
        }
        return output.ToString().Trim();
    }

    public static IReadOnlyList<NIRARichBlock> Parse(string json)
    {
        if (json.Length > 100_000) return [];
        var result = new List<NIRARichBlock>();
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 12 });
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];
            foreach (var item in doc.RootElement.EnumerateArray().Take(30))
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                string type = Str(item, "type", 30).ToLowerInvariant();
                if (!Types.Contains(type)) continue;
                string title = Str(item, "title", 180), text = Str(item, "text", 8000);
                var items = Strings(item, "items", 32, 600); var panels = Strings(item, "panels", 6, 5000);
                var cols = Strings(item, "columns", 8, 150); var labels = Strings(item, "labels", 40, 100);
                var values = Arr(item, "values", 40).Select(v => Number(v) ?? double.NaN).ToArray();
                var rows = Arr(item, "rows", 80).Select(row => (IReadOnlyList<string>)(row.ValueKind == JsonValueKind.Array ? row.EnumerateArray().Take(9).Select(v => Clip(v, 500)).ToArray() : [])).ToArray();
                double? value = Num(item, "value"), max = Num(item, "maximum");
                string unit = Str(item, "unit", 40), icon = Str(item, "icon", 30), color = Str(item, "color", 7), language = Str(item, "language", 30);
                if (new[] { title, text, unit, icon, color, language }.Concat(items).Concat(panels).Concat(cols).Concat(labels).Concat(rows.SelectMany(r => r)).Any(NIRAPublicDelivery.ContainsInternal)) continue;
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
                result.Add(new NIRARichBlock { Type=type, Title=title, Text=text, Unit=unit, Language=language, Icon=icon, Color=color,
                    Items=items, Panels=panels, Columns=cols, Rows=rows, Labels=labels, Values=values, ProgressValue=value, ProgressMaximum=max });
            }
        }
        catch (JsonException) { return []; }
        return result;
    }
    private static string Clip(JsonElement value, int limit) => value.ValueKind == JsonValueKind.String ? (value.GetString() ?? "")[..Math.Min((value.GetString() ?? "").Length, limit)] : "";
    private static string Str(JsonElement item, string key, int limit) => item.TryGetProperty(key, out var v) ? Clip(v, limit) : "";
    private static JsonElement[] Arr(JsonElement item, string key, int limit) => item.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Take(limit).ToArray() : [];
    private static string[] Strings(JsonElement item, string key, int limit, int size) => Arr(item, key, limit).Select(v => Clip(v, size)).ToArray();
    private static double? Number(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double n) && double.IsFinite(n) ? n : null;
    private static double? Num(JsonElement item, string key) => item.TryGetProperty(key, out var v) ? Number(v) : null;
}
