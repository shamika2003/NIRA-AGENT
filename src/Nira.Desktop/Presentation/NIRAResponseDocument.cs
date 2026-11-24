using System.Text.RegularExpressions;
using NIRAAgent.Presentation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace NIRAAgent.UI.Presentation;

/// <summary>Native selectable document layout. No XAML/HTML evaluation or automatic network content.</summary>
internal static class NIRAResponseDocument
{
    private static readonly string[] SectionAccents = ["#286BA9", "#8252A8", "#14765D", "#A35423", "#A34070", "#146A80"];

    public static FrameworkElement Create(string message)
    {
        var document = BuildDocument(message);
        var view = new RichTextBox { Document = document, IsReadOnly = true, IsDocumentEnabled = true,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Style = null, FocusVisualStyle = null, FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = 15, IsReadOnlyCaretVisible = false };
        view.SetResourceReference(Control.ForegroundProperty, "NIRATextBrush");
        return view;
    }

    internal static void Update(RichTextBox view, string message) => view.Document = BuildDocument(PreparePreview(message));

    // A provisional fragment may end halfway through a Markdown delimiter.
    // Keep partial table syntax out of the live view, never alter the saved answer.
    private static string PreparePreview(string message)
    {
        string[] lines = message.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        string? fence = null;
        bool table = false;
        for (int i = 0; i < lines.Length; i++)
        {
            string trimmed = lines[i].TrimStart();
            if (fence is not null)
            {
                if (ClosingFence(lines[i], fence)) fence = null;
                continue;
            }
            Match opening = OpeningFence(lines[i]);
            if (opening.Success) { fence = opening.Groups[1].Value; table = false; continue; }
            if (trimmed.StartsWith('|') || (IsTableRow(trimmed) && (table || (i + 1 < lines.Length && IsSeparator(lines[i + 1])))))
            {
                if (table)
                {
                    if (i == lines.Length - 1) lines[i] = "";
                    continue;
                }
                if (i + 1 < lines.Length && IsTableRow(trimmed) && IsSeparator(lines[i + 1]) &&
                    Cells(trimmed).Length == Cells(lines[i + 1]).Length)
                { table = true; i++; continue; }
                // Withhold an incomplete header or separator at the current stream boundary.
                if (i == lines.Length - 1 || (i == lines.Length - 2 &&
                    (string.IsNullOrWhiteSpace(lines[i + 1]) || lines[i + 1].TrimStart().StartsWith('|'))))
                { lines[i] = ""; if (i + 1 < lines.Length) lines[i + 1] = ""; continue; }
            }
            else table = false;
            if (i != lines.Length - 1) continue;
            // Hide a partially received color tag at the stream boundary. A complete
            // opening tag can already color its provisional text; saved text is untouched.
            int tagStart = lines[i].LastIndexOf('[');
            if (tagStart >= 0)
            {
                string tail = lines[i][tagStart..];
                if (!tail.Contains(']') && ("[color=".StartsWith(tail, StringComparison.OrdinalIgnoreCase) ||
                    "[/color]".StartsWith(tail, StringComparison.OrdinalIgnoreCase) ||
                    tail.StartsWith("[color=", StringComparison.OrdinalIgnoreCase)))
                    lines[i] = lines[i][..tagStart];
            }
            // Balance a trailing unfinished bold/italic/code span for preview only.
            if (Regex.Matches(lines[i], @"(?<!\*)\*\*(?!\*)").Count % 2 != 0)
                lines[i] = lines[i].EndsWith("**", StringComparison.Ordinal) ? lines[i][..^2] : lines[i] + "**";
            if (Regex.Matches(lines[i], @"(?<!\*)\*(?!\*)").Count % 2 != 0)
                lines[i] = lines[i].EndsWith('*') ? lines[i][..^1] : lines[i] + "*";
            if (lines[i].Count(c => c == '`') % 2 != 0)
                lines[i] = lines[i].EndsWith('`') ? lines[i][..^1] : lines[i] + "`";
            if (Regex.Matches(lines[i], @"\[color=#[0-9a-f]{6}\]", RegexOptions.IgnoreCase).Count >
                Regex.Matches(lines[i], @"\[/color\]", RegexOptions.IgnoreCase).Count)
                lines[i] += "[/color]";
            if (Regex.IsMatch(lines[i], @"^\s{0,3}#{1,6}$")) lines[i] = "";
        }
        return string.Join("\n", lines);
    }

