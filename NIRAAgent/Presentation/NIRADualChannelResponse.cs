using System.Text.RegularExpressions;
using System.Text.Json;

namespace NIRAAgent.Presentation;

// One authoritative cognition decision can contain separate spoken words and
// declarative screen blocks. The WPF app, not the model, owns the rendering.
public sealed record NIRARichBlock
{
    public string Type { get; init; } = string.Empty; // heading, text, card, metric, table, chart, code, details, list, quote, timeline, checklist, progress
    public string Title { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public IReadOnlyList<string> Items { get; init; } = Array.Empty<string>();
    // Tabs: parallel, model-authored labels/content; UI never executes supplied text.
    public IReadOnlyList<string> Panels { get; init; } = Array.Empty<string>();
    public double? ProgressValue { get; init; }
    public double? ProgressMaximum { get; init; }
    public IReadOnlyList<string> Columns { get; init; } = Array.Empty<string>();
    public IReadOnlyList<IReadOnlyList<string>> Rows { get; init; } = Array.Empty<IReadOnlyList<string>>();
    public IReadOnlyList<string> Labels { get; init; } = Array.Empty<string>();
    public IReadOnlyList<double> Values { get; init; } = Array.Empty<double>();
}

public sealed record NIRAPresentationSnapshot(string Speech, IReadOnlyList<NIRARichBlock> Blocks);

// UI-only per-message state. It is not a goal, user utterance, or action authorization.
public sealed record NIRARichInteractionState
{
    public bool Expanded { get; init; }
    public int SelectedIndex { get; init; } = -1;
    public IReadOnlyList<int> CheckedIndices { get; init; } = Array.Empty<int>();
}

public static class NIRAPresentationPolicy
{
    public const int MaxBlocks = 8;
    private static string Clip(string? s, int n)
    {
        string value = (s ?? string.Empty).Trim();
        return value.Length > n ? value[..n] : value;
    }

    public static IReadOnlyList<NIRARichBlock> Normalize(IReadOnlyList<NIRARichBlock>? blocks)
    {
        if (blocks is null || blocks.Count == 0) return Array.Empty<NIRARichBlock>();
        var accepted = new List<NIRARichBlock>();
        foreach (var input in blocks.Take(MaxBlocks))
        {
            if (input is null) continue;
            string type = (input.Type ?? string.Empty).Trim().ToLowerInvariant();
            if (type is not ("heading" or "text" or "card" or "metric" or "table" or "chart" or "code" or "details" or "list" or "quote" or "timeline" or "checklist" or "progress" or "tabs" or "followups")) continue;
            var items = (input.Items ?? Array.Empty<string>()).Take(24).Select(x => Clip(x, 450)).Where(x => x.Length > 0).ToArray();
            var panels = (input.Panels ?? Array.Empty<string>()).Take(5).Select(x => Clip(x, 3000)).ToArray();
            var cols = (input.Columns ?? Array.Empty<string>()).Take(8).Select(c => Clip(c, 75)).ToArray();
            var rows = (input.Rows ?? Array.Empty<IReadOnlyList<string>>())
                .Take(40).Where(row => row != null)
                .Select(row => (IReadOnlyList<string>)row.Take(cols.Length).Select(x => Clip(x, 240)).ToArray())
                .Where(row => row.Count == cols.Length).ToArray();
            var labels = (input.Labels ?? Array.Empty<string>()).Take(40).Select(x => Clip(x, 85)).ToArray();
            var values = (input.Values ?? Array.Empty<double>()).Take(40).ToArray();
            if ((type is "list" or "timeline" or "checklist" or "followups") && items.Length == 0) continue;
            if (type == "tabs" && (items.Length < 2 || items.Length > 5 ||
                panels.Length != items.Length || panels.Any(string.IsNullOrWhiteSpace))) continue;
            if (type == "progress" && (input.ProgressValue is not double progress ||
                input.ProgressMaximum is not double maximum || !double.IsFinite(progress) ||
                !double.IsFinite(maximum) || maximum <= 0 || progress < 0 || progress > maximum)) continue;
            if ((type is "details" or "quote") && string.IsNullOrWhiteSpace(input.Text)) continue;
            if (type == "table" && (cols.Length == 0 || rows.Length == 0)) continue;
            if (type == "chart" && (labels.Length < 2 || labels.Length != values.Length || values.Any(v => !double.IsFinite(v)))) continue;
            string title = Clip(input.Title, 160), text = Clip(input.Text, type == "code" ? 7000 : type == "details" ? 4000 : 1600);
            if ((type is "heading" or "text" or "card" or "code" or "details" or "quote") && title.Length == 0 && text.Length == 0) continue;
            if (type == "metric" && (title.Length == 0 || text.Length == 0)) continue;
            if (type == "followups" && items.Length > 6) items = items.Take(6).ToArray();
            if (type == "progress" && title.Length == 0) continue;
            accepted.Add(input with { Type=type, Title=title, Text=text, Unit=Clip(input.Unit, 24),
                Language=Clip(input.Language, 25), Items=items, Panels=panels, Columns=cols, Rows=rows, Labels=labels, Values=values });
        }
        return accepted;
    }
    // A model may return a fenced code example in reply rather than in the
    // optional typed blocks. Render it locally instead of displaying backticks
    // or reading syntax aloud. This is presentation parsing, not a second LLM.
    public static (string Screen, string Speech, IReadOnlyList<NIRARichBlock> Blocks)
        RecoverCode(string screen, string speech, IReadOnlyList<NIRARichBlock>? blocks)
    {
        if (string.IsNullOrWhiteSpace(screen) ||
            !screen.Contains("```", StringComparison.Ordinal))
            return (screen, speech, blocks ?? Array.Empty<NIRARichBlock>());
        if (blocks is { Count: > 0 } && blocks.Any(b => b.Type == "code"))
        {
            // The typed code block already owns the code. Suppress any duplicate
            // fenced source in reply/speech rather than rendering or saying it twice.
            string cleanScreen = Regex.Replace(screen, @"```[\s\S]*?```", " ").Trim();
            string cleanSpeech = Regex.Replace(speech, @"```[\s\S]*?```", " ").Trim();
            return (cleanScreen, cleanSpeech, blocks);
        }

        var matches = Regex.Matches(screen,
            @"```(?<language>[a-zA-Z0-9+#-]*)[ \t]*\r?\n(?<code>[\s\S]*?)\r?\n?```",
            RegexOptions.CultureInvariant);
        if (matches.Count == 0) return (screen, speech, blocks ?? Array.Empty<NIRARichBlock>());

        var visible = new List<NIRARichBlock>();
        int position = 0;
        string intro = StripInlineMarkdown(screen[..matches[0].Index].Trim());
        foreach (Match match in matches.Cast<Match>().Take(3))
        {
            string before = screen[position..match.Index].Trim();
            if (position > 0 && before.Length > 0)
                visible.Add(new NIRARichBlock { Type = "text", Text = StripInlineMarkdown(before) });
            visible.Add(new NIRARichBlock { Type = "code", Language = match.Groups["language"].Value,
                Text = match.Groups["code"].Value.Trim() });
            position = match.Index + match.Length;
        }
        string after = screen[position..].Trim();
        if (after.Length > 0)
            visible.Add(new NIRARichBlock { Type = "text", Text = StripInlineMarkdown(after) });
        string narration = speech;
        if (string.IsNullOrWhiteSpace(narration) || narration == screen)
        {
            // The explanatory prose is still useful, but spoken code is not.
            narration = Regex.Replace(screen, @"```[\s\S]*?```", " ");
            narration = StripInlineMarkdown(narration).Trim();
            if (narration.Length > 420)
            {
                int sentence = narration.LastIndexOf('.', 419);
                narration = (sentence > 90 ? narration[..(sentence + 1)] : narration[..420]).Trim();
            }
        }
        var combined = blocks is { Count: > 0 }
            ? blocks.Concat(visible).ToArray()
            : visible.ToArray();
        return (intro.Length == 0 ? "Code example:" : intro, narration, Normalize(combined));
    }

