using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Nira.Core;

namespace NIRAAgent.UI.Presentation;

// Local developer diagnostics only. The review dialog is an explicit disclosure boundary.
// No system prompts, provider credentials, hidden reasoning or attached file bytes are requested.
internal static class DeveloperChatExport
{
    internal sealed record ActivityEntry(DateTimeOffset TimeUtc, string Kind, string Summary, string? ToolId);
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals, Converters = { new JsonStringEnumConverter() } };

    internal static string Build(IReadOnlyList<ConversationState> thread,
        IReadOnlyDictionary<Guid, List<ActivityEntry>> activity, DependencyObject surface,
        string theme, double width, double height, double left, double right, string model, string vision)
    {
        var rendered = new List<object>();
        Capture(surface, rendered);
        return JsonSerializer.Serialize(new
        {
            format = "NIRA developer chat export v1",
            presentationRevision = NIRARichAnswer.Revision, exportedUtc = DateTimeOffset.UtcNow,
            warning = "Private diagnostic content. Review before sharing. Recorded observations do not establish truth. No hidden reasoning is captured.",
            limits = "Saved thread loader returns at most 100 linked tasks. Live activity is available for up to 100 tasks and 1000 events per task in this process only. Rendered snapshot is the currently mounted conversation, not pixels.",
            environment = new { theme, width, height, leftSidebarWidth = left, rightSidebarWidth = right, model, vision,
                version = typeof(DeveloperChatExport).Assembly.GetName().Version?.ToString(), os = Environment.OSVersion.VersionString },
            turns = thread.Select(t => new
            {
                t.Id, t.ChatThreadId, t.PreviousTaskId, t.Status, t.UpdatedUtc, t.Objective,
                t.AutomaticPlanningOutcome, t.RetryBlockCode, t.PendingQuestion, t.LastAnswer,
                messages = t.Messages.Where(m => m.Role is "user" or "assistant").Select(m => new
                {
                    m.Role, content = m.Role == "user" ? m.UserDisplayText ?? m.Content : m.Content,
                    m.AttachmentPaths, m.ToolCalls
                }),
                plan = t.Plan,
                operations = t.Operations.Select(o => new { o.Id, o.ToolName, o.Status, o.CreatedUtc, o.CompletedUtc,
                    o.ErrorCode, o.ArgumentsJson, o.ResultJson }),
                liveActivityAvailable = activity.ContainsKey(t.Id),
                activity = activity.TryGetValue(t.Id, out var events) ? events : null
            }),
            rendered
        }, Options);
    }

    private static void Capture(DependencyObject element, List<object> result)
    {
        switch (element)
        {
            case RichTextBox rich:
                result.Add(new { type = "assistant-document", text = new TextRange(rich.Document.ContentStart, rich.Document.ContentEnd).Text,
                    blocks = rich.Document.Blocks.Select(DescribeBlock).ToArray() });
                return;
            case TextBox box:
                result.Add(new { type = "text-box", text = box.Text }); return;
            case TextBlock text:
                result.Add(new { type = "label", text = text.Text }); return;
            case Button button when button.Content is string title:
                result.Add(new { type = "button", text = title }); return;
            case Expander expander:
                result.Add(new { type = "activity-expander", header = expander.Header?.ToString(), expanded = expander.IsExpanded });
                break;
        }
        foreach (object child in LogicalTreeHelper.GetChildren(element))
            if (child is DependencyObject dependency) Capture(dependency, result);
    }

    private static object DescribeBlock(Block block) => block switch
    {
        Paragraph p => new { type = "paragraph", p.FontSize, p.LineHeight, weight = p.FontWeight.ToString(),
            inlines = p.Inlines.Select(DescribeInline).ToArray() },
        Table table => new { type = "table", columns = table.Columns.Count,
            rows = table.RowGroups.SelectMany(g => g.Rows).Select(r => r.Cells.Select(c => c.Blocks.Select(DescribeBlock).ToArray()).ToArray()).ToArray() },
        BlockUIContainer ui => new { type = "embedded-control", control = ui.Child?.GetType().Name },
        _ => new { type = block.GetType().Name }
    };

    private static object DescribeInline(Inline inline)
    {
        string? color = (inline.Foreground as SolidColorBrush)?.Color.ToString();
        return inline switch
        {
            Run r => new { type = "run", text = r.Text, color, weight = r.FontWeight.ToString(), style = r.FontStyle.ToString(), font = r.FontFamily.Source },
            Hyperlink link => new { type = "link", url = link.NavigateUri?.AbsoluteUri, color, children = link.Inlines.Select(DescribeInline).ToArray() },
            Span span => new { type = "span", color, weight = span.FontWeight.ToString(), style = span.FontStyle.ToString(), children = span.Inlines.Select(DescribeInline).ToArray() },
            LineBreak => new { type = "line-break" },
            _ => new { type = inline.GetType().Name }
        };
    }
}
