using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using NIRAAgent.Presentation;

namespace NIRAAgent.UI.Presentation;

// Typed presentation only. No arbitrary XAML, HTML, script or model-owned bindings.
// All layout is local WPF and follows the active NIRA Eclipse/Halo resources.
public static class NIRARichBlockRenderer
{
    private static void Theme(FrameworkElement element, DependencyProperty property, string key) =>
        element.SetResourceReference(property, key);

    private static TextBlock Text(string value, double size = 13, bool bold = false, string brush = "NIRATextBrush")
    {
        var text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = size,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            LineHeight = size * 1.53
        };
        Theme(text, TextBlock.ForegroundProperty, brush);
        // Plain model output can contain Markdown emphasis. Render the familiar
        // inline formatting rather than leaking ** and backticks into the UI.
        var parts = Regex.Split(value ?? "", @"(\*\*[^*\r\n]+\*\*|`[^`\r\n]+`)");
        foreach (var part in parts)
        {
            if (part.StartsWith("**", StringComparison.Ordinal) && part.EndsWith("**", StringComparison.Ordinal) && part.Length > 4)
                text.Inlines.Add(new Bold(new Run(part[2..^2])));
            else if (part.StartsWith('`') && part.EndsWith('`') && part.Length > 2)
                text.Inlines.Add(new Run(part[1..^1]) { FontFamily = new FontFamily("Cascadia Code, Consolas") });
            else text.Inlines.Add(new Run(part));
        }
        return text;
    }

    private static Border Frame(UIElement child, bool boxed = true, double padding = 14, double maxWidth = double.PositiveInfinity)
    {
        var frame = new Border
        {
            Child = child,
            CornerRadius = new CornerRadius(boxed ? 12 : 0),
            BorderThickness = new Thickness(boxed ? 1 : 0),
            Padding = new Thickness(boxed ? padding : 0),
            Margin = new Thickness(0, 8, 0, 5),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MaxWidth = maxWidth
        };
        if (boxed)
        {
            Theme(frame, Border.BackgroundProperty, "NIRAGlassStrongTintBrush");
            Theme(frame, Border.BorderBrushProperty, "NIRAGlassBorderBrush");
        }
        return frame;
    }

    public static FrameworkElement Render(NIRARichBlock block,
        NIRARichInteractionState? state = null,
        Action<NIRARichInteractionState>? onStateChanged = null,
        Action<string>? followUp = null) => block.Type switch
    {
        "heading" => Frame(Text(block.Title.Length > 0 ? block.Title : block.Text, 18, true), false),
        "text" => Frame(Body(block), false),
        "card" => Card(block),
        "metric" => Frame(Metric(block)),
        "table" => Frame(Table(block), true, 12),
        "chart" => Frame(Chart(block)),
        "code" => Frame(Code(block)),
        "details" => Frame(Details(block, state, onStateChanged)),
        "tabs" => Frame(Tabs(block, state, onStateChanged)),
        "followups" => Frame(FollowUps(block, state, onStateChanged, followUp), false),
        "list" => Frame(BulletList(block), false),
        "quote" => Frame(Quote(block)),
        "timeline" => Frame(Timeline(block), false, maxWidth: 850),
        "checklist" => Frame(Checklist(block, state, onStateChanged), true, 16, maxWidth: 850),
        "progress" => Frame(Progress(block), true, 18),
        _ => Frame(Text(block.Text), false)
    };

    // Adjacent cards wrap naturally. A broad chat shows 3 columns; narrower
    // windows show 2 or 1 without a fixed viewport-width assumption.
    public static FrameworkElement RenderCardGroup(IEnumerable<NIRARichBlock> cards)
    {
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var card in cards)
        {
            var rendered = Card(card);
            rendered.Width = 232;
            rendered.MinHeight = 155;
            rendered.Margin = new Thickness(0, 8, 9, 5);
            wrap.Children.Add(rendered);
        }
        return wrap;
    }

    // Compact multi-metric strip (responsive WrapPanel), never a giant empty table.
    public static FrameworkElement RenderMetricGroup(IEnumerable<NIRARichBlock> metrics)
    {
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var block in metrics)
        {
            var frame = Frame(Metric(block));
            frame.Width = 208;
            frame.MinHeight = 106;
            frame.Margin = new Thickness(0, 7, 10, 6);
            wrap.Children.Add(frame);
        }
        return wrap;
    }

    private static Border Card(NIRARichBlock block)
    {
        var stack = new StackPanel();
        stack.Children.Add(Text(block.Title.Length == 0 ? "Details" : block.Title, 15, true, "NIRACyanBrush"));
        if (!string.IsNullOrWhiteSpace(block.Text))
        {
            var details = Text(block.Text, 12);
            details.Margin = new Thickness(0, 9, 0, 0);
            stack.Children.Add(details);
        }
        return Frame(stack);
    }

    private static UIElement Body(NIRARichBlock block)
    {
        var stack = new StackPanel();
        if (block.Title.Length > 0) stack.Children.Add(Text(block.Title, 15, true, "NIRACyanBrush"));
        if (block.Text.Length > 0)
        {
            var details = Text(block.Text);
            if (block.Title.Length > 0) details.Margin = new Thickness(0, 5, 0, 0);
            stack.Children.Add(details);
        }
        return stack;
    }

    private static UIElement Metric(NIRARichBlock block)
    {
        var stack = new StackPanel();
        stack.Children.Add(Text(block.Title.ToUpperInvariant(), 10, true, "NIRASecondaryBrush"));
        string unit = block.Unit.Trim();
        string valueText = block.Text.Trim();
        if (unit.Length > 0 && !valueText.EndsWith(unit, StringComparison.OrdinalIgnoreCase))
            valueText += " " + unit;
        var value = Text(valueText, 24, true, "NIRACyanBrush");
        value.Margin = new Thickness(0, 5, 0, 0);
        stack.Children.Add(value);
        return stack;
    }

    // Local-only UI commands never become model-owned capabilities.
    private static Button ActionButton(string label, Action action)
    {
        var button = new Button
        {
            Content = label,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(7, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Cursor = System.Windows.Input.Cursors.Hand,
            MinHeight = 30,
            BorderThickness = new Thickness(1)
        };
        Theme(button, Control.ForegroundProperty, "NIRACyanBrush");
        Theme(button, Control.BackgroundProperty, "NIRAGlassStrongTintBrush");
        Theme(button, Control.BorderBrushProperty, "NIRAGlassBorderBrush");
        button.Click += (_, _) => action();
        return button;
    }

    private static readonly Regex CodeTokens = new(
        @"(?<comment>//[^\r\n]*|#[^\r\n]*)|(?<string>""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*')|(?<number>\b\d+(?:\.\d+)?\b)|(?<keyword>\b(?:abstract|as|async|await|bool|break|byte|case|catch|char|class|const|continue|decimal|default|def|delete|do|double|elif|else|enum|event|except|export|extends|false|False|finally|float|for|foreach|from|function|if|import|in|int|interface|internal|is|let|long|namespace|new|null|None|object|out|override|private|protected|public|readonly|record|return|sealed|short|static|string|struct|super|switch|this|throw|throws|true|True|try|typeof|using|var|virtual|void|while|yield)\b)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Brush CodeComment = Frozen("#8395AC");
    private static readonly Brush CodeString = Frozen("#91D5B1");
    private static readonly Brush CodeNumber = Frozen("#EBC182");
    private static readonly Brush CodeKeyword = Frozen("#6CCAF3");
    private static Brush Frozen(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }

    private static TextBlock HighlightedSource(string source)
    {
        var body = new TextBlock
        {
            TextWrapping = TextWrapping.NoWrap,
            FontSize = 13,
            LineHeight = 21,
            FontFamily = new FontFamily("Cascadia Code, Consolas"),
            Padding = new Thickness(2)
        };
        Theme(body, TextBlock.ForegroundProperty, "NIRATextBrush");
        string[] lines = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            body.Inlines.Add(new Run($"{i + 1,3}  ") { Foreground = CodeComment });
            string line = lines[i];
            int cursor = 0;
            foreach (Match match in CodeTokens.Matches(line))
            {
                if (match.Index > cursor)
                    body.Inlines.Add(new Run(line[cursor..match.Index]));
                Brush brush = match.Groups["comment"].Success ? CodeComment :
                    match.Groups["string"].Success ? CodeString :
                    match.Groups["number"].Success ? CodeNumber : CodeKeyword;
                body.Inlines.Add(new Run(match.Value) { Foreground = brush });
                cursor = match.Index + match.Length;
            }
            if (cursor < line.Length) body.Inlines.Add(new Run(line[cursor..]));
            if (i < lines.Length - 1) body.Inlines.Add(new LineBreak());
        }
        return body;
    }

    // A short code/table block cannot scroll vertically, and a long one
    // eventually reaches its top or bottom. In either case, keep the mouse
    // wheel moving through the conversation instead of swallowing the input.
    // Apply only to the nested rich-content viewers, not the main ChatScroll.
    private static ScrollViewer ChainVerticalWheel(ScrollViewer nested)
    {
        nested.PreviewMouseWheel += (_, e) =>
        {
            if (e.Handled || e.Delta == 0) return;

            // One standard wheel notch is 120 units. Carry any unconsumed
            // distance to the nearest outer viewer when the inner one ends.
            double remaining = -e.Delta / 120.0 * 72.0;
            bool moved = false;
            ScrollViewer? viewer = nested;
            while (viewer != null && Math.Abs(remaining) > 0.5)
            {
                double max = Math.Max(0, viewer.ScrollableHeight);
                if (max > 0.5)
                {
                    double start = viewer.VerticalOffset;
                    double target = Math.Clamp(start + remaining, 0, max);
                    double consumed = target - start;
                    if (Math.Abs(consumed) > 0.5)
                    {
                        viewer.ScrollToVerticalOffset(target);
                        remaining -= consumed;
                        moved = true;
                    }
                }
                viewer = ParentScrollViewer(viewer);
            }
            if (moved) e.Handled = true;
            // If no viewer moved, leave the event alone for normal WPF handling.
        };
        return nested;
    }

    private static ScrollViewer? ParentScrollViewer(DependencyObject child)
    {
        DependencyObject? parent = VisualTreeHelper.GetParent(child);
        while (parent != null)
        {
            if (parent is ScrollViewer viewer) return viewer;
            parent = VisualTreeHelper.GetParent(parent);
        }
        return null;
    }

    private static UIElement Code(NIRARichBlock block)
    {
        string source = block.Text.Replace("\r\n", "\n").Replace('\r', '\n');
        var root = new StackPanel();
        var toolbar = new DockPanel { LastChildFill = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(buttons, Dock.Right);
        toolbar.Children.Add(buttons);
        var label = Text(block.Title.Length > 0 ? block.Title :
            (block.Language.Length > 0 ? block.Language.ToUpperInvariant() : "CODE"),
            12, true, "NIRACyanBrush");
        label.VerticalAlignment = VerticalAlignment.Center;
        toolbar.Children.Add(label);
        root.Children.Add(toolbar);
        buttons.Children.Add(ActionButton("Copy", () => Clipboard.SetText(source)));

        var highlighted = HighlightedSource(source);
        var scroller = new ScrollViewer
        {
            Content = highlighted,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 390,
            Margin = new Thickness(0, 10, 0, 0)
        };
        int lineCount = source.Count(c => c == '\n') + 1;
        if (lineCount > 20)
        {
            bool expanded = false;
            scroller.MaxHeight = 20 * 21 + 12;
            Button? toggle = null;
            toggle = ActionButton($"Expand · {lineCount} lines", () =>
            {
                expanded = !expanded;
                scroller.MaxHeight = expanded ? 720 : 20 * 21 + 12;
                if (toggle != null) toggle.Content = expanded ? "Collapse" : $"Expand · {lineCount} lines";
            });
            buttons.Children.Insert(0, toggle);
        }
        root.Children.Add(ChainVerticalWheel(scroller));
        return root;
    }

    private static UIElement Details(NIRARichBlock block,
        NIRARichInteractionState? saved, Action<NIRARichInteractionState>? changed)
    {
        var root = new StackPanel();
        bool expanded = saved?.Expanded == true;
        var content = Text(block.Text, 13);
        content.Margin = new Thickness(3, 10, 3, 5);
        content.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        string label = block.Title.Length > 0 ? block.Title : "More details";
        Button? toggle = null;
        toggle = ActionButton((expanded ? "▾  " : "▸  ") + label, () =>
        {
            expanded = !expanded;
            content.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            if (toggle != null) toggle.Content = (expanded ? "▾  " : "▸  ") + label;
            changed?.Invoke(new NIRARichInteractionState { Expanded = expanded });
        });
        toggle.HorizontalAlignment = HorizontalAlignment.Stretch;
        toggle.HorizontalContentAlignment = HorizontalAlignment.Left;
        toggle.Margin = new Thickness(0);
        toggle.FontSize = 14;
        toggle.Padding = new Thickness(14, 11, 14, 11);
        root.Children.Add(toggle);
        root.Children.Add(content);
        return root;
    }

    // A tab panel can mix explanatory prose and fenced source. A plain
    // TextBlock would show source as unformatted text and lose code tooling.
    // Render source using the same syntax highlighter/copy/scroll surface as
    // an ordinary "code" block, including when a model omits code fences in
    // a clearly named Example/Code tab.
    private static readonly Regex TabCodeFence = new(
        @"```(?<language>[^`\r\n]*)\r?\n(?<source>[\s\S]*?)\r?\n?```",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static UIElement TabPanelContent(string text, string tabLabel)
    {
        var content = new StackPanel { Margin = new Thickness(3, 5, 3, 9) };
        string source = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        MatchCollection fences = TabCodeFence.Matches(source);
        if (fences.Count == 0)
        {
            bool codeTab = Regex.IsMatch(tabLabel,
                @"\b(example|code|snippet)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            bool codeBody = source.Contains('\n') && Regex.IsMatch(source,
                @"(?m)^\s*(?:for\s*\(|(?:int|var|bool|string|double|float|decimal)\s+\w+\s*=|(?:Console\.|print\s*\(|//)|[{}]\s*$)",
                RegexOptions.CultureInvariant);
            if (codeTab && codeBody)
            {
                content.Children.Add(Code(new NIRARichBlock
                {
                    Type = "code", Text = source.Trim(), Title = "Example"
                }));
            }
            else content.Children.Add(Text(source, 13));
            return content;
        }

        int cursor = 0;
        foreach (Match fence in fences)
        {
            if (fence.Index > cursor)
            {
                string prose = source[cursor..fence.Index].Trim();
                if (prose.Length > 0)
                {
                    var paragraph = Text(prose, 13);
                    paragraph.Margin = new Thickness(0, 0, 0, 10);
                    content.Children.Add(paragraph);
                }
            }
            string code = fence.Groups["source"].Value.Trim('\n');
            if (code.Length > 0)
            {
                var codeBlock = Code(new NIRARichBlock
                {
                    Type = "code", Language = fence.Groups["language"].Value.Trim(),
                    Text = code
                });
                var frame = Frame(codeBlock, true, 11);
                frame.Margin = new Thickness(0, 4, 0, 10);
                content.Children.Add(frame);
            }
            cursor = fence.Index + fence.Length;
        }
        if (cursor < source.Length)
        {
            string trailing = source[cursor..].Trim();
            if (trailing.Length > 0) content.Children.Add(Text(trailing, 13));
        }
        return content;
    }

    private static UIElement Tabs(NIRARichBlock block,
        NIRARichInteractionState? saved, Action<NIRARichInteractionState>? changed)
    {
        int selected = saved?.SelectedIndex is int index && index >= 0 &&
            index < block.Items.Count ? index : 0;
        var root = new StackPanel();
        if (block.Title.Length > 0)
            root.Children.Add(Text(block.Title, 16, true, "NIRACyanBrush"));
        var strip = new WrapPanel { Margin = new Thickness(0, 9, 0, 10) };
        var controls = new List<Button>();
        var panelHost = new StackPanel();
        panelHost.Children.Add(TabPanelContent(block.Panels[selected], block.Items[selected]));
        void Select(int next)
        {
            selected = next;
            panelHost.Children.Clear();
            panelHost.Children.Add(TabPanelContent(block.Panels[next], block.Items[next]));
            for (int i = 0; i < controls.Count; i++)
            {
                controls[i].FontWeight = i == selected ? FontWeights.Bold : FontWeights.Normal;
                controls[i].SetResourceReference(Control.ForegroundProperty,
                    i == selected ? "NIRACyanBrush" : "NIRATextBrush");
            }
            changed?.Invoke(new NIRARichInteractionState { SelectedIndex = selected });
        }
        for (int i = 0; i < block.Items.Count; i++)
        {
            int choice = i;
            var button = ActionButton(block.Items[i], () => Select(choice));
            button.Margin = new Thickness(0, 0, 7, 6);
            button.Padding = new Thickness(13, 8, 13, 8);
            button.FontSize = 13;
            button.FontWeight = i == selected ? FontWeights.Bold : FontWeights.Normal;
            button.SetResourceReference(Control.ForegroundProperty,
                i == selected ? "NIRACyanBrush" : "NIRATextBrush");
            controls.Add(button);
            strip.Children.Add(button);
        }
        root.Children.Add(strip);
        root.Children.Add(panelHost);
        return root;
    }

    // Only a real click submits a follow-up, never a model-authored callback.
    private static UIElement FollowUps(NIRARichBlock block,
        NIRARichInteractionState? saved, Action<NIRARichInteractionState>? changed,
        Action<string>? submit)
    {
        var root = new StackPanel();
        root.Children.Add(Text(block.Title.Length == 0 ? "Continue with" : block.Title,
            14, true, "NIRACyanBrush"));
        var strip = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        int selected = saved?.SelectedIndex ?? -1;
        for (int i = 0; i < block.Items.Count; i++)
        {
            int index = i;
            string choice = block.Items[i];
            var button = ActionButton((index == selected ? "✓  " : "↗  ") + choice, () =>
            {
                selected = index;
                changed?.Invoke(new NIRARichInteractionState { SelectedIndex = index });
                submit?.Invoke(choice);
            });
            button.HorizontalAlignment = HorizontalAlignment.Left;
            button.Margin = new Thickness(0, 0, 8, 7);
            button.Padding = new Thickness(14, 9, 14, 9);
            button.FontSize = 13;
            strip.Children.Add(button);
        }
        root.Children.Add(strip);
        return root;
    }

    private static UIElement BulletList(NIRARichBlock block)
    {
        var root = new StackPanel();
        if (block.Title.Length > 0) root.Children.Add(Text(block.Title, 15, true, "NIRACyanBrush"));
        foreach (string item in block.Items)
        {
            // A DockPanel allows wrapped text to take available chat width.
            var row = new DockPanel();
            var bullet = Text("•", 14, true, "NIRACyanBrush");
            DockPanel.SetDock(bullet, Dock.Left);
            row.Children.Add(bullet);
            var text = Text(item, 13);
            text.Margin = new Thickness(10, 0, 0, 0);
            row.Children.Add(text);
            row.Margin = new Thickness(0, 7, 0, 0);
            root.Children.Add(row);
        }
        return root;
    }

    private static UIElement Quote(NIRARichBlock block)
    {
        var root = new StackPanel();
        if (block.Title.Length > 0) root.Children.Add(Text(block.Title, 12, true, "NIRACyanBrush"));
        var quote = Text(block.Text, 14);
        quote.FontStyle = FontStyles.Italic;
        quote.Margin = new Thickness(12, 7, 0, 0);
        root.Children.Add(quote);
        return root;
    }

    // A milestone rail, not a plain list. The model supplies only content/order;
    // the UI supplies stage numbers and decorative structure. No dates inferred.
    private static UIElement Timeline(NIRARichBlock block)
    {
        var root = new StackPanel();
        var header = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 12) };
        var count = Text($"{block.Items.Count} STEPS", 11, true, "NIRASecondaryBrush");
        count.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(count, Dock.Right);
        header.Children.Add(count);
        header.Children.Add(Text(block.Title.Length == 0 ? "Timeline" : block.Title,
            17, true, "NIRACyanBrush"));
        root.Children.Add(header);

        for (int i = 0; i < block.Items.Count; i++)
        {
            var item = block.Items[i];
            // Only format an explicit day/stage + task separator, never invent one.
            var split = Regex.Match(item, @"^\s*(?<when>[^—–:]{1,55})\s*[—–:]\s*(?<task>.+)$");
            string when = split.Success ? split.Groups["when"].Value.Trim() : $"Step {i + 1:00}";
            string task = split.Success ? split.Groups["task"].Value.Trim() : item;
            var line = new Grid { Margin = new Thickness(0, 0, 0, i == block.Items.Count - 1 ? 0 : 10) };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var rail = new Grid();
            if (i < block.Items.Count - 1)
            {
                var connector = new Border { Width = 2, HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 25, 0, -16), Opacity = .40 };
                Theme(connector, Border.BackgroundProperty, "NIRACyanBrush");
                rail.Children.Add(connector);
            }
            var halo = new Border { Width = 23, Height = 23, CornerRadius = new CornerRadius(12),
                VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center,
                BorderThickness = new Thickness(1.5), Margin = new Thickness(0, 11, 0, 0) };
            Theme(halo, Border.BorderBrushProperty, "NIRACyanBrush");
            Theme(halo, Border.BackgroundProperty, "NIRAGlassStrongTintBrush");
            var dot = new Ellipse { Width = 7, Height = 7, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center };
            Theme(dot, Shape.FillProperty, "NIRACyanBrush");
            halo.Child = dot;
            rail.Children.Add(halo);
            line.Children.Add(rail);

            var card = new Border { CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 11, 16, 12) };
            Theme(card, Border.BackgroundProperty, "NIRAGlassStrongTintBrush");
            Theme(card, Border.BorderBrushProperty, "NIRAGlassBorderBrush");
            var body = new StackPanel();
            var stage = Text(when.ToUpperInvariant(), 11.5, true, "NIRACyanBrush");
            stage.Margin = new Thickness(0, 0, 0, 3);
            body.Children.Add(stage);
            body.Children.Add(Text(task, 14));
            card.Child = body;
            Grid.SetColumn(card, 1);
            line.Children.Add(card);
            root.Children.Add(line);
        }
        return root;
    }

    // Borderless button template: the stock Windows CheckBox and Button chrome
    // does not follow NIRA's Eclipse/Halo palette. Buttons are keyboard-accessible;
    // each toggle remains local to this view, NOT a persisted goal or commitment.
    private static ControlTemplate ChecklistRowTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetBinding(Border.BackgroundProperty, new Binding("Background")
            { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        border.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush")
            { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        content.SetBinding(ContentPresenter.ContentProperty, new Binding("Content")
            { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        border.AppendChild(content);
        template.VisualTree = border;
        return template;
    }

    private static UIElement Checklist(NIRARichBlock block,
        NIRARichInteractionState? state, Action<NIRARichInteractionState>? changed)
    {
        var root = new StackPanel();
        var header = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 11) };
        var checkedItems = new HashSet<int>((state?.CheckedIndices ?? Array.Empty<int>())
            .Where(i => i >= 0 && i < block.Items.Count));
        var tally = Text($"{checkedItems.Count} / {block.Items.Count} DONE", 11, true,
            checkedItems.Count == block.Items.Count ? "NIRACyanBrush" : "NIRASecondaryBrush");
        tally.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(tally, Dock.Right);
        header.Children.Add(tally);
        header.Children.Add(Text(block.Title.Length == 0 ? "Checklist" : block.Title,
            17, true, "NIRACyanBrush"));
        root.Children.Add(header);
        int checkedCount = checkedItems.Count;
        var rowTemplate = ChecklistRowTemplate();
        for (int i = 0; i < block.Items.Count; i++)
        {
            int itemIndex = i;
            string item = block.Items[i];
            bool completed = checkedItems.Contains(i);
            var row = new DockPanel { LastChildFill = true, Margin = new Thickness(13, 10, 13, 10) };
            var glyph = new Border { Width = 21, Height = 21, CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1.4), Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center };
            Theme(glyph, Border.BorderBrushProperty, "NIRACyanBrush");
            Theme(glyph, Border.BackgroundProperty, "NIRAGlassStrongTintBrush");
            var tick = Text("✓", 15, true, "NIRACyanBrush");
            tick.TextAlignment = TextAlignment.Center;
            tick.Visibility = completed ? Visibility.Visible : Visibility.Collapsed;
            glyph.Child = tick;
            DockPanel.SetDock(glyph, Dock.Left);
            row.Children.Add(glyph);
            var label = Text(item, 14);
            label.VerticalAlignment = VerticalAlignment.Center;
            label.TextDecorations = completed ? TextDecorations.Strikethrough : null;
            label.Opacity = completed ? .67 : 1;
            row.Children.Add(label);
            var button = new Button { Content = row, Template = rowTemplate,
                Margin = new Thickness(0, 0, 0, 7), Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Cursor = System.Windows.Input.Cursors.Hand, MinHeight = 43,
                ToolTip = "Mark this item done in this view" };
            Theme(button, Control.BackgroundProperty, "NIRAGlassStrongTintBrush");
            Theme(button, Control.BorderBrushProperty, "NIRAGlassBorderBrush");
            button.Click += (_, _) =>
            {
                completed = !completed;
                if (completed) checkedItems.Add(itemIndex);
                else checkedItems.Remove(itemIndex);
                checkedCount = checkedItems.Count;
                changed?.Invoke(new NIRARichInteractionState
                    { CheckedIndices = checkedItems.OrderBy(x => x).ToArray() });
                tick.Visibility = completed ? Visibility.Visible : Visibility.Collapsed;
                label.TextDecorations = completed ? TextDecorations.Strikethrough : null;
                label.Opacity = completed ? .67 : 1;
                tally.Text = $"{checkedCount} / {block.Items.Count} DONE";
                tally.SetResourceReference(TextBlock.ForegroundProperty,
                    checkedCount == block.Items.Count ? "NIRACyanBrush" : "NIRASecondaryBrush");
            };
            root.Children.Add(button);
        }
        var note = Text(changed == null
            ? "This checklist is local to this view · does not change NIRA's tasks"
            : "Saved with this chat · does not change NIRA's goals or tasks", 11.5,
            false, "NIRASecondaryBrush");
        note.Margin = new Thickness(0, 5, 0, 0);
        root.Children.Add(note);
        return root;
    }

    private static UIElement Progress(NIRARichBlock block)
    {
        double value = block.ProgressValue ?? 0;
        double maximum = block.ProgressMaximum ?? 1;
        double percent = value / maximum * 100;
        double remaining = maximum - value;
        string unit = block.Unit.Length > 0 ? " " + block.Unit : "";

        var root = new StackPanel();
        var header = new DockPanel { LastChildFill = true };
        var pct = Text($"{percent:0.#}%", 25, true, "NIRACyanBrush");
        pct.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(pct, Dock.Right);
        header.Children.Add(pct);
        var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(Text(block.Title, 17, true, "NIRACyanBrush"));
        var detail = Text($"{value:0.##} of {maximum:0.##}{unit} complete", 12.5,
            false, "NIRASecondaryBrush");
        detail.Margin = new Thickness(0, 5, 0, 0);
        title.Children.Add(detail);
        header.Children.Add(title);
        root.Children.Add(header);

        var track = new Border { CornerRadius = new CornerRadius(6), Height = 12,
            Margin = new Thickness(0, 15, 0, 13),
            HorizontalAlignment = HorizontalAlignment.Stretch, ClipToBounds = true };
        Theme(track, Border.BackgroundProperty, "NIRAGlassBorderBrush");
        var fill = new Border { CornerRadius = new CornerRadius(6), Height = 12,
            HorizontalAlignment = HorizontalAlignment.Left };
        Theme(fill, Border.BackgroundProperty, "NIRACyanBrush");
        track.Child = fill;
        track.SizeChanged += (_, _) => fill.Width = Math.Max(0, track.ActualWidth * percent / 100);
        root.Children.Add(track);

        var foot = new DockPanel { LastChildFill = true };
        var remainingText = Text($"{remaining:0.##}{unit} remaining", 12.5, true);
        DockPanel.SetDock(remainingText, Dock.Right);
        foot.Children.Add(remainingText);
        var start = Text("START", 10.5, true, "NIRASecondaryBrush");
        start.VerticalAlignment = VerticalAlignment.Center;
        foot.Children.Add(start);
        root.Children.Add(foot);
        // Captions may add distinct context, but an automatically derived
        // 'five chapters remaining' must not be printed twice.
        if (block.Text.Length > 0 &&
            !block.Text.Contains("remaining", StringComparison.OrdinalIgnoreCase) &&
            !block.Text.Contains("left", StringComparison.OrdinalIgnoreCase))
        {
            var caption = Text(block.Text, 12.5, false, "NIRASecondaryBrush");
            caption.Margin = new Thickness(0, 10, 0, 0);
            root.Children.Add(caption);
        }
        return root;
    }

    private static UIElement Table(NIRARichBlock block)
    {
        var root = new StackPanel();
        var toolbar = new DockPanel { LastChildFill = true };
        var copy = ActionButton("Copy TSV", () =>
        {
            static string EscapeCell(string value) => (value ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
            var lines = new List<string> { string.Join("\t", block.Columns.Select(EscapeCell)) };
            lines.AddRange(block.Rows.Select(row => string.Join("\t", row.Select(EscapeCell))));
            Clipboard.SetText(string.Join(Environment.NewLine, lines));
        });
        DockPanel.SetDock(copy, Dock.Right);
        toolbar.Children.Add(copy);
        if (block.Title.Length > 0) toolbar.Children.Add(Text(block.Title, 15, true));
        root.Children.Add(toolbar);
        var grid = new Grid();
        foreach (var _ in block.Columns) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 75 });
        for (int i = 0; i <= block.Rows.Count; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int column = 0; column < block.Columns.Count; column++) Cell(block.Columns[column], 0, column, true);
        for (int row = 0; row < block.Rows.Count; row++)
            for (int column = 0; column < block.Columns.Count; column++)
                Cell(block.Rows[row][column], row + 1, column, false);
        void Cell(string value, int row, int column, bool header)
        {
            var cell = Text(value, 12, header, header ? "NIRACyanBrush" : "NIRATextBrush");
            cell.Margin = new Thickness(10, 8, 14, 8);
            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, column);
            grid.Children.Add(cell);
            if (row > 0 && column == 0)
            {
                var rule = new Border { Height = 1, Opacity = 0.14,
                    VerticalAlignment = VerticalAlignment.Top };
                Theme(rule, Border.BackgroundProperty, "NIRASecondaryBrush");
                Grid.SetRow(rule, row);
                Grid.SetColumnSpan(rule, block.Columns.Count);
                grid.Children.Insert(0, rule);
            }
        }
        root.Children.Add(ChainVerticalWheel(new ScrollViewer
        {
            Content = grid,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 400,
            Margin = new Thickness(0, 9, 0, 0)
        }));
        return root;
    }

    private static UIElement Chart(NIRARichBlock block)
    {
        var root = new StackPanel();
        if (block.Title.Length > 0) root.Children.Add(Text(block.Title, 15, true));
        var canvas = new Canvas
        {
            Height = 215,
            MinWidth = 225,
            Margin = new Thickness(0, 10, 0, 0),
            ClipToBounds = true,
            SnapsToDevicePixels = true
        };
        root.Children.Add(canvas);
        double min = Math.Min(0, block.Values.Min());
        double max = Math.Max(0, block.Values.Max());
        if (Math.Abs(max - min) < 1e-12) { min -= 1; max += 1; }

        void Paint()
        {
            canvas.Children.Clear();
            double width = Math.Max(225, canvas.ActualWidth), height = canvas.Height;
            double left = 42, right = width - 20, top = 22, bottom = height - 36;
            for (int tick = 0; tick <= 2; tick++)
            {
                double number = min + (max - min) * tick / 2;
                double y = bottom - (bottom - top) * tick / 2;
                var line = new Line { X1 = left, X2 = right, Y1 = y, Y2 = y,
                    StrokeThickness = 1, Opacity = tick == 0 ? 0.38 : 0.15 };
                Theme(line, Shape.StrokeProperty, "NIRASecondaryBrush");
                canvas.Children.Add(line);
                var axis = Text(number.ToString("0.##", CultureInfo.InvariantCulture), 10,
                    false, "NIRASecondaryBrush");
                Canvas.SetLeft(axis, 1); Canvas.SetTop(axis, y - 10);
                canvas.Children.Add(axis);
            }
            var trace = new Polyline { StrokeThickness = 2.5, StrokeLineJoin = PenLineJoin.Round };
            Theme(trace, Shape.StrokeProperty, "NIRACyanBrush");
            canvas.Children.Add(trace);
            int step = Math.Max(1, (int)Math.Ceiling(block.Values.Count / 8.0));
            for (int i = 0; i < block.Values.Count; i++)
            {
                double x = left + (right - left) * i / (block.Values.Count - 1);
                double y = bottom - (block.Values[i] - min) / (max - min) * (bottom - top);
                trace.Points.Add(new Point(x, y));
                var dot = new Ellipse { Width = 6, Height = 6,
                    ToolTip = block.Labels[i] + ": " + block.Values[i].ToString("G", CultureInfo.InvariantCulture) +
                              (block.Unit.Length == 0 ? "" : " " + block.Unit) };
                Theme(dot, Shape.FillProperty, "NIRACyanBrush");
                Canvas.SetLeft(dot, x - 3); Canvas.SetTop(dot, y - 3);
                canvas.Children.Add(dot);
                if (i % step != 0 && i != block.Values.Count - 1) continue;
                var value = Text(block.Values[i].ToString("0.##", CultureInfo.InvariantCulture), 10,
                    true, "NIRACyanBrush");
                Canvas.SetLeft(value, Math.Clamp(x - 18, 0, width - 42));
                Canvas.SetTop(value, Math.Max(0, y - 23));
                canvas.Children.Add(value);
                var label = Text(block.Labels[i], 10, false, "NIRASecondaryBrush");
                label.Width = 60;
                label.TextAlignment = TextAlignment.Center;
                Canvas.SetLeft(label, Math.Clamp(x - 30, 0, width - 60));
                Canvas.SetTop(label, bottom + 13);
                canvas.Children.Add(label);
            }
        }
        canvas.Loaded += (_, _) => Paint();
        canvas.SizeChanged += (_, _) => Paint();
        if (block.Unit.Length > 0)
        {
            var unit = Text("Unit: " + block.Unit, 10, false, "NIRASecondaryBrush");
            unit.Margin = new Thickness(0, 2, 0, 0);
            root.Children.Add(unit);
        }
        return root;
    }
}



