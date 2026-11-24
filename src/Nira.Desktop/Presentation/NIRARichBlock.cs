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
}

public sealed record NIRARichInteractionState
{
    public bool Expanded { get; init; }
    public int SelectedIndex { get; init; } = -1;
    public IReadOnlyList<int> CheckedIndices { get; init; } = Array.Empty<int>();
}

