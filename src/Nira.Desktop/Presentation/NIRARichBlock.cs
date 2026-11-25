namespace NIRAAgent.Presentation;

// One authoritative cognition decision can contain separate spoken words and
// declarative screen blocks. The WPF app, not the model, owns the rendering.
public sealed record NIRARichBlock
{
    public string Icon { get; init; } = string.Empty;
    public string Color { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty; // heading, text, hero, insight, source, card, metric, equation, table, chart, code, details, list, quote, timeline, checklist, progress
    public string Title { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public string Formula { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public IReadOnlyList<string> Items { get; init; } = Array.Empty<string>();
    // Tabs: parallel, model-authored labels/content; UI never executes supplied text.
    public IReadOnlyList<string> Panels { get; init; } = Array.Empty<string>();
    // Response Realization may identify the verified image-search subject to
    // place immediately AFTER this block. This field is layout-only, never a
    // source URL or permission to retrieve a new image.
    public string ImageSubject { get; init; } = string.Empty;
    public double? ProgressValue { get; init; }
    public double? ProgressMaximum { get; init; }
    public IReadOnlyList<string> Columns { get; init; } = Array.Empty<string>();
    public IReadOnlyList<IReadOnlyList<string>> Rows { get; init; } = Array.Empty<IReadOnlyList<string>>();
    public IReadOnlyList<string> Labels { get; init; } = Array.Empty<string>();
    public IReadOnlyList<double> Values { get; init; } = Array.Empty<double>();
    public IReadOnlyList<NIRAChartSeries> Series { get; init; } = Array.Empty<NIRAChartSeries>();
}

public sealed record NIRAChartSeries(string Label, IReadOnlyList<double> Values);

public sealed record NIRARichInteractionState
{
    public bool Expanded { get; init; }
    public int SelectedIndex { get; init; } = -1;
    public IReadOnlyList<int> CheckedIndices { get; init; } = Array.Empty<int>();
}



// Bounded arithmetic for local interactive presentation. No script, reflection,
// system access, or implicit tool execution. Variables are positional x1..x8.
public static class NIRAVisualArithmetic
{
    public static bool TryEvaluate(string formula, IReadOnlyList<double> values, out double result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(formula) || formula.Length > 256 || values.Count is < 1 or > 8 ||
            values.Any(v => !double.IsFinite(v) || Math.Abs(v) > 1e100)) return false;
        try
        {
            var parser = new Parser(formula, values);
            result = parser.Expression();
            return parser.AtEnd && double.IsFinite(result);
        }
        catch (FormatException) { return false; }
    }

    private sealed class Parser(string source, IReadOnlyList<double> values)
    {
        private int position;
        private void Space() { while (position < source.Length && char.IsWhiteSpace(source[position])) position++; }
        public bool AtEnd { get { Space(); return position == source.Length; } }
        private bool Eat(char token)
        {
            Space();
            if (position >= source.Length || source[position] != token) return false;
            position++; return true;
        }
        private static double Finite(double n) => double.IsFinite(n) ? n : throw new FormatException();
        public double Expression()
        {
            double n = Product();
            while (true)
            {
                if (Eat('+')) n = Finite(n + Product());
                else if (Eat('-')) n = Finite(n - Product());
                else return n;
            }
        }
        private double Product()
        {
            double n = Unary();
            while (true)
            {
                if (Eat('*')) n = Finite(n * Unary());
                else if (Eat('/')) n = Finite(n / Unary());
                else return n;
            }
        }
        private double Unary()
        {
            if (Eat('+')) return Unary();
            if (Eat('-')) return -Unary();
            double n = Atom();
            return Eat('^') ? Finite(Math.Pow(n, Unary())) : n;
        }
        private double Atom()
        {
            Space();
            if (Eat('('))
            {
                double n = Expression();
                if (!Eat(')')) throw new FormatException();
                return n;
            }
            if (position < source.Length && source[position] == 'x')
            {
                position++;
                if (position >= source.Length || source[position] is < '1' or > '8') throw new FormatException();
                int index = source[position++] - '1';
                if (index >= values.Count) throw new FormatException();
                return values[index];
            }
            int start = position;
            while (position < source.Length && (char.IsAsciiDigit(source[position]) || source[position] == '.')) position++;
            if (start == position || !double.TryParse(source.AsSpan(start, position - start),
                System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out double number))
                throw new FormatException();
            return Finite(number);
        }
    }
}

// The parser and renderer share one bounded graph validator. Acyclic graphs
// use topological layers; small cyclic or crowded graphs use a radial layout.
public static class NIRAVisualDiagram
{
    public static bool TryLayers(NIRARichBlock block, out int[] layers) => TryLayout(block, out layers, out _);

    public static bool TryLayout(NIRARichBlock block, out int[] layers, out bool radial)
    {
        int count = block.Items.Count;
        layers = new int[count]; radial = false;
        if (count is < 2 or > 12 || block.Rows.Count is < 1 or > 24 ||
            block.Items.Any(n => string.IsNullOrWhiteSpace(n) || n.Length > 90) ||
            block.Items.Distinct(StringComparer.OrdinalIgnoreCase).Count() != count) return false;
        var edges = new HashSet<(int From, int To)>();
        foreach (var row in block.Rows)
        {
            if (row.Count != 2 || !int.TryParse(row[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int from) ||
                !int.TryParse(row[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int to) ||
                from < 1 || from > count || to < 1 || to > count || from == to || !edges.Add((from - 1, to - 1))) return false;
        }
        if (Enumerable.Range(0, count).Any(i => !edges.Any(e => e.From == i || e.To == i))) return false;
        int[] incoming = new int[count];
        foreach (var edge in edges) incoming[edge.To]++;
        var ready = new Queue<int>(Enumerable.Range(0, count).Where(i => incoming[i] == 0));
        int visited = 0;
        while (ready.TryDequeue(out int node))
        {
            visited++;
            foreach (var edge in edges.Where(e => e.From == node))
            {
                layers[edge.To] = Math.Max(layers[edge.To], layers[node] + 1);
                if (--incoming[edge.To] == 0) ready.Enqueue(edge.To);
            }
        }
        radial = visited != count || layers.GroupBy(n => n).Any(g => g.Count() > 4);
        if (radial) Array.Clear(layers, 0, layers.Length);
        return !radial || (count <= 8 && edges.Count <= 16);
    }
}
