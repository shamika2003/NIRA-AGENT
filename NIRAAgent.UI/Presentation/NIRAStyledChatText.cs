using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using NIRAAgent.Presentation;

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
        string text = NIRAMathMarkup.ToReadableText(
            (e.NewValue as string ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n'));
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
                h.SetResourceReference(TextElement.ForegroundProperty, "NIRACyanBrush");
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
        @"\[(?<label>[^\]\r\n]{1,180})\]\((?<link>https?://[^\s)<>]{1,1900})\)|(?<url>https?://[^\s<>]+)|(?<![a-z0-9_@./:-])(?<domain>(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+(?:com|org|net|gov|edu|mil|int|io|ai|dev|info|app|lk|uk|me|co|tv)(?:/[^\s<>]*)?)|\*\*(?<bold>[^*\r\n]+)\*\*|(?<![\p{L}\p{N}_*])\*(?<italic>(?:[^\s*\r\n]|[^\s*\r\n][^*\r\n]*[^\s*\r\n]))\*(?![\p{L}\p{N}_*])|`(?<code>[^`\r\n]+)`",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);


    private static void AppendInline(InlineCollection inlines, string text)
    {
        int offset = 0;
        foreach (Match match in InlineTokens.Matches(text))
        {
            if (match.Index > offset)
                inlines.Add(new Run(text[offset..match.Index]));

            if (match.Groups["link"].Success)
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
            else if (match.Groups["bold"].Success)
            {
                // Bold text can itself contain an observed Markdown URL.
                // Parse its child inlines rather than showing raw [label](url).
                // Links are still HTTPS/HTTP and user-click-only in AddLink.
                var bold = new Span { FontWeight = FontWeights.SemiBold };
                AppendInline(bold.Inlines, match.Groups["bold"].Value);
                inlines.Add(bold);
            }
            else if (match.Groups["italic"].Success)
                inlines.Add(new Italic(new Run(match.Groups["italic"].Value)));
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
        if (offset < text.Length) inlines.Add(new Run(text[offset..]));
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
            ToolTip = uri.AbsoluteUri
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
