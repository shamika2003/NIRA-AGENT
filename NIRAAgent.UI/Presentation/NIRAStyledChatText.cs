using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace NIRAAgent.UI.Presentation;

/// <summary>
/// Safely displays NIRA's screen-only Markdown in the existing assistant TextBlock.
/// The structured NIRARichBlock renderer remains responsible for tables/cards/etc.
/// This is presentation only: no URL actions, HTML, or model/tool execution.
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

            if (Regex.IsMatch(line, @"^\s*---+\s*$"))
            {
                var separator = new Run("────────────");
                separator.SetResourceReference(TextElement.ForegroundProperty, "NIRAMutedBrush");
                block.Inlines.Add(separator);
                continue;
            }

            AppendInline(block.Inlines, line);
        }
    }

    private static void AppendInline(InlineCollection inlines, string text)
    {
        // Only a bounded set of visible inline notations is interpreted.
        // Everything else remains literal text, including raw HTML and links.
        var matches = Regex.Matches(text, @"\*\*(?<bold>.+?)\*\*|(?<!\*)\*(?<italic>[^*\n]+?)\*(?!\*)|`(?<code>[^`\n]+)`");
        int offset = 0;
        foreach (Match match in matches)
        {
            if (match.Index > offset) inlines.Add(new Run(text[offset..match.Index]));
            if (match.Groups["bold"].Success)
                inlines.Add(new Bold(new Run(match.Groups["bold"].Value)));
            else if (match.Groups["italic"].Success)
                inlines.Add(new Italic(new Run(match.Groups["italic"].Value)));
            else
            {
                var code = new Run(match.Groups["code"].Value) { FontFamily = new FontFamily("Cascadia Code, Consolas"),
                    FontWeight = FontWeights.SemiBold };
                code.SetResourceReference(TextElement.ForegroundProperty, "NIRACyanBrush");
                inlines.Add(code);
            }
            offset = match.Index + match.Length;
        }
        if (offset < text.Length) inlines.Add(new Run(text[offset..]));
    }
}
