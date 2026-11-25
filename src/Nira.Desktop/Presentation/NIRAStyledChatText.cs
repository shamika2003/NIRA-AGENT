using System.Text.RegularExpressions;
using NIRAAgent.Presentation;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace NIRAAgent.UI.Presentation;

/// <summary>
/// Safely displays NIRA's screen-only Markdown in the existing assistant TextBlock.
/// The structured NIRARichBlock renderer remains responsible for tables/cards/etc.
/// This is presentation only: user-clicked HTTPS/HTTP URLs may open in the system browser; never navigate on render.
/// </summary>
public static class NIRAStyledChatText
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached("Text", typeof(string),
            typeof(NIRAStyledChatText), new PropertyMetadata(string.Empty, OnTextChanged));

    public static void SetText(DependencyObject target, string value) =>
        target.SetValue(TextProperty, value);

    public static string GetText(DependencyObject target) =>
        (string)target.GetValue(TextProperty);

    private static void OnTextChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBlock block) return;
        block.Inlines.Clear();
        string text = (e.NewValue as string ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        if (text.Length == 0) return;

        // Preserve ordinary natural conversation. Interpret only explicit Markdown.
        string[] lines = text.Split('\n');
        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            if (lineIndex > 0) block.Inlines.Add(new LineBreak());
            string line = lines[lineIndex];
            Match heading = Regex.Match(line, @"^\s{0,3}(#{1,3})\s+(.*)$");
            if (heading.Success)
            {
                var h = new Span { FontWeight = FontWeights.SemiBold,
                    FontSize = heading.Groups[1].Length == 1 ? 19 :
                        heading.Groups[1].Length == 2 ? 17 : 15 };
                AppendInline(h.Inlines, heading.Groups[2].Value);
                block.Inlines.Add(h);
                continue;
            }

            Match bullet = Regex.Match(line, @"^\s{0,3}(?:[-*+]\s+|\d{1,3}[.)]\s+)(.*)$");
            if (bullet.Success)
            {
                string prefix = Regex.IsMatch(line, @"^\s*\d{1,3}[.)]")
                    ? Regex.Match(line, @"^\s*\d{1,3}[.)]").Value.Trim() + "  "
                    : "•  ";
                var mark = new Run(prefix) { FontWeight = FontWeights.SemiBold };
                mark.SetResourceReference(TextElement.ForegroundProperty, "NIRACyanBrush");
                block.Inlines.Add(mark);
                AppendInline(block.Inlines, bullet.Groups[1].Value);
                continue;
            }

            if (Regex.IsMatch(line, @"^\s*(?:-{3,}|\*{3,}|_{3,})\s*$"))
            {
                var separator = new Run("────────────");
                separator.SetResourceReference(TextElement.ForegroundProperty, "NIRAMutedBrush");
                block.Inlines.Add(separator);
                continue;
            }

            AppendInline(block.Inlines, line);
        }
    }

    // Markdown links, absolute URLs and unambiguous public hostnames are
    // clickable only after a user clicks. Do not fetch/preview external content.
    // Restrict scheme-less links to common web TLDs so a filename such as
    // photo.png or NIRAAgent.dll does not become a misleading website link.
    private static readonly Regex InlineTokens = new(
        @"(?<math>\\\([^\r\n]{1,1600}?\\\)|(?<![\\$])\$(?!\$)[^$\r\n]{1,350}\$(?!\$))|(?<break><br\s*/?>)|(?<keycap>[0-9#*]\uFE0F?\u20E3)|(?<escape>\\[\\`*_{}\[\]()#+.!|>~-])|`(?<code>[^`\r\n]+)`|\[color=(?<rgb>#[0-9a-f]{6})\](?<colored>[^\r\n]*?)\[/color\]|\[(?<label>[^\]\r\n]{1,180})\]\((?<link>https?://[^\s)<>]{1,1900})\)|\*\*\*(?<both>[^*\r\n]+)\*\*\*|\*\*(?<bold>(?:[^*\r\n]|\*(?!\*))+?\*?)\*\*(?!\*)|__(?<underbold>[^\r\n]+?)__|(?<![\p{L}\p{N}_*])\*(?![\s*])(?<italic>(?:\*\*[^*\r\n]+\*\*|[^*\r\n])+?)\*(?![\p{L}\p{N}_*])|(?<![\p{L}\p{N}_])_(?<underitalic>[^_\r\n]+)_(?![\p{L}\p{N}_])|~~(?<strike>[^~\r\n]+)~~|(?<url>https?://[^\s<>*]+)|(?<![a-z0-9_@./:-])(?<domain>(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+(?:com|org|net|gov|edu|mil|int|io|ai|dev|info|app|lk|uk|me|co|tv)(?:/[^\s<>]*)?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);


    private static readonly Regex ColorMarkup = new(@"(?<code>`+[^`\r\n]*`+)|(?<open>\[\s*color\s*=\s*[""']?(?<hex>#[0-9a-f]{6})[""']?\s*\])|(?<close>\[\s*/\s*color\s*\])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex CrossingColor = new(@"(?<em>\*{1,3}|_{1,2})(?<tag>\[color=#[0-9a-f]{6}\])(?<value>(?:(?!\[/?color)[^\r\n])*?)\k<em>\[/color\]|(?<tag2>\[color=#[0-9a-f]{6}\])(?<em2>\*{1,3}|_{1,2})(?<value2>(?:(?!\[/?color)[^\r\n])*?)\[/color\]\k<em2>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    private static string NormalizeColorMarkup(string text)
    {
        // Protect literal inline code when repairing model presentation tags.
        string[] parts = Regex.Split(text, @"(`+[^`\r\n]*`+)", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        for (int i = 0; i < parts.Length; i += 2)
        {
            string value = ColorMarkup.Replace(parts[i], m => m.Groups["open"].Success ? "[color=" + m.Groups["hex"].Value + "]" : m.Groups["close"].Success ? "[/color]" : m.Value);
            value = CrossingColor.Replace(value, m => m.Groups["tag"].Success
                ? m.Groups["tag"].Value + m.Groups["em"].Value + m.Groups["value"].Value + m.Groups["em"].Value + "[/color]"
                : m.Groups["tag2"].Value + m.Groups["em2"].Value + m.Groups["value2"].Value + m.Groups["em2"].Value + "[/color]");
            parts[i] = value;
        }
        return string.Concat(parts);
    }

    private static string VisibleText(string value) => ColorMarkup.Replace(value, m => m.Groups["code"].Success ? m.Value : "");

    internal static void AppendInline(InlineCollection inlines, string text, int depth = 0, bool explicitColor = false, string fallbackColor = "#286BA9")
    {
        if (depth == 0) text = NormalizeColorMarkup(text);
        if (depth >= 12) { inlines.Add(new Run(VisibleText(text))); return; }
        int offset = 0;
        foreach (Match match in InlineTokens.Matches(text))
        {
            if (match.Index > offset)
                inlines.Add(new Run(VisibleText(text[offset..match.Index])));

            if (match.Groups["math"].Success)
            {
                string raw = match.Value;
                // The helper deliberately preserves ambiguous currency expressions.
                if (raw.StartsWith('$') && NIRAMathMarkup.ToReadableText(raw) == raw)
                    inlines.Add(new Run(raw));
                else
                {
                    string expression = raw.StartsWith('$') ? raw[1..^1] : raw[2..^2];
                    inlines.Add(new InlineUIContainer(NIRAMathEquationRenderer.RenderInline(expression))
                    { BaselineAlignment = BaselineAlignment.Center });
                }
            }
            else if (match.Groups["break"].Success)
                inlines.Add(new LineBreak());
            else if (match.Groups["keycap"].Success)
                inlines.Add(new Run(match.Value[0] + ". "));
            else if (match.Groups["escape"].Success)
                inlines.Add(new Run(match.Value[1..]));
            else if (match.Groups["colored"].Success)
            {
                var span = new Span();
                NIRAResponsePalette.SetCustom(span, match.Groups["rgb"].Value);
                AppendInline(span.Inlines, match.Groups["colored"].Value, depth + 1, true, fallbackColor);
                inlines.Add(span);
            }
            else if (match.Groups["link"].Success)
            {
                AddLink(inlines, match.Groups["label"].Value, match.Groups["link"].Value);
            }
            else if (match.Groups["url"].Success)
            {
                // Keep punctuation belonging to prose outside the URL.
                string original = match.Groups["url"].Value;
                string url = original.TrimEnd('.', ',', ';', '!', '?', ')', ']', '}');
                AddLink(inlines, url, url);
                if (url.Length < original.Length)
                    inlines.Add(new Run(original[url.Length..]));
            }
            else if (match.Groups["domain"].Success)
            {
                string original = match.Groups["domain"].Value;
                string hostnameAndPath = original.TrimEnd('.', ',', ';', '!', '?', ')', ']', '}');
                // Explicit HTTPS is important: WPF Hyperlink only accepts a
                // validated absolute web URI. Never infer a non-web scheme.
                AddLink(inlines, hostnameAndPath, "https://" + hostnameAndPath);
                if (hostnameAndPath.Length < original.Length)
                    inlines.Add(new Run(original[hostnameAndPath.Length..]));
            }
            else if (match.Groups["both"].Success || match.Groups["bold"].Success || match.Groups["underbold"].Success ||
                match.Groups["italic"].Success || match.Groups["underitalic"].Success || match.Groups["strike"].Success)
            {
                string kind = new[] { "both", "bold", "underbold", "italic", "underitalic", "strike" }
                    .First(key => match.Groups[key].Success);
                var span = new Span();
                if (kind is "both" or "bold" or "underbold") span.FontWeight = FontWeights.Bold;
                if (kind is "both" or "italic" or "underitalic") span.FontStyle = FontStyles.Italic;
                if (kind == "strike") span.TextDecorations = TextDecorations.Strikethrough;
                if (!explicitColor && (kind is "both" or "bold" or "underbold") && match.Groups[kind].Value.Length is > 1 and <= 72 &&
                    !match.Groups[kind].Value.Contains("[color=", StringComparison.OrdinalIgnoreCase))
                    NIRAResponsePalette.SetCustom(span, fallbackColor);
                AppendInline(span.Inlines, match.Groups[kind].Value, depth + 1, explicitColor, fallbackColor);
                inlines.Add(span);
            }
            else if (match.Groups["code"].Success)
            {
                var code = new Run(match.Groups["code"].Value)
                {
                    FontFamily = new FontFamily("Cascadia Code, Consolas"),
                    FontWeight = FontWeights.SemiBold
                };
                code.SetResourceReference(TextElement.ForegroundProperty, "NIRACyanBrush");
                inlines.Add(code);
            }
            offset = match.Index + match.Length;
        }
        if (offset < text.Length) inlines.Add(new Run(VisibleText(text[offset..])));
    }

    private static void AddLink(InlineCollection inlines, string label, string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            inlines.Add(new Run(label));
            return;
        }

        // Host-validated clickable link. The original user/LLM text is still
        // untrusted; it cannot create file:, javascript:, cmd: or shell actions.
        var link = new Hyperlink(new Run(label))
        {
            NavigateUri = uri,
            Cursor = System.Windows.Input.Cursors.Hand,
            TextDecorations = TextDecorations.Underline,
            ToolTip = uri.AbsoluteUri + "\nMessage-supplied link; the renderer has not verified the source."
        };
        link.SetResourceReference(TextElement.ForegroundProperty, "NIRACyanBrush");
        link.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = uri.AbsoluteUri,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NIRA-CHAT-LINK] Open failed: {ex.GetType().Name}");
            }
        };
        inlines.Add(link);
    }
}
