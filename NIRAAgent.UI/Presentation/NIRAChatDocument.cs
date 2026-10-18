using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Text.RegularExpressions;

namespace NIRAAgent.UI.Presentation;

/// <summary>
/// Layout-only document typography. Reuses the trusted NIRAStyledChatText
/// inline parser for clickable web links, emphasis and literal code; it never
/// interprets or executes model-supplied XAML/HTML and never fetches resources.
/// The model may emit ordinary speech; visual hierarchy is only applied where
/// explicit line breaks/Markdown delimiters supply a structure.
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

    private static void OnChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not StackPanel root) return;
        root.Children.Clear();
        string source = ((string?)e.NewValue ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
        if (string.IsNullOrWhiteSpace(source)) return;

        string[] lines = source.Split('\n');
        var paragraph = new List<string>();
        var code = new List<string>();
        bool codeFence = false;

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
                Padding = new Thickness(0), Margin = new Thickness(0, 3, 0, 14),
                BorderThickness = new Thickness(1), Child = body };
            frame.SetResourceReference(Border.BackgroundProperty, "NIRAGlassStrongTintBrush");
            frame.SetResourceReference(Border.BorderBrushProperty, "NIRAGlassBorderBrush");
            root.Children.Add(frame);
            code.Clear();
        }

        foreach (string original in lines)
        {
            string line = original.TrimEnd();
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph();
                if (codeFence) FlushCode();
                codeFence = !codeFence;
                continue;
            }
            if (codeFence) { code.Add(line); continue; }
            if (string.IsNullOrWhiteSpace(line)) { FlushParagraph(); continue; }

            Match heading = Regex.Match(line, @"^\s{0,3}(#{1,3})\s+(.+)$");
            if (heading.Success)
            {
                FlushParagraph();
                int depth = heading.Groups[1].Length;
                var title = MakeText(heading.Groups[2].Value, depth == 1 ? 23 : depth == 2 ? 19 : 16,
                    depth == 1 ? 33 : 28, new Thickness(0, depth == 1 ? 19 : 15, 0, 12));
                title.FontWeight = FontWeights.SemiBold;
                if (depth <= 2) title.SetResourceReference(TextBlock.ForegroundProperty, "NIRACyanBrush");
                root.Children.Add(title);
                continue;
            }
            Match bullet = Regex.Match(line, @"^\s{0,3}(?<mark>[-*+]|\d{1,3}[.)])\s+(?<body>.*)$");
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
