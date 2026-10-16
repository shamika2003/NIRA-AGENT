using System.Globalization;
using System.Text;

namespace NIRAAgent.Capabilities;

// Trusted, deterministic evidence for a COMPLETE read of an ordinary delimited
// file. Never estimates partial files, ignores malformed rows, or coerces mixed
// text into numbers. Numeric identifiers may be reported as numeric columns;
// the agent must still decide whether summation is meaningful for the user.
internal static class NIRADelimitedNumericAudit
{
    internal static string? TryAudit(string path, IReadOnlyList<string> lines)
    {
        char delimiter = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".csv" => ',',
            ".tsv" => '\t',
            _ => '\0'
        };
        if (delimiter == '\0' || lines.Count < 2 || lines.Count > 1001)
            return null;

        // RFC-style quoted fields within a logical line. Multi-line CSV records
        // cannot be validated by this line-based reader, so audit nothing.
        var records = new List<string[]>(lines.Count);
        foreach (string line in lines)
        {
            if (!TrySplit(line, delimiter, out string[] record)) return null;
            if (records.Count > 0 && record.Length != records[0].Length) return null;
            records.Add(record);
        }
        string[] header = records[0];
        if (header.Length < 2 || header.Length > 32) return null;
        if (header.Any(string.IsNullOrWhiteSpace) ||
            header.Distinct(StringComparer.OrdinalIgnoreCase).Count() != header.Length)
            return null;

        var details = new List<string>();
        for (int col = 0; col < header.Length && details.Count < 8; col++)
        {
            decimal sum = 0, min = 0, max = 0;
            int count = 0, maxLine = 0, empty = 0;
            bool valid = true;
            for (int row = 1; row < records.Count; row++)
            {
                string cell = records[row][col].Trim();
                if (cell.Length == 0) { empty++; continue; }
                if (!decimal.TryParse(cell,
                    NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint |
                    NumberStyles.AllowThousands | NumberStyles.AllowLeadingWhite |
                    NumberStyles.AllowTrailingWhite,
                    CultureInfo.InvariantCulture, out decimal value))
                { valid = false; break; }
                try { sum = checked(sum + value); }
                catch (OverflowException) { valid = false; break; }
                if (count == 0 || value < min) min = value;
                if (count == 0 || value > max) { max = value; maxLine = row + 1; }
                count++;
            }
            if (!valid || count == 0) continue;
            string column = header[col].Replace("|", " ").Replace("=", " ").Trim();
            details.Add($"Column='{column}',NumericRows={count},Blanks={empty}," +
                $"Sum={sum.ToString(CultureInfo.InvariantCulture)}," +
                $"Min={min.ToString(CultureInfo.InvariantCulture)}," +
                $"Max={max.ToString(CultureInfo.InvariantCulture)},MaxSourceLine={maxLine}");
        }
        if (details.Count == 0) return null;
        return "VerifiedNumericAudit=CompleteDelimitedRead; " + string.Join("; ", details) +
            ". Numeric-column sums are arithmetic facts, not automatic proof that a column represents money or an appropriate business total.";
    }

    private static bool TrySplit(string line, char delimiter, out string[] fields)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        bool quoted = false;
        bool endedQuote = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                { current.Append('"'); i++; }
                else if (c == '"') { quoted = false; endedQuote = true; }
                else current.Append(c);
            }
            else if (c == delimiter)
            { parts.Add(current.ToString()); current.Clear(); endedQuote = false; }
            else if (c == '"' && current.Length == 0 && !endedQuote)
                quoted = true;
            else if (c == '"' || (endedQuote && !char.IsWhiteSpace(c)))
            { fields = Array.Empty<string>(); return false; }
            else if (!endedQuote)
                current.Append(c);
        }
        if (quoted) { fields = Array.Empty<string>(); return false; }
        parts.Add(current.ToString());
        fields = parts.ToArray();
        return true;
    }
}
