using System.Text;
using System.Text.RegularExpressions;

namespace NIRAAgent.Presentation;

/// <summary>
/// Presentation-only conversion of common model-authored TeX math delimiters
/// into readable Unicode/plain-text expressions. Never evaluates expressions,
/// and never interprets XAML/HTML or other executable markup.
/// The original numeric characters and values are preserved.
/// </summary>
public static class NIRAMathMarkup
{
    private static readonly Regex MathSpan = new(
        @"(?<!\\)\$\$(?<block>[\s\S]{1,500}?)\$\$|\\\[(?<square>[\s\S]{1,500}?)\\\]|\\\((?<round>[\s\S]{1,350}?)\\\)|(?<![\\$])\$(?<dollar>[^$\r\n]{1,350})\$(?!\$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly (string Code, string Symbol)[] Symbols =
    {
        (@"\times", "×"), (@"\cdot", "·"), (@"\div", "÷"),
        (@"\pm", "±"), (@"\approx", "≈"), (@"\neq", "≠"),
        (@"\leq", "≤"), (@"\geq", "≥"), (@"\le", "≤"),
        (@"\ge", "≥"), (@"\pi", "π"), (@"\infty", "∞"),
        (@"\%", "%"), (@"\,", " "), (@"\;", " "),
        (@"\quad", " "), (@"\qquad", " "),
        (@"\left", ""), (@"\right", "")
    };

    private static readonly string[] EscapedEquationCommands =
    [
        "operatorname", "mathrm", "text", "tfrac", "times", "theta", "tau",
        "frac", "neq", "nabla", "nu", "right", "rho", "beta", "binom"
    ];

    /// <summary>
    /// Repair ONLY recognized TeX command prefixes in typed equation data.
    /// Some LLMs emit a single backslash inside a JSON string. Valid JSON
    /// escapes such as \t, \f, \n, \r and \b then become control characters
    /// before the equation reaches the WPF renderer. The exact command must
    /// still be recognizable; ambiguous/unrelated text is never reconstructed.
    /// This is syntax recovery, not numerical inference or expression evaluation.
    /// </summary>
    public static string NormalizeEquationInput(string? source)
    {
        if (string.IsNullOrEmpty(source)) return source ?? string.Empty;
        if (source.Length > 12000) return source;
        // A TeX control word is recovered only if the remaining letters and
        // their terminator match a supported mathematical command.
        var output = new StringBuilder(source.Length + 16);
        for (int i = 0; i < source.Length; i++)
        {
            char current = source[i];
            // Double literal backslashes can come from extra JSON escaping.
            // Canonicalize only when followed by a supported TeX word.
            if (current == '\\' && i + 2 < source.Length && source[i + 1] == '\\')
            {
                int start = i + 2;
                int end = start;
                while (end < source.Length && char.IsLetter(source[end])) end++;
                if (IsEquationCommand(source[start..end]))
                {
                    output.Append('\\');
                    i++;
                    continue;
                }
            }
            if (current is not ('\t' or '\f' or '\n' or '\r' or '\b'))
            {
                output.Append(current);
                continue;
            }
            char escapedLetter = current switch
            {
                '\t' => 't', '\f' => 'f', '\n' => 'n',
                '\r' => 'r', '\b' => 'b', _ => '\0'
            };
            bool repaired = false;
            foreach (string command in EscapedEquationCommands)
            {
                if (command[0] != escapedLetter) continue;
                string suffix = command[1..];
                int after = i + 1 + suffix.Length;
                if (after > source.Length ||
                    !source.AsSpan(i + 1, suffix.Length).SequenceEqual(suffix.AsSpan()))
                    continue;
                // TeX commands are letter-delimited. Commands that take a
                // braced argument must visibly have one, not an inferred one.
                if (after < source.Length && char.IsLetter(source[after])) continue;
                if (command is "text" or "mathrm" or "operatorname" or "frac" or "tfrac" or "binom")
                    if (after >= source.Length || source[after] != '{') continue;
                output.Append('\\').Append(command);
                i = after - 1;
                repaired = true;
                break;
            }
            if (!repaired) output.Append(current);
        }
        return output.ToString();
    }

    private static bool IsEquationCommand(string command) => command is
        "text" or "mathrm" or "operatorname" or "binom" or "frac" or "dfrac" or
        "tfrac" or "sqrt" or "times" or "cdot" or "div" or "pm" or
        "approx" or "neq" or "le" or "leq" or "ge" or "geq" or
        "pi" or "infty" or "alpha" or "beta" or "gamma" or "theta" or
        "Delta" or "sum" or "rightarrow" or "left" or "right" or
        "quad" or "qquad" or "nabla" or "nu" or "rho" or "tau";

    private static readonly string[] DisplayEquationDelimiters =
        [@"\\[", @"\\]", @"\\(", @"\\)", @"\[", @"\]", @"\(", @"\)", "$$"];

    private static readonly string[] AlignedMathEnvironments =
        ["aligned", "align", "gathered", "gather"];

    /// <summary>
    /// Split a typed display-equation field into independently rendered rows.
    /// Model output can contain several adjacent TeX display spans (or plain
    /// newline-separated expressions) inside ONE JSON displayBlock. The old WPF
    /// renderer removed only the outermost delimiters and printed internal
    /// \] / \[ tokens literally. This is a lexical, bounded display operation;
    /// it never evaluates, rewrites numbers, or infers mathematical answers.
    /// </summary>
    public static IReadOnlyList<string> SplitDisplayEquations(string? source)
    {
        string input = NormalizeEquationInput(source).Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        if (string.IsNullOrWhiteSpace(input)) return Array.Empty<string>();

        var lines = new List<string>();
        var current = new StringBuilder();
        int braceDepth = 0;
        bool aligned = false;

        void Flush()
        {
            string line = current.ToString().Trim();
            current.Clear();
            // Single-dollar math delimiters apply only when they wrap the
            // entire expression, never to arbitrary currency signs ($12).
            if (line.Length > 2 && line[0] == '$' && line[^1] == '$' &&
                line.Count(c => c == '$') == 2)
                line = line[1..^1].Trim();
            if (line.Length > 0) lines.Add(line);
        }

        // Canonical and over-escaped forms of display delimiters are syntax,
        // not content. Match the longer alternatives first. A TeX \\ hard
        // break is handled separately and only at top level.
        for (int i = 0; i < input.Length;)
        {
            string? token = null;
            foreach (string marker in DisplayEquationDelimiters)
            {
                if (input.AsSpan(i).StartsWith(marker.AsSpan(), StringComparison.Ordinal))
                { token = marker; break; }
            }
            if (token != null)
            {
                Flush();
                i += token.Length;
                continue;
            }

            // Standard multi-line environments use the same row model.
            // Do not treat a matrix environment as an aligned list of rows.
            string? environmentToken = null;
            bool openEnvironment = false;
            foreach (string env in AlignedMathEnvironments)
            {
                string begin = @"\begin{" + env + "}";
                string end = @"\end{" + env + "}";
                if (input.AsSpan(i).StartsWith(begin.AsSpan(), StringComparison.Ordinal))
                { environmentToken = begin; openEnvironment = true; break; }
                if (input.AsSpan(i).StartsWith(end.AsSpan(), StringComparison.Ordinal))
                { environmentToken = end; break; }
            }
            if (environmentToken != null)
            {
                Flush();
                aligned = openEnvironment;
                i += environmentToken.Length;
                continue;
            }
            char ch = input[i];
            if (ch == '{') braceDepth++;
            else if (ch == '}' && braceDepth > 0) braceDepth--;

            if (braceDepth == 0 && ch == '\n')
            {
                Flush();
                i++;
                continue;
            }
            if (braceDepth == 0 && ch == '\\' && i + 1 < input.Length && input[i + 1] == '\\')
            {
                Flush();
                i += 2;
                continue;
            }
            // TeX & is an alignment marker, not a visible operator, in an
            // aligned environment; preserve literal & in text and formulas.
            if (aligned && braceDepth == 0 && ch == '&')
            { i++; continue; }
            current.Append(ch);
            i++;
        }
        Flush();
        return lines;
    }

    public static string ToReadableText(string? source)
    {
        if (string.IsNullOrEmpty(source)) return source ?? string.Empty;
        if (!source.Contains('$') && !source.Contains(@"\(") &&
            !source.Contains(@"\[")) return source;
        return MathSpan.Replace(source, match =>
        {
            string expr = match.Groups["block"].Success ? match.Groups["block"].Value :
                match.Groups["square"].Success ? match.Groups["square"].Value :
                match.Groups["round"].Success ? match.Groups["round"].Value :
                match.Groups["dollar"].Value;

            // Ordinary money ('$10 ... $20') is not an equation. Preserve it.
            bool hasMathSyntax = expr.IndexOfAny(new[] {'=', '+', '-', '*', '/', '^', '_', '\\', '{', '}'}) >= 0;
            if (match.Groups["dollar"].Success &&
                (!hasMathSyntax ||
                 // A pair of currency amounts can look like '$5 + $10'.
                 // Never eat the currency signs and present it as math.
                 (Regex.IsMatch(expr, @"^\s*\d", RegexOptions.CultureInvariant) &&
                  Regex.IsMatch(expr, @"(?:[+*/-]\s*|[A-Za-z]\s*)$", RegexOptions.CultureInvariant) &&
                  !expr.Contains('=') && !expr.Contains('^') && !expr.Contains('\\'))))
                return match.Value;
            if (!hasMathSyntax && expr.Trim().Length == 0) return match.Value;
            return ConvertExpression(expr);
        });
    }

    private static string ConvertExpression(string expr)
    {
        string text = expr.Trim();
        // Handle common nested fraction/root notation without regex nested-brace bugs.
        text = ReplaceBracedCommand(text, @"\frac", (a, b) => "(" + a + ") / (" + b + ")", 2);
        // The binomial coefficient is not a fraction. Use C(n, k) in
        // read-only/plain-text contexts where stacked notation is unavailable.
        text = ReplaceBracedCommand(text, @"\binom", (a, b) => "C(" + a + ", " + b + ")", 2);
        text = ReplaceBracedCommand(text, @"\sqrt", (a, _) => "√(" + a + ")", 1);
        text = ReplaceBracedCommand(text, @"\text", (a, _) => a, 1);
        text = ReplaceBracedCommand(text, @"\mathrm", (a, _) => a, 1);
        foreach (var (code, symbol) in Symbols)
            text = text.Replace(code, symbol, StringComparison.Ordinal);
        text = Regex.Replace(text, @"\^\{([^{}]+)\}", m => "^(" + m.Groups[1].Value + ")");
        text = Regex.Replace(text, @"_\{([^{}]+)\}", m => "_(" + m.Groups[1].Value + ")");
        text = Regex.Replace(text, @"\{([^{}]*)\}", "$1");
        text = Regex.Replace(text, @"\^\((?<one>\d)\)", m => "^" + m.Groups["one"].Value);
        text = Regex.Replace(text, @"\^\(2\)(?!\d)|\^2(?!\d)", "²");
        text = Regex.Replace(text, @"\^\(3\)(?!\d)|\^3(?!\d)", "³");
        return text.Trim();
    }

    private static string ReplaceBracedCommand(string text, string command,
        Func<string, string, string> format, int argumentCount)
    {
        // Bound each pass: model output can be malformed or intentionally odd.
        for (int pass = 0; pass < 16; pass++)
        {
            int index = text.IndexOf(command + "{", StringComparison.Ordinal);
            if (index < 0) break;
            int position = index + command.Length;
            if (!TryTakeBrace(text, ref position, out string first)) break;
            string second = string.Empty;
            if (argumentCount == 2 && !TryTakeBrace(text, ref position, out second)) break;
            string replacement = format(first, second);
            text = text[..index] + replacement + text[position..];
        }
        return text;
    }

    private static bool TryTakeBrace(string text, ref int position, out string content)
    {
        content = string.Empty;
        if (position >= text.Length || text[position] != '{') return false;
        int start = ++position, depth = 1;
        while (position < text.Length)
        {
            char ch = text[position++];
            if (ch == '{') depth++;
            else if (ch == '}' && --depth == 0)
            {
                content = text[start..(position - 1)];
                return true;
            }
        }
        return false;
    }
}
