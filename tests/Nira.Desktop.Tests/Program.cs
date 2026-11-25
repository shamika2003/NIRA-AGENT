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
            NIRAAgent.UI.Presentation.NIRAVideoThumbnailService.DisableNetworkForOfflineAcceptance = true;
            NIRAAgent.UI.Presentation.NIRAVideoPlayer.DisableNetworkForOfflineAcceptance = true;
            const string watchUrl = "https://www.youtube.com/watch?v=abcdefghijk";
            // Valid JPEG loads immediately before the preview is mounted. No CDN access.
            Check(Nira.Core.NIRAVideoReferencePolicy.TryParse(watchUrl, out var posterReference) && posterReference is not null, "ui-poster-reference");
            var posterPixels = System.Windows.Media.Imaging.BitmapSource.Create(16, 9, 96, 96, PixelFormats.Bgr24, null, new byte[16 * 9 * 3], 16 * 3);
            var posterEncoder = new System.Windows.Media.Imaging.JpegBitmapEncoder();
            posterEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(posterPixels));
            using var posterStream = new System.IO.MemoryStream(); posterEncoder.Save(posterStream);
            int posterCalls = 0;
            Func<string, Task<byte[]?>> fakePoster = _ => { posterCalls++; return Task.FromResult<byte[]?>(posterStream.ToArray()); };
            var posterGrid = new Grid();
            var attachPoster = typeof(NIRAAgent.UI.Presentation.NIRAVideoThumbnailService).GetMethod("AttachCore", BindingFlags.Static | BindingFlags.NonPublic)!;
            attachPoster.Invoke(null, [posterGrid, posterReference!, fakePoster]);
            Check(!posterGrid.IsLoaded && posterCalls == 1 && posterGrid.Children[0] is Image { Source: not null, Opacity: 1 }, "ui-poster-eager-before-mounted");
            var invalidPoster = new Grid();
            Func<string, Task<byte[]?>> badPoster = _ => Task.FromResult<byte[]?>(new byte[] { 1, 2, 3 });
            attachPoster.Invoke(null, [invalidPoster, posterReference!, badPoster]);
            Check(invalidPoster.Children[0] is Image { Source: null, Opacity: 0 }, "ui-poster-invalid-keeps-fallback");

            var posterType = typeof(NIRAAgent.UI.Presentation.NIRAVideoThumbnailService);
            var markup = posterType.GetMethod("PosterMarkup", BindingFlags.Static | BindingFlags.NonPublic)!;
            string[] posterIds = ["abcdefghijk", "bbbbbbbbbbb", "ccccccccccc", "ddddddddddd", "eeeeeeeeeee", "abcdefghijk", "<bad>"];
            string posterHtml = (string)markup.Invoke(null, [posterIds])!;
            Check(posterIds.Take(5).All(id => posterHtml.Contains("https://i.ytimg.com/vi/" + id + "/hqdefault.jpg", StringComparison.Ordinal) &&
                posterHtml.Contains("https://img.youtube.com/vi/" + id + "/hqdefault.jpg", StringComparison.Ordinal)) &&
                !posterHtml.Contains("<bad>", StringComparison.Ordinal) && posterHtml.Split("<img").Length == 11,
                "ui-video-all-five-browser-posters-both-hosts");
            var posterPolicy = posterType.GetMethod("TryPosterId", BindingFlags.Static | BindingFlags.NonPublic)!;
            bool AllowedPoster(string url) => (bool)posterPolicy.Invoke(null, [url, posterIds.Take(5).ToHashSet(), ""])!;
            Check(AllowedPoster("https://i.ytimg.com/vi/abcdefghijk/hqdefault.jpg") &&
                !AllowedPoster("https://i.ytimg.com.evil.example/vi/abcdefghijk/hqdefault.jpg") &&
                !AllowedPoster("https://i.ytimg.com/vi/zzzzzzzzzzz/hqdefault.jpg"), "ui-video-browser-poster-evidence-bound");

            var videoState = new Nira.Core.ConversationState { Messages = [new Nira.Core.ChatEntry("user", watchUrl)] };
            var allowedVideos = Nira.Core.VideoEvidence.Allowed(videoState);
            Check(Nira.Core.VideoEvidence.Allows(allowedVideos, watchUrl) &&
                !Nira.Core.VideoEvidence.Allows(allowedVideos, "https://www.youtube.com/watch?v=bbbbbbbbbbb") &&
                !Nira.Core.NIRAVideoReferencePolicy.TryParse("https://www.youtube.com.evil.example/watch?v=abcdefghijk", out _), "ui-video-evidence-and-host-policy");
            var videoAnswer = (StackPanel)NIRAAgent.UI.Presentation.NIRARichAnswer.Create("[Watch demo](" + watchUrl + ")", videoUrls: allowedVideos);
            Check(videoAnswer.Children.Count == 1 && videoAnswer.Children[0] is Border, "ui-video-native-card-without-network");
            static IEnumerable<DependencyObject> VisualChildren(DependencyObject element)
            {
                yield return element;
                if (element is ContentControl { Content: DependencyObject content })
                    foreach (var child in VisualChildren(content)) yield return child;
                if (element is Decorator { Child: DependencyObject inner })
                    foreach (var child in VisualChildren(inner)) yield return child;
                if (element is Panel panel)
                    foreach (UIElement member in panel.Children)
                        foreach (var child in VisualChildren(member)) yield return child;
            }
            Check(videoAnswer.Children[0] is Border { Tag: "NIRA.VideoPlayer.AutoLoad" } &&
                !VisualChildren(videoAnswer).OfType<Button>().Any(b => b.Content?.ToString()?.Contains("Play video", StringComparison.OrdinalIgnoreCase) == true),
                "ui-video-direct-native-player-no-extra-play-button");
            Check(VisualChildren(videoAnswer).OfType<Button>().Any(b => b.Content?.ToString()?.Contains("Mini", StringComparison.Ordinal) == true) &&
                VisualChildren(videoAnswer).OfType<Button>().Any(b => b.Content?.ToString()?.Contains("Wide view", StringComparison.Ordinal) == true), "ui-video-restored-media-controls");
            var visibility = typeof(NIRAAgent.UI.Presentation.NIRAVideoPlayer).GetMethod("IsInViewport", BindingFlags.Static | BindingFlags.NonPublic)!;
            bool Visible(Rect bounds) => (bool)visibility.Invoke(null, [bounds, new Rect(0, 0, 800, 600)])!;
            Check(Visible(new Rect(20, 550, 500, 200)) && !Visible(new Rect(20, 600, 500, 200)) &&
                !Visible(new Rect(20, -201, 500, 200)) && !Visible(new Rect(20, 0, 0, 200)), "ui-video-autoload-visible-viewport-only");
            // Explicit release is safe even before initialization or mounting.
            var release = typeof(NIRAAgent.UI.Presentation.NIRAVideoPlayer).GetMethod("Release", BindingFlags.Static | BindingFlags.NonPublic)!;
            release.Invoke(null, [videoAnswer.Children[0]]);
            release.Invoke(null, [videoAnswer.Children[0]]);
            Check(VisualChildren(videoAnswer).OfType<Button>().Any(b => b.Content?.ToString()?.Contains("Reload player", StringComparison.Ordinal) == true && b.Visibility == Visibility.Visible),
                "ui-video-release-idempotent-and-reload-visible");
            var noEvidence = (StackPanel)NIRAAgent.UI.Presentation.NIRARichAnswer.Create("[Watch demo](" + watchUrl + ")");
            Check(noEvidence.Children.Count == 1 && noEvidence.Children[0] is RichTextBox, "ui-video-unobserved-stays-link");
            var sixUrls = Enumerable.Range(1, 6).Select(i => "https://www.youtube.com/watch?v=abcdefghij" + i).ToArray();
            var sixSet = new HashSet<string>(sixUrls, StringComparer.Ordinal);
            var sixTitles = sixUrls.Select((url, index) => (url, title: "Recorded video " + (index + 1))).ToDictionary(pair => pair.url, pair => pair.title, StringComparer.Ordinal);
            string videoTable = "| # | Video title | Short description | Link |\n|---|---|---|---|\n" +
                string.Join("\n", sixUrls.Select((url, index) => $"| {index + 1} | **Model title** | Short excerpt | {url} |"));
            var multiple = (StackPanel)NIRAAgent.UI.Presentation.NIRARichAnswer.Create(videoTable, videoUrls: sixSet, videoTitles: sixTitles);
            Check(multiple.Children.Count == 1 && multiple.Children[0] is Grid { Tag: "NIRA.VideoDepthDeck" }, "ui-video-six-table-rows-one-original-deck");
            var mixed = (StackPanel)NIRAAgent.UI.Presentation.NIRARichAnswer.Create("| Value | Link |\n|---|---|\n| 12 | " + sixUrls[0] + " |", videoUrls: sixSet);
            Check(mixed.Children.Count == 2 && mixed.Children[0] is RichTextBox && mixed.Children[1] is Border, "ui-video-ordinary-table-preserved");
            var codeOnly = (StackPanel)NIRAAgent.UI.Presentation.NIRARichAnswer.Create("```text\n" + sixUrls[0] + "\n```", videoUrls: sixSet);
            Check(codeOnly.Children.Count == 1 && codeOnly.Children[0] is RichTextBox, "ui-video-code-not-promoted");
            var rejected = (StackPanel)NIRAAgent.UI.Presentation.NIRARichAnswer.Create(videoTable, videoUrls: new HashSet<string>());
            Check(rejected.Children.Count == 1 && rejected.Children[0] is RichTextBox, "ui-video-unobserved-table-not-promoted");
            var duplicates = (StackPanel)NIRAAgent.UI.Presentation.NIRARichAnswer.Create(sixUrls[0] + "\n\n" + sixUrls[0], videoUrls: sixSet);
            Check(duplicates.Children.Count == 1 && duplicates.Children[0] is Border, "ui-video-duplicates-single-player");
            // Test the actual final-answer entry point, not just deck.TryAdd in isolation.
            string wrappedVideo = "```nira-ui\n{\"type\":\"video\",\"title\":\"Watch\",\"url\":\"" + sixUrls[0] + "\"}\n```";
            var aliasVideo = (StackPanel)NIRAAgent.UI.Presentation.NIRARichAnswer.Create(wrappedVideo, videoUrls: sixSet, videoTitles: sixTitles);
            Check(aliasVideo.Children.Count == 1 && aliasVideo.Children[0] is Border, "ui-video-single-object-url-alias");
            string malformedDeck = "```nira-ui\n{\"type\":\"video-carousel\",\"videos\":[" +
                string.Join(",", sixUrls.Take(5).Select(url => "\"" + url + "\"")) + "]}\n```";
            var recoveredDeck = (StackPanel)NIRAAgent.UI.Presentation.NIRARichAnswer.Create(malformedDeck, videoUrls: sixSet, videoTitles: sixTitles);
            Check(recoveredDeck.Children.Count == 1 && recoveredDeck.Children[0] is Grid { Tag: "NIRA.VideoDepthDeck" }, "ui-video-unknown-carousel-shape-recovered");
            string noUrlDeck = "Intro\n\n```nira-ui\n{\"type\":\"videos\",\"items\":[\"bad visual\"]}\n```\n\nEnding";
            var fallbackDeck = (StackPanel)NIRAAgent.UI.Presentation.NIRARichAnswer.Create(noUrlDeck, videoUrls: sixSet, videoTitles: sixTitles, searchFallback: sixUrls.Take(5).ToArray());
            Check(fallbackDeck.Children.OfType<Grid>().Any(g => Equals(g.Tag, "NIRA.VideoDepthDeck")) &&
                !fallbackDeck.Children.OfType<RichTextBox>().Any(box => Text(box).Contains("Some visual content", StringComparison.Ordinal) || Text(box).Contains("A video preview could not", StringComparison.Ordinal)), "ui-video-failed-visual-uses-recorded-search");
            var unobservedDeck = (StackPanel)NIRAAgent.UI.Presentation.NIRARichAnswer.Create(malformedDeck, videoUrls: new HashSet<string>());
            Check(!unobservedDeck.Children.OfType<Grid>().Any(), "ui-video-malformed-unobserved-not-embedded");
            var sixInline = (StackPanel)NIRAAgent.UI.Presentation.NIRARichAnswer.Create(string.Join(" ", sixUrls), videoUrls: sixSet);
            Check(sixInline.Children.OfType<Grid>().Any(g => Equals(g.Tag, "NIRA.VideoDepthDeck")), "ui-video-six-inline-references");
            var resizeDeck = new NIRAAgent.UI.Presentation.NIRAVideoDepthDeck();
            foreach (string url in sixUrls.Take(5))
                Check(resizeDeck.TryAdd(new NIRAAgent.Presentation.NIRARichBlock { Type = "video", Title = url, Text = url }), "ui-video-resize-add-" + url[^1]);
            resizeDeck.Complete();
            var playerField = resizeDeck.GetType().GetField("_player", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var resize = resizeDeck.GetType().GetMethod("TryResizeMountedPlayer", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var mountedPlayer = (StackPanel)playerField.GetValue(resizeDeck)!;
            var originalCard = mountedPlayer.Children[0];
            double oldWidth = mountedPlayer.Width;
            bool resized = (bool)resize.Invoke(resizeDeck, [520.0])!;
            Check(resized && ReferenceEquals(mountedPlayer, playerField.GetValue(resizeDeck)) &&
                ReferenceEquals(originalCard, mountedPlayer.Children[0]) && mountedPlayer.Width < oldWidth &&
                resizeDeck.ActiveIndex == 0, "ui-video-resize-preserves-provider-card-and-selection");
            var deck = new NIRAAgent.UI.Presentation.NIRAVideoDepthDeck();
            foreach (string url in sixUrls)
                Check(deck.TryAdd(new NIRAAgent.Presentation.NIRARichBlock { Type = "video", Title = sixTitles[url], Text = url }), "ui-video-deck-add-" + url[^1]);
            deck.Complete();
            string? savedMotion = Environment.GetEnvironmentVariable("NIRA_REDUCE_MOTION");
            try
            {
                Environment.SetEnvironmentVariable("NIRA_REDUCE_MOTION", "1");
                deck.SelectVideo(5);
                Check(deck.Count == 6 && deck.ActiveIndex == 5, "ui-video-sixth-card-accessible");
                deck.SelectVideo(0);
                Check(deck.ActiveIndex == 0, "ui-video-deck-can-return-to-first");
            }
            finally { Environment.SetEnvironmentVariable("NIRA_REDUCE_MOTION", savedMotion); }
            // Verify actual full-frame GIF decoding, including cell order and distinct pixels.
            using (var sheetStream = new System.IO.MemoryStream(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAgAAAAIAAgMAAACJFjxpAAAADFBMVEX/AAAA/wAAAP///wDWAo97AAABKElEQVR42u3RwQkAMAwCwKQ7ZslO2SF8hML5Vw7sCjNh/9RyAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAPqGA+MCAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACA3wEPqPIFUwqT7/oAAAAASUVORK5CYII=")))
            {
                var sheet = System.Windows.Media.Imaging.BitmapDecoder.Create(sheetStream,
                    System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
                    System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames[0];
                byte[] encodedGif = Nira.Infrastructure.NiraGifComposer.Compose(sheet, 250, default);
                using var gifStream = new System.IO.MemoryStream(encodedGif);
                var gif = new System.Windows.Media.Imaging.GifBitmapDecoder(gifStream,
                    System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
                    System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                var pixels = new List<string>();
                foreach (var frame in gif.Frames)
                {
                    var rgb = new System.Windows.Media.Imaging.FormatConvertedBitmap(frame, PixelFormats.Bgr24, null, 0);
                    byte[] pixel = new byte[3]; rgb.CopyPixels(new Int32Rect(64, 64, 1, 1), pixel, 3, 0);
                    pixels.Add(Convert.ToHexString(pixel));
                }
                Check(gif.Frames.Count == 4 && gif.Frames.All(frame => frame.PixelWidth == 256 && frame.PixelHeight == 256) &&
                    pixels.SequenceEqual(new[] { "0000FF", "00FF00", "FF0000", "00FFFF" }), "ui-gif-four-frames-and-cell-order");
            }
            string? oldMotion = Environment.GetEnvironmentVariable("NIRA_REDUCE_MOTION");
            try
            {
                Environment.SetEnvironmentVariable("NIRA_REDUCE_MOTION", "1");
                var transition = typeof(Nira.Desktop.MainWindow).GetMethod("TransitionSidebarAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
                var column = new ColumnDefinition { Width = new GridLength(276) };
                var leaving = new Border(); var arriving = new Border { Visibility = Visibility.Collapsed };
                ((Task)transition.Invoke(null, [column, leaving, arriving, 52.0, -10.0])!).GetAwaiter().GetResult();
                Check(column.Width.Value == 52 && leaving.Visibility == Visibility.Collapsed && arriving.Visibility == Visibility.Visible &&
                    leaving.IsHitTestVisible && arriving.IsHitTestVisible && arriving.Opacity == 1, "ui-sidebar-reduced-motion-final-state");
            }
            finally { Environment.SetEnvironmentVariable("NIRA_REDUCE_MOTION", oldMotion); }
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
            var repaired = Render("- **[color=#8651B5]What is a black hole?**[/color]\n- [color = #087A68]**Weird physics[/color]**\n- [color=#A34E22]Orphan");
            var repairedSpans = repaired.Document.Blocks.OfType<Paragraph>().Take(2).Select(p => p.Inlines.OfType<Span>().Single()).ToArray();
            Check(repairedSpans.Length == 2 && repairedSpans[0].Foreground is SolidColorBrush firstAccent &&
                repairedSpans[1].Foreground is SolidColorBrush secondAccent && firstAccent.Color != secondAccent.Color &&
                repairedSpans.All(s => s.Inlines.OfType<Span>().Any(b => b.FontWeight == FontWeights.Bold)) &&
                !Text(repaired).Contains("[color", StringComparison.OrdinalIgnoreCase) && !Text(repaired).Contains("[/color]", StringComparison.OrdinalIgnoreCase), "ui-color-crossed-delimiters-repaired");
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
