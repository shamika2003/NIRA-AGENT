using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NIRAAgent.Presentation;

namespace NIRAAgent.UI.Presentation;

/// <summary>
/// Safe, native-WPF mathematical display for declarative equation blocks.
/// Parses a bounded, display-only subset of TeX (fractions, powers, roots,
/// subscripts, named symbols). It does not evaluate formulas or execute markup.
/// Unsupported commands are shown literally instead of being executed.
/// </summary>
internal static class NIRAMathEquationRenderer
{
    private static readonly FontFamily MathFont = new("Cambria Math");
    private static readonly Dictionary<string, string> Symbols = new(StringComparer.Ordinal)
    {
        ["times"] = "×", ["cdot"] = "·", ["div"] = "÷", ["pm"] = "±",
        ["approx"] = "≈", ["neq"] = "≠", ["leq"] = "≤", ["geq"] = "≥",
        ["le"] = "≤", ["ge"] = "≥", ["pi"] = "π", ["infty"] = "∞",
        ["alpha"] = "α", ["beta"] = "β", ["gamma"] = "γ",
        ["theta"] = "θ", ["tau"] = "τ", ["rho"] = "ρ", ["nu"] = "ν",
        ["nabla"] = "∇", ["Delta"] = "Δ", ["sum"] = "∑",
        ["rightarrow"] = "→", ["%"] = "%"
    };

    public static FrameworkElement Render(NIRARichBlock block)
    {
        var layout = new StackPanel { Margin = new Thickness(0, 5, 0, 9) };
        if (!string.IsNullOrWhiteSpace(block.Title))
        {
            var heading = Label(block.Title, 12, true);
            heading.Margin = new Thickness(0, 0, 0, 9);
            heading.SetResourceReference(TextBlock.ForegroundProperty, "NIRASecondaryBrush");
            layout.Children.Add(heading);
        }

        // One model-authored equation block can contain multiple display
        // spans. Give each span its own visual row and horizontal scrollbar;
        // never flatten separate calculations into one unreadable line.
        foreach (string expression in NIRAMathMarkup.SplitDisplayEquations(block.Text))
        {
            var formula = Parse(expression, 19, 0);
            formula.Margin = new Thickness(4, 4, 4, 5);
            layout.Children.Add(new ScrollViewer
            {
                Content = formula,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                CanContentScroll = false,
                Margin = new Thickness(0, 0, 0, 5)
            });
        }
        return layout;
    }

    private static TextBlock Label(string text, double size, bool bold = false)
    {
        var label = new TextBlock
        {
            Text = text, FontSize = size, FontFamily = MathFont,
            TextWrapping = TextWrapping.NoWrap,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "NIRATextBrush");
        return label;
    }

    private static StackPanel Row()
        => new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

