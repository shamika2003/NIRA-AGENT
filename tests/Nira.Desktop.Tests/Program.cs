using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using NIRAAgent.UI.Theming;

namespace Nira.Desktop.Tests;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var renderer = typeof(Nira.Desktop.MainWindow).Assembly.GetType("NIRAAgent.UI.Presentation.NIRAResponseDocument", throwOnError: true)!;
        var create = renderer.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!;
        var update = renderer.GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Static)!;
        RichTextBox Render(string text) => (RichTextBox)create.Invoke(null, [text])!;
        static string Text(RichTextBox box) => new TextRange(box.Document.ContentStart, box.Document.ContentEnd).Text;
        static void Check(bool ok, string name)
        {
            if (!ok) throw new InvalidOperationException("FAIL " + name);
            Console.WriteLine("PASS " + name);
        }
        try
        {
            var heading = Render("# Heading\n\n## C#\n\n### Subheading");
            var sizes = heading.Document.Blocks.OfType<Paragraph>().Select(p => p.FontSize).ToArray();
            Check(sizes.SequenceEqual(new double[] {24,20,17}) && Text(heading).Contains("C#", StringComparison.Ordinal), "ui-heading-hierarchy");
            var nested = Render("**Summary of *report.xlsx***\n\n***Both*** and ~~old~~");
            Check(Text(nested).Contains("Summary of report.xlsx", StringComparison.Ordinal) && !Text(nested).Contains('*') && !Text(nested).Contains('~'), "ui-nested-emphasis");
            var first = (Paragraph)nested.Document.Blocks.FirstBlock!;
            var bold = first.Inlines.OfType<Span>().First();
            Check(bold.FontWeight == FontWeights.Bold && bold.Inlines.OfType<Span>().Any(span => span.FontStyle == FontStyles.Italic), "ui-bold-italic-format");
            var table = Render("Item | Amount\n--- | ---\nRent | 1200\nFood | 300");
            var t = table.Document.Blocks.OfType<Table>().Single();
            Check(t.Columns.Count == 2 && t.RowGroups[0].Rows.Count == 3, "ui-table-layout");
            var code = Render("```csharp\nvar name = \"**literal**\";\n```\n\n[Docs](https://example.com)");
            Check(Text(code).Contains("**literal**", StringComparison.Ordinal) && code.Document.Blocks.OfType<BlockUIContainer>().Any(), "ui-code-preserved");
            Check(code.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Hyperlink>()).Any(l => l.NavigateUri.Scheme == "https"), "ui-explicit-link");
            var preview = Render("");
            update.Invoke(null, [preview, "# Streaming\n\n**Partial"]);
            Check(Text(preview).Contains("Partial", StringComparison.Ordinal) && !Text(preview).Contains('*'), "ui-streaming-markdown");
            update.Invoke(null, [preview, "| A | B |\n| --- | --- |\n| 1 | 2 |\n"]);
            Check(preview.Document.Blocks.OfType<Table>().Single().RowGroups[0].Rows.Count == 2, "ui-streaming-table");
            var quotation = Render("> *“An AI agent must be **transparent** and **verifiable**.”* – Example");
            var quote = (Paragraph)quotation.Document.Blocks.FirstBlock!;
            var italic = quote.Inlines.OfType<Span>().First();
            Check(italic.FontStyle == FontStyles.Italic && italic.Inlines.OfType<Span>().Count(x => x.FontWeight == FontWeights.Bold) == 2 &&
                !Text(quotation).Contains('*'), "ui-italic-contains-bold");
            var inlineItalic = Render("*The code demonstrates how to **request** a tool.*");
            Check(!Text(inlineItalic).Contains('*') && ((Paragraph)inlineItalic.Document.Blocks.FirstBlock!).Inlines.OfType<Span>().Single().FontStyle == FontStyles.Italic,
                "ui-italic-code-caption");
            NIRAThemeManager.ApplyTheme(NIRAThemeMode.Halo, persist: false);
            var colors = Render("## [color=#A02020]Risk[/color]\n\n[color=#006040]**Ready**[/color]");
            var colorSpans = colors.Document.Blocks.OfType<Paragraph>().SelectMany(x => x.Inlines.OfType<Span>()).ToArray();
            Check(colorSpans.Length == 2 && colorSpans[0].Foreground is SolidColorBrush red && colorSpans[1].Foreground is SolidColorBrush green && red.Color != green.Color &&
                !Text(colors).Contains("[color=", StringComparison.Ordinal), "ui-model-selected-color");
            var lightColor = ((SolidColorBrush)colorSpans[0].Foreground).Color;
            NIRAThemeManager.ApplyTheme(NIRAThemeMode.Eclipse, persist: false);
            var darkColors = Render("## [color=#A02020]Risk[/color]");
            var darkColor = ((SolidColorBrush)((Paragraph)darkColors.Document.Blocks.FirstBlock!).Inlines.OfType<Span>().Single().Foreground).Color;
            Check(lightColor != darkColor, "ui-color-theme-contrast");
            const string example = "public class Demo { int n = 42; // note\n    string s = \"literal\"; }";
            var syntax = Render("```csharp\n" + example + "\n```");
            var syntaxBlock = syntax.Document.Blocks.OfType<Paragraph>().Single();
            var runs = syntaxBlock.Inlines.OfType<Run>().ToArray();
            Check(string.Concat(runs.Select(x => x.Text)) == example && runs.Select(x => (x.Foreground as SolidColorBrush)?.Color).Distinct().Count() >= 5,
                "ui-code-syntax-and-exact-content");
            var literal = Render("````text\n```\n[color=#FF0000]literal[/color]\n````");
            var literalBlock = literal.Document.Blocks.OfType<Paragraph>().Single();
            Check(string.Concat(literalBlock.Inlines.OfType<Run>().Select(x => x.Text)) == "```\n[color=#FF0000]literal[/color]", "ui-code-fence-literal");
            update.Invoke(null, [preview, "## [color=#006040]Streaming"]);
            Check(Text(preview).Contains("Streaming", StringComparison.Ordinal) && !Text(preview).Contains("[color=", StringComparison.Ordinal), "ui-streaming-model-color");
            update.Invoke(null, [preview, "```python\ndef demo():\n    return 42"]);
            Check(preview.Document.Blocks.OfType<Paragraph>().Single().Inlines.OfType<Run>().Count() > 2, "ui-streaming-code-highlight");
            var breaks = Render("| A | B |\n| --- | --- |\n| First | **One**<br>Two<br/>Three<BR />Four |");
            var cell = (Paragraph)breaks.Document.Blocks.OfType<Table>().Single().RowGroups[0].Rows[1].Cells[1].Blocks.FirstBlock!;
            Check(cell.Inlines.OfType<LineBreak>().Count() == 3 && !Text(breaks).Contains("<br", StringComparison.OrdinalIgnoreCase), "ui-table-cell-line-breaks");
            var ragged = Render("| A | B | C |\n| --- | --- | --- |\n| One | Two |\n| Three | Four | Five | Extra |\n| Six | Seven | Eight |\n\nEnd paragraph.");
            var raggedTable = ragged.Document.Blocks.OfType<Table>().Single();
            Check(raggedTable.RowGroups[0].Rows.Count == 4 && raggedTable.RowGroups[0].Rows.Cast<TableRow>().All(row => row.Cells.Count == 3) && Text(ragged).Contains("Extra") && Text(ragged).Contains("End paragraph."), "ui-ragged-table-preserves-content");
            var pipes = Render("| A | B |\n| --- | --- |\n| `a|b` | x\\|y |");
            Check(pipes.Document.Blocks.OfType<Table>().Single().RowGroups[0].Rows.Count == 2 && Text(pipes).Contains("a|b") && Text(pipes).Contains("x|y"), "ui-table-pipe-literals");
            var keycap = Render("## 1️⃣ Overview\n\n```text\n<br> 2️⃣\n```");
            Check(Text(keycap).Contains("1.  Overview") || Text(keycap).Contains("1. Overview"), "ui-keycap-heading-fallback");
            Check(Text(keycap).Contains("<br> 2️⃣"), "ui-code-keeps-break-and-emoji-literals");
            var exporter = typeof(Nira.Desktop.MainWindow).Assembly.GetType("NIRAAgent.UI.Presentation.DeveloperChatExport", throwOnError: true)!;
            var activityType = exporter.GetNestedType("ActivityEntry", BindingFlags.NonPublic)!;
            var dictionaryType = typeof(Dictionary<,>).MakeGenericType(typeof(Guid), typeof(List<>).MakeGenericType(activityType));
            var task = new Nira.Core.ConversationState { Messages = [new("system", "SECRET_SYSTEM_TEST"), new("user", "hello"), new("assistant", "**hello**")] };
            task.Operations.Add(new Nira.Core.AgentOperation { ToolName = "web.search", ArgumentsJson = "{}", ResultJson = "{}" });
            string diagnostic = (string)exporter.GetMethod("Build", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null,
                [new[] { task }, Activator.CreateInstance(dictionaryType), breaks, "Halo", 1200d, 800d, 276d, 292d, "test-model", "test-vision"])!;
            using var json = System.Text.Json.JsonDocument.Parse(diagnostic);
            Check(!diagnostic.Contains("SECRET_SYSTEM_TEST") && diagnostic.Contains("web.search") && diagnostic.Contains("line-break") && diagnostic.Contains("**hello**") && json.RootElement.GetProperty("turns").GetArrayLength() == 1, "ui-debug-export-records-and-render-tree");
            var normalColors = Render("## Overview\n\n**Important concept** remains readable.");
            Check(((Paragraph)normalColors.Document.Blocks.FirstBlock!).Foreground is SolidColorBrush &&
                normalColors.Document.Blocks.OfType<Paragraph>().Last().Inlines.OfType<Span>().First().Foreground is SolidColorBrush, "ui-default-color-without-user-prompt");
            string publicAnswer = NIRAAgent.UI.Presentation.NIRAPublicDelivery.Answer("## Images\nUseful source: https://example.com\n\n**How the list was obtained**\n- Used `image.search` with receipt ID secret\n\n**Important notes**\nRights are unverified.\n\nHost-recorded execution evidence:\n- filesystem.write_text [private]: raw args\n  path: private");
            Check(publicAnswer.Contains("Useful source") && publicAnswer.Contains("Rights are unverified") && !publicAnswer.Contains("image.search") && !publicAnswer.Contains("private") && !publicAnswer.Contains("receipt ID"), "ui-public-answer-no-internal-diary");
            Check(NIRAAgent.UI.Presentation.NIRAPublicDelivery.Activity(new Nira.Core.AgentProgress("routing", "5/43 active capability schemas", "private")) is null &&
                NIRAAgent.UI.Presentation.NIRAPublicDelivery.Activity(new Nira.Core.AgentProgress("tool", "secret", "image.search")) == "Finding relevant images", "ui-public-activity-no-routing");
            var selective = Render("Neutral sentence with **important detail** and ordinary text.");
            var selectiveP = (Paragraph)selective.Document.Blocks.FirstBlock!;
            var emphasis = selectiveP.Inlines.OfType<Span>().Single();
            Check(emphasis.Foreground is SolidColorBrush accent && (selectiveP.Inlines.OfType<Run>().First().Foreground is not SolidColorBrush ordinary || accent.Color != ordinary.Color), "ui-selective-inline-effective-rgb");
            var rich = NIRAAgent.UI.Presentation.NIRARichAnswer.Create("Intro\n\n```nira-ui\n[{\"type\":\"card\",\"title\":\"First\",\"text\":\"Useful\",\"icon\":\"brain\",\"color\":\"#7755CC\"},{\"type\":\"card\",\"title\":\"Second\",\"text\":\"Another\"}]\n```\n\nEnding");
            Check(rich is StackPanel richPanel && richPanel.Children.Count == 3 && richPanel.Children[1] is WrapPanel, "ui-real-rich-component-group");
            var invalid = NIRAAgent.UI.Presentation.NIRARichAnswer.Parse("[{\"type\":\"card\",\"text\":\"image.search receipt ID abc\"},{\"type\":\"tabs\",\"items\":[\"a\",\"b\"],\"panels\":[\"a\"]},{\"type\":\"unknown\",\"text\":\"no\"}]");
            Check(invalid.Count == 0, "ui-rich-rejects-private-invalid-and-unknown");
            var data = NIRAAgent.UI.Presentation.NIRARichAnswer.Parse("[{\"type\":\"bar\",\"labels\":[\"A\",\"B\"],\"values\":[2,3]},{\"type\":\"progress\",\"title\":\"Progress\",\"value\":2,\"maximum\":3},{\"type\":\"equation\",\"text\":\"x^2\"},{\"type\":\"tabs\",\"items\":[\"A\",\"B\"],\"panels\":[\"One\",\"Two\"]},{\"type\":\"checklist\",\"items\":[\"First\",\"Second\"]}]");
            Check(data.Count == 5 && data.All(block => NIRAAgent.UI.Presentation.NIRARichBlockRenderer.Render(block) is FrameworkElement), "ui-rich-real-native-controls");
            Check(NIRAAgent.UI.Presentation.NIRAPublicDelivery.Status(Nira.Core.ConversationStatus.NeedsReconciliation).Contains("review", StringComparison.OrdinalIgnoreCase), "ui-natural-uncertainty-status");
            var varied = Render("## First\n\n**First idea**.\n\n## Second\n\n**Second idea**.\n\n## Third\n\n**Third idea**.");
            var variedColors = varied.Document.Blocks.OfType<Paragraph>().SelectMany(x => x.Inlines.OfType<Span>())
                .Select(x => ((SolidColorBrush)x.Foreground).Color).Distinct().Count();
            Check(variedColors == 3, "ui-section-accents-are-distinct");
            var math = Render("Before\n\n\\[\n\\boxed{L = \\frac{L_{0}}{\\gamma}}\n\\]\n\nAfter \\(\\gamma\\approx 1\\) and $5 plus $10.\n\n| A | B |\n| --- | --- |\n| Formula | \\(E=m c^{2}\\) |");
            Check(math.Document.Blocks.OfType<BlockUIContainer>().Count() == 1 &&
                math.Document.Blocks.OfType<Paragraph>().SelectMany(x => x.Inlines.OfType<InlineUIContainer>()).Count() == 1 &&
                ((Paragraph)math.Document.Blocks.OfType<Table>().Single().RowGroups[0].Rows[1].Cells[1].Blocks.FirstBlock!).Inlines.OfType<InlineUIContainer>().Count() == 1 &&
                Text(math).Contains("$5 plus $10") && !Text(math).Contains("\\boxed"), "ui-display-inline-table-math-and-currency");
            var rawMath = Render("```python\nprint(r'\\[x\\]')\n```");
            Check(Text(rawMath).Contains(@"\[x\]"), "ui-math-not-interpreted-in-code");
            var live = new NIRAAgent.UI.Presentation.NIRALivePreview();
            Check(!live.Append("First **concept**") && live.Text.Length == 0 &&
                live.Append(" is useful.\n\n") && live.Text.Contains("First **concept**"), "ui-live-preview-complete-prose");
            Check(!live.Append("{\"ur") && !live.Append("ls\":[\"https://example.com\"]}\n\n") &&
                !live.Text.Contains("urls") && !live.Append("More prose.\n\n"), "ui-live-preview-split-json-blocked");
            live.Reset();
            Check(live.Text.Length == 0 && !live.Append("```nira-ui\n[{\"type\":\"card\"}]\n```\n\n") &&
                live.Text.Length == 0, "ui-live-preview-reset-and-rich-json-hidden");
            live.Reset();
            Check(!live.Append("The image.re") && !live.Append("trieve operation is running.\n\n") && live.Text.Length == 0, "ui-live-preview-split-internal-name-blocked");
            live.Reset();
            Check(live.Append("[color=#8651B5]Concept[/color] is useful.\n\n"), "ui-live-preview-explicit-color");
            // Original masonry panel: unlike fixed WrapPanel rows, the third tile uses
            // the shortest column; changing viewport width reflows the same children.
            var masonry = new NIRAAgent.UI.Presentation.NIRAMasonryPanel();
            var tile1 = new Border { Height = 100 }; var tile2 = new Border { Height = 210 }; var tile3 = new Border { Height = 70 };
            masonry.Children.Add(tile1); masonry.Children.Add(tile2); masonry.Children.Add(tile3);
            masonry.Measure(new Size(500, double.PositiveInfinity));
            masonry.Arrange(new Rect(0, 0, 500, masonry.DesiredSize.Height));
            var firstSlot = System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(tile1);
            var secondSlot = System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(tile2);
            var thirdSlot = System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(tile3);
            Check(firstSlot.X == 0 && secondSlot.X > 0 && thirdSlot.X == 0 && thirdSlot.Y == 115 &&
                firstSlot.Height == 100 && secondSlot.Height == 210, "ui-original-gallery-masonry-shortest-column");
            masonry.Measure(new Size(230, double.PositiveInfinity));
            masonry.Arrange(new Rect(0, 0, 230, masonry.DesiredSize.Height));
            Check(System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(tile2).X == 0 &&
                System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(tile2).Y == 115, "ui-original-gallery-masonry-reflows-on-resize");
            Console.WriteLine("NIRA DESKTOP RENDERER TESTS PASS");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