    private static Match OpeningFence(string line) => Regex.Match(line, @"^\s{0,3}(`{3,}|~{3,})([^\r\n]*)$");
    private static bool ClosingFence(string line, string fence)
    {
        string trimmed = line.Trim();
        return trimmed.Length >= fence.Length && trimmed.All(c => c == fence[0]);
    }

    private static FlowDocument BuildDocument(string message)
    {
        var document = new FlowDocument { PagePadding = new Thickness(0), FontSize = 15, LineHeight = 25.5, FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI") };
        document.SetResourceReference(TextElement.ForegroundProperty, "NIRATextBrush");
        string[] lines = message.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        string[] accents = SectionAccents;
        int section = -1;
        string accent = accents[0];
        int i = 0;
        while (i < lines.Length)
        {
            string line = lines[i++];
            Match opening = OpeningFence(line);
            if (opening.Success)
            {
                string fence = opening.Groups[1].Value;
                string language = opening.Groups[2].Value.Trim();
                var code = new System.Text.StringBuilder();
                while (i < lines.Length && !ClosingFence(lines[i], fence))
                    code.Append(lines[i++]).Append('\n');
                if (i < lines.Length) i++;
                string codeText = code.Length == 0 ? string.Empty : code.ToString(0, code.Length - 1);
                var toolbar = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 2, 0, 0) };
                var copy = new Button { Content = "Copy code", FontSize = 11, Padding = new Thickness(9,4,9,4), Cursor = System.Windows.Input.Cursors.Hand };
                copy.Click += (_, _) => { try { Clipboard.SetText(codeText); } catch (System.Runtime.InteropServices.ExternalException) { } };
                DockPanel.SetDock(copy, Dock.Right); toolbar.Children.Add(copy);
                var label = new TextBlock { Text = language.Length == 0 ? "Code" : language, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
                label.SetResourceReference(TextBlock.ForegroundProperty, "NIRASecondaryBrush"); toolbar.Children.Add(label);
                document.Blocks.Add(new BlockUIContainer(toolbar) { Margin = new Thickness(0,0,0,4) });
                var block = new Paragraph() { FontFamily = new FontFamily("Cascadia Code, Consolas"), FontSize = 12.5, LineHeight = 21, Padding = new Thickness(12,10,12,10), Margin = new Thickness(0,0,0,17), BorderThickness = new Thickness(1) };
                NIRACodeHighlighter.Append(block, codeText, language);
                block.SetResourceReference(Block.BorderBrushProperty, "NIRAGlassBorderStrongBrush");
                document.Blocks.Add(block); continue;
            }
            if (TryDisplayMath(line, lines, ref i, out string formula))
            {
                document.Blocks.Add(new BlockUIContainer(NIRAMathEquationRenderer.Render(
                    new NIRARichBlock { Type = "equation", Text = formula })) { Margin = new Thickness(0, 2, 0, 9) });
                continue;
            }
            if (i < lines.Length && IsTableRow(line) && IsSeparator(lines[i]) && Cells(line).Length == Cells(lines[i]).Length)
            {
                var headings = Cells(line); i++;
                var table = new Table { CellSpacing = 0, Margin = new Thickness(0,8,0,20), BorderThickness = new Thickness(1) };
                table.SetResourceReference(Block.BorderBrushProperty, "NIRAGlassBorderStrongBrush");
                foreach (string _ in headings) table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
                var rows = new TableRowGroup(); table.RowGroups.Add(rows);
                AddRow(rows, headings, true, 0, accent);
                int rowIndex = 1;
                while (i < lines.Length && (IsTableRow(lines[i]) || lines[i].TrimStart().StartsWith('|')) &&
                    !IsSeparator(lines[i]) && !(i + 1 < lines.Length && IsSeparator(lines[i + 1])))
                {
                    string[] cells = Cells(lines[i]);
                    // Recover explicit pipe-delimited ragged rows without discarding content.
                    if (cells.Length != headings.Length && !lines[i].TrimStart().StartsWith('|')) break;
                    if (cells.Length < headings.Length)
                        cells = cells.Concat(Enumerable.Repeat("", headings.Length - cells.Length)).ToArray();
                    else if (cells.Length > headings.Length)
                        cells = cells.Take(headings.Length - 1).Append(string.Join(" | ", cells.Skip(headings.Length - 1))).ToArray();
                    AddRow(rows, cells, false, rowIndex++, accent);
                    i++;
                }
                document.Blocks.Add(table); continue;
            }
            if (string.IsNullOrWhiteSpace(line)) continue;
            Match heading = Regex.Match(line, @"^\s{0,3}(#{1,6})\s+(.+)$");
            if (heading.Success)
            {
                int level = heading.Groups[1].Length;
                if (level <= 3 || section < 0) accent = accents[++section % accents.Length];
                var h = ParagraphFor(Regex.Replace(heading.Groups[2].Value, @"\s+#+\s*$", ""), level switch { 1 => 24, 2 => 20, 3 => 17, _ => 15 }, true, accent);
                NIRAResponsePalette.SetCustom(h, accent);
                h.LineHeight = level == 1 ? 33 : 28;
                h.Margin = new Thickness(0, level == 1 ? 19 : 15, 0, 12);
                document.Blocks.Add(h); continue;
            }
            if (Regex.IsMatch(line, @"^\s*(?:-{3,}|\*{3,}|_{3,})\s*$"))
            {
                var rule = new Paragraph { BorderThickness = new Thickness(0,0,0,1), Margin = new Thickness(0,14,0,20) };
                rule.SetResourceReference(Block.BorderBrushProperty, "NIRAGlassBorderBrush"); document.Blocks.Add(rule); continue;
            }
            if (line.TrimStart().StartsWith('>'))
            {
                var quote = ParagraphFor(line.TrimStart()[1..].TrimStart(), 15, false, accent);
                quote.Padding = new Thickness(15,8,10,8); quote.BorderThickness = new Thickness(3,0,0,0);
                quote.SetResourceReference(Block.BorderBrushProperty, "NIRABlueBrush");
                quote.SetResourceReference(TextElement.BackgroundProperty, "NIRAGlassTintBrush");
                document.Blocks.Add(quote); continue;
            }
            Match bullet = Regex.Match(line, @"^(\s*)([-*+]|\d{1,9}[.)])\s+(.*)$");
            if (bullet.Success)
            {
                string mark = bullet.Groups[2].Value;
                var item = new Paragraph { FontSize = 15, LineHeight = 25, TextIndent = -24,
                    Margin = new Thickness(28 + Math.Min(48, bullet.Groups[1].Length * 6), 2, 0, 7) };
                var marker = new Run((mark.Length == 1 ? "•" : mark) + "  ") { FontWeight = FontWeights.SemiBold };
                marker.SetResourceReference(TextElement.ForegroundProperty, "NIRABlueBrush");
                item.Inlines.Add(marker);
                NIRAStyledChatText.AppendInline(item.Inlines, bullet.Groups[3].Value, fallbackColor: accent);

                document.Blocks.Add(item); continue;
            }
            // The original visual hierarchy also distinguishes a short standalone bold title.
            if (line.Trim().StartsWith("**", StringComparison.Ordinal) && line.Trim().EndsWith("**", StringComparison.Ordinal) &&
                line.Trim().Length <= 125 && (i == lines.Length || string.IsNullOrWhiteSpace(lines[i]) || IsTableRow(lines[i])))
            {
                var title = ParagraphFor(line.Trim(), 17, true, accent);
                NIRAResponsePalette.SetCustom(title, accent);
                title.Margin = new Thickness(0,8,0,10); document.Blocks.Add(title); continue;
            }
            var prose = ParagraphFor(line, 15, false, accent);
            while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]) && !StartsDisplayMath(lines[i]) &&
                !Regex.IsMatch(lines[i], @"^\s*(?:#{1,6}\s|[-*+]\s|\d{1,9}[.)]\s|>|`{3,}|~{3,}|[-*_]{3,}\s*$)") && !(i + 1 < lines.Length && IsTableRow(lines[i]) && IsSeparator(lines[i + 1])))
            { prose.Inlines.Add(new LineBreak()); NIRAStyledChatText.AppendInline(prose.Inlines, lines[i++], fallbackColor: accent); }
            document.Blocks.Add(prose);
        }
        return document;
    }

    private static bool StartsDisplayMath(string line) => line.TrimStart().StartsWith(@"\[", StringComparison.Ordinal) ||
        line.TrimStart().StartsWith("$$", StringComparison.Ordinal);

    private static bool TryDisplayMath(string line, string[] lines, ref int next, out string formula)
    {
        formula = "";
        string start = line.Trim();
        if (!StartsDisplayMath(start)) return false;
        string closing = start.StartsWith("$$", StringComparison.Ordinal) ? "$$" : @"\]";
        var content = new System.Text.StringBuilder(start[2..]);
        int probe = next;
        while (!content.ToString().TrimEnd().EndsWith(closing, StringComparison.Ordinal) &&
            probe < lines.Length && probe - next < 60 && content.Length < 12000)
            content.Append('\n').Append(lines[probe++]);
        string value = content.ToString().TrimEnd();
        if (!value.EndsWith(closing, StringComparison.Ordinal)) return false;
        formula = value[..^2].Trim();
        if (formula.Length == 0) return false;
        next = probe;
        return true;
    }

    private static Paragraph ParagraphFor(string text, double size, bool bold, string accent = "#286BA9")
    {
        var p = new Paragraph { FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, LineHeight = size >= 17 ? 28 : 25.5, Margin = new Thickness(0,0,0,17) };
        NIRAStyledChatText.AppendInline(p.Inlines, text, fallbackColor: accent);

        return p;
    }
    private static bool IsTableRow(string line) => line.Contains('|') && Cells(line).Length is >= 2 and <= 12;
    private static string[] Cells(string line)
    {
        string text = line.Trim();
        if (text.StartsWith('|')) text = text[1..];
        if (text.EndsWith('|') && !text.EndsWith(@"\|", StringComparison.Ordinal)) text = text[..^1];
        // Ignore pipes inside inline code; escaped delimiters remain literal.
        var cells = new List<string>();
        int start = 0, ticks = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length) { i++; continue; }
            if (text[i] == '`')
            {
                int end = i + 1;
                while (end < text.Length && text[end] == '`') end++;
                int count = end - i;
                if (ticks == 0) ticks = count;
                else if (ticks == count) ticks = 0;
                i = end - 1; continue;
            }
            if (text[i] == '|' && ticks == 0) { cells.Add(text[start..i].Trim()); start = i + 1; }
        }
        cells.Add(text[start..].Trim());
        return cells.ToArray();
    }
    private static bool IsSeparator(string line) => IsTableRow(line) && Cells(line).All(cell => Regex.IsMatch(cell, @"^:?-{3,}:?$"));
    private static void AddRow(TableRowGroup rows, string[] values, bool header, int rowIndex, string accent)
    {
        var row = new TableRow();
        if (header || rowIndex % 2 == 0) row.SetResourceReference(TextElement.BackgroundProperty, header ? "NIRAGlassStrongTintBrush" : "NIRAGlassTintBrush");
        for (int column = 0; column < values.Length; column++)
        {
            string cellAccent = values.Length >= 3 && column > 0 ? SectionAccents[(column - 1) % SectionAccents.Length] : accent;
            var paragraph = ParagraphFor(values[column], 13.5, header || column == 0, cellAccent); paragraph.Margin = new Thickness(0); paragraph.LineHeight = 21;
            if (header && column > 0) NIRAResponsePalette.SetCustom(paragraph, cellAccent);
            var cell = new TableCell(paragraph) { Padding = new Thickness(10,10,10,10), BorderThickness = new Thickness(0,0,column == values.Length - 1 ? 0 : 1,1) };
            cell.SetResourceReference(TableCell.BorderBrushProperty, "NIRAGlassBorderBrush");
            row.Cells.Add(cell);
        }
        rows.Rows.Add(row);
    }
}