    private static FrameworkElement Parse(string source, double size, int depth)
    {
        var row = Row();
        if (depth >= 7 || source.Length > 1600)
        {
            row.Children.Add(Label(source.Length > 1600 ? source[..1600] : source, size));
            return row;
        }
        var buffer = new StringBuilder();
        void Flush()
        {
            if (buffer.Length > 0)
            {
                row.Children.Add(Label(buffer.ToString(), size));
                buffer.Clear();
            }
        }

        for (int i = 0; i < source.Length;)
        {
            char ch = source[i];
            if (ch == '\\')
            {
                int start = i++;
                int nameStart = i;
                while (i < source.Length && char.IsLetter(source[i])) i++;
                string name;
                if (i == nameStart && i < source.Length)
                    name = source[i++].ToString();
                else
                    name = source[nameStart..i];
                if (name is "frac" or "dfrac" or "tfrac")
                {
                    int next = i;
                    if (TryGroup(source, ref next, out string top) &&
                        TryGroup(source, ref next, out string bottom))
                    {
                        Flush();
                        row.Children.Add(Fraction(top, bottom, size, depth + 1));
                        i = next;
                        continue;
                    }
                }
                if (name == "binom")
                {
                    int next = i;
                    if (TryGroup(source, ref next, out string upper) &&
                        TryGroup(source, ref next, out string lower))
                    {
                        Flush();
                        row.Children.Add(Binomial(upper, lower, size, depth + 1));
                        i = next;
                        continue;
                    }
                }
                if (name == "sqrt")
                {
                    int next = i;
                    if (TryGroup(source, ref next, out string radicand))
                    {
                        Flush();
                        row.Children.Add(Label("√", size + 2));
                        row.Children.Add(Parse("(" + radicand + ")", size, depth + 1));
                        i = next;
                        continue;
                    }
                }
                if (name is "text" or "mathrm" or "operatorname")
                {
                    int next = i;
                    if (TryGroup(source, ref next, out string plain))
                    {
                        buffer.Append(plain);
                        i = next;
                        continue;
                    }
                }
                if (name is "left" or "right" or "quad" or "qquad" or "," or ";")
                {
                    if (name is "quad" or "qquad" or "," or ";") buffer.Append(' ');
                    continue;
                }
                if (Symbols.TryGetValue(name, out string? symbol))
                {
                    buffer.Append(symbol);
                    continue;
                }
                // Display unfamiliar commands as literal data, not executable UI.
                buffer.Append(source[start..i]);
                continue;
            }
            if (ch is '^' or '_')
            {
                int next = i + 1;
                string script;
                if (!TryGroup(source, ref next, out script))
                {
                    if (next >= source.Length) { buffer.Append(ch); i++; continue; }
                    script = source[next].ToString();
                    next++;
                }
                Flush();
                var scriptText = Parse(script, Math.Max(10, size * 0.67), depth + 1);
                scriptText.VerticalAlignment = ch == '^' ? VerticalAlignment.Top : VerticalAlignment.Bottom;
                scriptText.Margin = ch == '^' ? new Thickness(0, 0, 0, size * 0.5)
                    : new Thickness(0, size * 0.42, 0, 0);
                row.Children.Add(scriptText);
                i = next;
                continue;
            }
            if (ch is '{' or '}') { i++; continue; }
            buffer.Append(ch);
            i++;
        }
        Flush();
        return row;
    }

    private static FrameworkElement Fraction(string top, string bottom, double size, int depth)
    {
        var fraction = new Grid
        {
            Margin = new Thickness(3, 0, 3, 0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        fraction.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        fraction.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
        fraction.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var numerator = Parse(top, Math.Max(11, size * 0.82), depth);
        numerator.HorizontalAlignment = HorizontalAlignment.Center;
        numerator.Margin = new Thickness(4, 0, 4, 2);
        var denominator = Parse(bottom, Math.Max(11, size * 0.82), depth);
        denominator.HorizontalAlignment = HorizontalAlignment.Center;
        denominator.Margin = new Thickness(4, 2, 4, 0);
        var rule = new Border { Height = 1, MinWidth = 18, VerticalAlignment = VerticalAlignment.Center };
        rule.SetResourceReference(Border.BackgroundProperty, "NIRATextBrush");
        Grid.SetRow(numerator, 0);
        Grid.SetRow(rule, 1);
        Grid.SetRow(denominator, 2);
        fraction.Children.Add(numerator);
        fraction.Children.Add(rule);
        fraction.Children.Add(denominator);
        return fraction;
    }

    private static FrameworkElement Binomial(string top, string bottom,
        double size, int depth)
    {
        var row = Row();
        row.Children.Add(Label("(", size + 5));
        var stack = new StackPanel { Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Center };
        var upper = Parse(top, Math.Max(11, size * 0.79), depth);
        var lower = Parse(bottom, Math.Max(11, size * 0.79), depth);
        upper.HorizontalAlignment = HorizontalAlignment.Center;
        lower.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(upper);
        stack.Children.Add(lower);
        row.Children.Add(stack);
        row.Children.Add(Label(")", size + 5));
        return row;
    }

    private static bool TryGroup(string text, ref int index, out string content)
    {
        content = string.Empty;
        if (index >= text.Length || text[index] != '{') return false;
        int start = ++index;
        int nesting = 1;
        while (index < text.Length)
        {
            char ch = text[index++];
            if (ch == '{') nesting++;
            else if (ch == '}' && --nesting == 0)
            {
                content = text[start..(index - 1)];
                return true;
            }
        }
        return false;
    }
}