    // Repair only genuinely under-informative narration when the requested
    // visuals contain enough grounded data to explain themselves. This is a
    // fast local fallback; it does not invent facts or run a second model call.
    // Substantive model-authored speech retains NIRA's own character/wording.
    public static string EnsureInformativeSpeech(
        string speech, IReadOnlyList<NIRARichBlock>? blocks)
    {
        if (blocks is null || blocks.Count == 0 ||
            Regex.Matches(speech ?? string.Empty, @"\b[\p{L}\p{N}]+\b").Count >= 19)
            return speech;

        var progress = blocks.FirstOrDefault(b => b.Type == "progress" &&
            b.ProgressValue is not null && b.ProgressMaximum is > 0);
        if (progress != null)
        {
            double done = progress.ProgressValue!.Value;
            double total = progress.ProgressMaximum!.Value;
            double rest = total - done;
            double percentage = done / total * 100;
            string unit = progress.Unit.Length == 0 ? "items" : progress.Unit;
            return $"You've completed {done:0.##} of {total:0.##} {unit}, " +
                $"so you're about {percentage:0.#} percent of the way through. " +
                $"That leaves {rest:0.##} {unit} to go.";
        }

        var timeline = blocks.FirstOrDefault(b => b.Type == "timeline" && b.Items.Count > 0);
        if (timeline != null)
        {
            var milestones = timeline.Items.Take(4)
                .Select(x => Regex.Replace(x, @"\s*[—–]\s*", ", ").Trim())
                .Select(x => x.TrimEnd('.', ';'))
                .Where(x => x.Length > 0).ToArray();
            if (milestones.Length > 0)
            {
                string ordered = milestones.Length == 1 ? milestones[0] :
                    string.Join("; ", milestones[..^1]) + "; and " + milestones[^1];
                string extra = timeline.Items.Count > 4 ?
                    " The remaining steps are on screen." : "";
                bool checklist = blocks.Any(b => b.Type == "checklist");
                return "The plan goes in this order: " + ordered + "." + extra +
                    (checklist ? " I've also put the tasks into a checklist so you can mark each one as you finish." : "");
            }
        }
        return speech;
    }

    private static string StripInlineMarkdown(string text)
        => text.Replace("**", "", StringComparison.Ordinal)
            .Replace("`", "", StringComparison.Ordinal);

    public static string Serialize(NIRAPresentationSnapshot snapshot) => JsonSerializer.Serialize(snapshot);
    public static NIRAPresentationSnapshot? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var v = JsonSerializer.Deserialize<NIRAPresentationSnapshot>(json);
            return v is null ? null : v with { Blocks = Normalize(v.Blocks) };
        }
        catch (JsonException) { return null; }
    }
}


