using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NIRAAgent.Presentation;

namespace NIRAAgent.UI.Presentation;

/// <summary>
/// Safe document layout for assistant prose. Explicit Markdown is rendered as
/// native WPF controls; ordinary conversation remains ordinary paragraphs.
/// Typed displayBlocks remain the preferred format for tabs/cards/timelines.
/// This parser never executes model-authored markup or invents facts.
/// </summary>
public static class NIRAChatDocument
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached("Text", typeof(string), typeof(NIRAChatDocument),
            new PropertyMetadata(string.Empty, OnChanged));

    public static void SetText(DependencyObject element, string value) =>
        element.SetValue(TextProperty, value);
    public static string GetText(DependencyObject element) =>
        (string)element.GetValue(TextProperty);

    private static readonly Regex Heading = new(@"^\s{0,3}(#{1,4})\s+(.+)$",
        RegexOptions.CultureInvariant);
    private static readonly Regex Bullet = new(@"^\s{0,3}(?<mark>[-*+]|\d{1,3}[.)])\s+(?<body>.*)$",
        RegexOptions.CultureInvariant);
    private static readonly Regex Task = new(@"^\s{0,3}[-*+]\s+\[(?<done>[ xX])\]\s+(?<body>.+)$",
        RegexOptions.CultureInvariant);
    private static readonly Regex TableSeparator = new(@"^:?-{3,}:?$",
        RegexOptions.CultureInvariant);

    private static void OnChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not StackPanel root) return;
        root.Children.Clear();
        string source = ((string?)e.NewValue ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        if (string.IsNullOrWhiteSpace(source)) return;

        string[] lines = source.Split('\n');
        var paragraph = new List<string>();
        var code = new List<string>();
        bool inCode = false;

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            string text = string.Join("\n", paragraph).Trim();
            paragraph.Clear();
            if (text.Length > 0)
                root.Children.Add(MakeText(text, 14, 24.5, new Thickness(0, 0, 0, 17)));
        }
        void FlushCode()
        {
            if (code.Count == 0) return;
            var body = MakeText(string.Join("\n", code), 12.5, 21,
                new Thickness(12, 10, 12, 10));
            body.FontFamily = new FontFamily("Cascadia Code, Consolas");
            var frame = new Border { CornerRadius = new CornerRadius(10),
                Margin = new Thickness(0, 3, 0, 14), BorderThickness = new Thickness(1), Child = body };
            frame.SetResourceReference(Border.BackgroundProperty, "NIRAGlassStrongTintBrush");
            frame.SetResourceReference(Border.BorderBrushProperty, "NIRAGlassBorderBrush");
            root.Children.Add(frame);
            code.Clear();
        }

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd();
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph();
                if (inCode) FlushCode();
                inCode = !inCode;
                continue;
            }
            if (inCode) { code.Add(line); continue; }
            if (string.IsNullOrWhiteSpace(line)) { FlushParagraph(); continue; }

            // Markdown table: recognize an actual header + separator before
            // converting anything. Never guess that pipe-delimited prose is data.
            if (i + 2 < lines.Length &&
                TryTableHeader(lines[i], lines[i + 1], out string[] headers))
            {
                var rows = new List<IReadOnlyList<string>>();
                int next = i + 2;
                while (next < lines.Length && rows.Count < 40 &&
                       TryCells(lines[next], out string[] cells) && cells.Length == headers.Length)
                {
                    rows.Add(cells);
                    next++;
                }
                if (rows.Count > 0)
                {
                    FlushParagraph();
                    root.Children.Add(NIRARichBlockRenderer.Render(new NIRARichBlock
                    {
                        Type = "table", Columns = headers, Rows = rows
                    }));
                    i = next - 1;
                    continue;
                }
            }

            // Markdown task syntax has real interactive checkboxes. This is
            // local display state, not a NIRA goal, action, or claim of completion.
            if (Task.IsMatch(line))
            {
                var items = new List<string>();
                var checkedIndices = new List<int>();
                int next = i;
                while (next < lines.Length && items.Count < 24)
                {
                    Match match = Task.Match(lines[next]);
                    if (!match.Success) break;
                    if (!string.Equals(match.Groups["done"].Value, " ", StringComparison.Ordinal))
                        checkedIndices.Add(items.Count);
                    items.Add(match.Groups["body"].Value.Trim());
                    next++;
                }
                FlushParagraph();
                root.Children.Add(NIRARichBlockRenderer.Render(new NIRARichBlock
                {
                    Type = "checklist", Title = "Checklist", Items = items
                }, new NIRARichInteractionState { CheckedIndices = checkedIndices }));
                i = next - 1;
                continue;
            }

            // A standalone Markdown source/reference is a compact themed
            // clickable resource row. Do not mistake an inline prose link for
            // a source card or imply that an LLM-authored URL was verified.
            if (NIRAResourceLinkPolicy.TryParse(line, out var resource) && resource != null)
            {
                FlushParagraph();
                root.Children.Add(MakeReferenceRow(resource));
                continue;
            }

            Match heading = Heading.Match(line);
            if (heading.Success)
            {
                FlushParagraph();
                int depth = heading.Groups[1].Length;
                double size = depth switch { 1 => 23, 2 => 19, 3 => 16, _ => 14.5 };
                var title = MakeText(heading.Groups[2].Value, size,
                    depth == 1 ? 33 : 28,
                    new Thickness(0, depth == 1 ? 19 : 15, 0, 12));
                title.FontWeight = FontWeights.SemiBold;
                if (depth <= 2)
                    title.SetResourceReference(TextBlock.ForegroundProperty, "NIRACyanBrush");
                root.Children.Add(title);
                continue;
            }
            Match bullet = Bullet.Match(line);
            if (bullet.Success)
            {
                FlushParagraph();
                var grid = new Grid { Margin = new Thickness(4, 4, 3, 9) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                string marker = Regex.IsMatch(bullet.Groups["mark"].Value, @"\d")
                    ? bullet.Groups["mark"].Value : "•";
                var symbol = MakeText(marker, 13.5, 24, new Thickness(0));
                symbol.FontWeight = FontWeights.SemiBold;
                symbol.SetResourceReference(TextBlock.ForegroundProperty, "NIRACyanBrush");
                var body = MakeText(bullet.Groups["body"].Value, 14, 24, new Thickness(0));
                Grid.SetColumn(body, 1);
                grid.Children.Add(symbol); grid.Children.Add(body);
                root.Children.Add(grid);
                continue;
            }
            if (Regex.IsMatch(line, @"^\s*---+\s*$"))
            {
                FlushParagraph();
                var rule = new Border { Height = 1, Margin = new Thickness(0, 14, 0, 20),
                    HorizontalAlignment = HorizontalAlignment.Stretch };
                rule.SetResourceReference(Border.BackgroundProperty, "NIRAGlassBorderBrush");
                root.Children.Add(rule);
                continue;
            }
            if (line.TrimStart().StartsWith('>'))
            {
                FlushParagraph();
                var quote = MakeText(line.TrimStart().TrimStart('>').TrimStart(), 13.5, 22,
                    new Thickness(15, 9, 9, 9));
                var panel = new Border { Child = quote, BorderThickness = new Thickness(3, 0, 0, 0),
                    Margin = new Thickness(0, 5, 0, 15) };
                panel.SetResourceReference(Border.BorderBrushProperty, "NIRACyanBrush");
                root.Children.Add(panel);
                continue;
            }
            paragraph.Add(line.Trim());
        }
        FlushParagraph(); FlushCode();
    }

    private static Border MakeReferenceRow(NIRAResourceLinkPresentation resource)
    {
        var panel = new StackPanel { Margin = new Thickness(13, 9, 13, 9) };
        var heading = MakeText(resource.Kind + "  /  " + resource.Host, 10.5, 16,
            new Thickness(0, 0, 0, 5));
        heading.FontWeight = FontWeights.SemiBold;
        heading.SetResourceReference(TextBlock.ForegroundProperty, "NIRASecondaryBrush");
        panel.Children.Add(heading);
        var link = MakeText($"[{resource.Label}  ↗]({resource.Address})", 13, 21,
            new Thickness(0));
        link.FontWeight = FontWeights.SemiBold;
        panel.Children.Add(link);
        var frame = new Border { Child = panel, CornerRadius = new CornerRadius(11),
            HorizontalAlignment = HorizontalAlignment.Left,
            MaxWidth = 540, BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 3, 0, 14) };
        frame.SetResourceReference(Border.BackgroundProperty, "NIRAGlassStrongTintBrush");
        frame.SetResourceReference(Border.BorderBrushProperty, "NIRAGlassBorderBrush");
        return frame;
    }

    // Only explicit Markdown table syntax is accepted: a heading row, a
    // --- separator row, then at least one complete data row.
    private static bool TryTableHeader(string header, string separator, out string[] columns)
    {
        columns = Array.Empty<string>();
        if (!TryCells(header, out string[] cells) || cells.Length < 2 || cells.Length > 8 ||
            !TryCells(separator, out string[] separators) || separators.Length != cells.Length ||
            separators.Any(cell => !TableSeparator.IsMatch(cell)) ||
            cells.Any(string.IsNullOrWhiteSpace)) return false;
        columns = cells;
        return true;
    }

    private static bool TryCells(string line, out string[] cells)
    {
        cells = Array.Empty<string>();
        string content = line.Trim();
        if (!content.Contains('|')) return false;
        // Split on unescaped bars. A displayed literal \| stays in its cell.
        var parts = Regex.Split(content, @"(?<!\\)\|").ToList();
        if (content.StartsWith('|')) parts.RemoveAt(0);
        if (content.EndsWith('|')) parts.RemoveAt(parts.Count - 1);
        if (parts.Count is < 2 or > 8) return false;
        cells = parts.Select(cell => cell.Trim().Replace(@"\|", "|", StringComparison.Ordinal))
            .ToArray();
        return true;
    }

    private static TextBlock MakeText(string value, double size, double lineHeight, Thickness margin)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = size,
            LineHeight = lineHeight, Margin = margin, HorizontalAlignment = HorizontalAlignment.Stretch,
            TextTrimming = TextTrimming.None };
        text.SetResourceReference(TextBlock.ForegroundProperty, "NIRATextBrush");
        NIRAStyledChatText.SetText(text, value);
        return text;
    }
}
