using System.Text.RegularExpressions;

namespace NIRAAgent.Presentation;

// Declarative table layout only. Shared by core regression checks and WPF UI;
// no conversation, model calls, cognition, tool access, or personality state.
public static class NIRATableCellLayout
{
    // Account for the rare model that double-escapes visual newlines in a table
    // cell (literal backslash+n). Only convert a visible list/paragraph break,
    // not a Windows path such as C:\new or an arbitrary backslash sequence.
    private static readonly Regex VisibleBreak = new(
        @"(?<![A-Za-z]:)(?<!\\)\\(?:r\\n|n|r)(?=\s*(?:[•●▪◦‣*\-]|\d+[.)]\s|[A-Z])|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string ScreenCellText(string? value)
    {
        string text = (value ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        return VisibleBreak.Replace(text, "\n");
    }

    // WPF can make every column fit with wrapped cells as long as its viewport
    // is this wide. The nested horizontal scrollbar remains available when the
    // window is narrower or the table has many columns.
    public static double MinimumGridWidth(int count) => Math.Clamp(count, 0, 8) * 104.0;

    // Allocate space from actual header/cell length, not a special-case topic
    // (such as Java/C#/Python). Cap the influence of very long descriptions;
    // they wrap and increase ROW height instead of expanding COLUMN width.
    public static IReadOnlyList<double> ColumnWeights(
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var widths = new double[headers.Count];
        for (int col = 0; col < headers.Count; col++)
        {
            int length = VisualLineLength(headers[col]);
            foreach (var row in rows)
                if (col < row.Count)
                    length = Math.Max(length, VisualLineLength(row[col]));
            widths[col] = Math.Clamp(Math.Sqrt(length + 8), 3.5, 8.5);
        }
        return widths;
    }

    private static int VisualLineLength(string? raw)
    {
        string value = ScreenCellText(raw);
        int longest = 0;
        foreach (string line in value.Split('\n'))
            longest = Math.Max(longest, line.Length);
        return Math.Min(longest, 100);
    }
}
