using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using NIRAAgent.UI.ViewModels;
using NIRAAgent.UI.Visuals;
using NIRAAgent.Presentation;

namespace NIRAAgent.UI.Presentation;

/// <summary>
/// Safe document layout for assistant prose. Explicit Markdown is rendered as
/// native WPF controls; ordinary conversation remains ordinary paragraphs.
/// Typed displayBlocks remain the preferred format for tabs/cards/timelines.
/// This parser never executes model-authored markup or invents facts.
/// </summary>
public static class NIRAChatDocument
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached("Text", typeof(string), typeof(NIRAChatDocument),
            new PropertyMetadata(string.Empty, OnChanged));

    public static void SetText(DependencyObject element, string value) =>
        element.SetValue(TextProperty, value);
    public static string GetText(DependencyObject element) =>
        (string)element.GetValue(TextProperty);

    public static readonly DependencyProperty EmbeddedVideoKeysProperty =
        DependencyProperty.RegisterAttached("EmbeddedVideoKeys", typeof(string),
            typeof(NIRAChatDocument), new PropertyMetadata(string.Empty, OnKeysChanged));

    public static void SetEmbeddedVideoKeys(DependencyObject element, string value) =>
        element.SetValue(EmbeddedVideoKeysProperty, value);
    public static string GetEmbeddedVideoKeys(DependencyObject element) =>
        (string)element.GetValue(EmbeddedVideoKeysProperty);

    // Rich blocks are rendered in a separate WPF row. Photos belonging to
    // those blocks are inserted there by ChatMessageViewModel, never before
    // the row simply because plain-text layout finished first.
    public static readonly DependencyProperty HasRichBlocksProperty =
        DependencyProperty.RegisterAttached("HasRichBlocks", typeof(bool),
            typeof(NIRAChatDocument), new PropertyMetadata(false, OnRichChanged));
    public static void SetHasRichBlocks(DependencyObject owner, bool value) =>
        owner.SetValue(HasRichBlocksProperty, value);
    public static bool GetHasRichBlocks(DependencyObject owner) =>
        (bool)owner.GetValue(HasRichBlocksProperty);
    private static void OnRichChanged(DependencyObject owner, DependencyPropertyChangedEventArgs _) =>
        Build(owner, GetText(owner));

    private static void OnKeysChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) =>
        Build(sender, GetText(sender));

    // This attached collection carries already verified local image artifacts.
    // The revision binding updates the document when an image arrives *after*
    // the prose, avoiding any second LLM call or remote image load at render.
    public static readonly DependencyProperty InlineVisualsProperty =
        DependencyProperty.RegisterAttached("InlineVisuals", typeof(object),
            typeof(NIRAChatDocument), new PropertyMetadata(null, OnInlineChanged));
    public static void SetInlineVisuals(DependencyObject owner, object? value) =>
        owner.SetValue(InlineVisualsProperty, value);
    public static object? GetInlineVisuals(DependencyObject owner) =>
        owner.GetValue(InlineVisualsProperty);
    public static readonly DependencyProperty InlineVisualRevisionProperty =
        DependencyProperty.RegisterAttached("InlineVisualRevision", typeof(int),
            typeof(NIRAChatDocument), new PropertyMetadata(0, OnInlineChanged));
    public static void SetInlineVisualRevision(DependencyObject owner, int value) =>
        owner.SetValue(InlineVisualRevisionProperty, value);
    public static int GetInlineVisualRevision(DependencyObject owner) =>
        (int)owner.GetValue(InlineVisualRevisionProperty);
    private static void OnInlineChanged(DependencyObject owner, DependencyPropertyChangedEventArgs _) =>
        Build(owner, GetText(owner));

    private static readonly Regex Heading = new(@"^\s{0,3}(#{1,4})\s+(.+)$",
        RegexOptions.CultureInvariant);
    private static readonly Regex Bullet = new(@"^\s{0,3}(?<mark>[-*+]|\d{1,3}[.)])\s+(?<body>.*)$",
        RegexOptions.CultureInvariant);
    private static readonly Regex Task = new(@"^\s{0,3}[-*+]\s+\[(?<done>[ xX])\]\s+(?<body>.+)$",
        RegexOptions.CultureInvariant);
    private static readonly Regex TableSeparator = new(@"^:?-{3,}:?$",
        RegexOptions.CultureInvariant);

    // Recovery for an LLM that mistakenly prints a video display-block object
    // in prose. Only one-line video objects with a validated provider URL are
    // promoted. Ordinary JSON/code blocks and unrecognized objects are untouched.
    private static readonly Regex RawVideoRecord = new(
        @"^\s*\{\s*['""]type['""]\s*:\s*['""]video['""]\s*,.*\}\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex RawVideoUrl = new(
        @"['""]text['""]\s*:\s*(?<q>['""])(?<url>https://[^'""]{1,1900})\k<q>",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex RawVideoTitle = new(
        @"['""]title['""]\s*:\s*(?<q>['""])(?<title>.{0,150}?)\k<q>(?=\s*[,}])",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);


    // A YouTube playlist is NOT an individual YouTube video ID. The model can
    // accidentally emit it as a raw {type:video} object. Render a safe link,
    // not the JSON syntax and not a fake player with a missing video ID.
    private static bool IsYouTubePlaylist(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0 ||
            uri.Port != 443 || uri.Host is not ("youtube.com" or "www.youtube.com" or "m.youtube.com") ||
            !uri.AbsolutePath.Equals("/playlist", StringComparison.OrdinalIgnoreCase))
            return false;
        return Regex.IsMatch(uri.Query, @"(?:^\?|&)list=[A-Za-z0-9_-]{8,100}(?:&|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static void OnChanged(DependencyObject element, DependencyPropertyChangedEventArgs e) =>
        Build(element, (string?)e.NewValue ?? string.Empty);

    private static void Build(DependencyObject element, string raw)
    {
        if (element is not StackPanel root) return;
        root.Children.Clear();
        string source = raw.Replace("\r\n", "\n").Replace('\r', '\n');
        var knownVideoKeys = new HashSet<string>(
            GetEmbeddedVideoKeys(element).Split('|', StringSplitOptions.RemoveEmptyEntries),
            StringComparer.OrdinalIgnoreCase);
        var embeddedLinks = new List<(NIRAVideoReference Video, string Title)>();
        var queuedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        NIRAVideoDepthDeck? videoDeck = null;
        void AddVideo(NIRARichBlock videoBlock)
        {
            videoDeck ??= new NIRAVideoDepthDeck();
            if (videoDeck.TryAdd(videoBlock) && videoDeck.Count == 1)
                root.Children.Add(videoDeck.View);
        }
        if (string.IsNullOrWhiteSpace(source)) return;

        // Preserve the original gallery for generated, local, unsupported or
        // mixed artifact sets. Use inline cards only for 1-4 independently
        // retrieved and decoded source-backed photographs in a substantive answer.
        var photos = GetInlineVisuals(element) as IReadOnlyList<VisualArtifactViewModel>;
        // Text-anchored photos also belong inside replies that have typed rich
        // blocks. The earlier !HasRichBlocks gate forced every retrieved photo
        // into a separate rich-elements row after the entire explanation.
        bool inlinePhotos = photos is { Count: > 0 and <= 12 } && source.Length >= 100 &&
            photos.All(p => p.HasPreview && p.HasSourceUri && !p.IsGeneratedImage);
        var pendingPhotos = inlinePhotos ? photos!.Select(p => new
        {
            Artifact = p,
            Anchor = NIRAInlineImageAnchorPolicy.FindLine(source,
                string.IsNullOrWhiteSpace(p.SubjectQuery) ? p.Title : p.SubjectQuery)
        }).ToList() : null;
        var placedPhotos = new HashSet<Guid>();
        int currentLine = -1;
        void PlacePhotosThrough(int lastLine)
        {
            if (pendingPhotos is null) return;
            foreach (var candidate in pendingPhotos)
            {
                // Never manufacture a position for an unrelated/unmatched photo.
                // Rich-section matching handles those independently.
                if (placedPhotos.Contains(candidate.Artifact.ArtifactId) ||
                    candidate.Anchor < 0 || candidate.Anchor > lastLine)
                    continue;
                root.Children.Add(MakeInlinePhoto(candidate.Artifact,
                    prominent: placedPhotos.Count == 0));
                placedPhotos.Add(candidate.Artifact.ArtifactId);
            }
        }

        string[] lines = source.Split('\n');
        var paragraph = new List<string>();
        var code = new List<string>();
        bool inCode = false;

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            string text = string.Join("\n", paragraph).Trim();
            paragraph.Clear();
            if (text.Length > 0)
            {
                root.Children.Add(MakeText(text, 15, 25.5, new Thickness(0, 0, 0, 17)));
                PlacePhotosThrough(currentLine);
            }
        }
        void FlushCode()
        {
            if (code.Count == 0) return;
            var body = new TextBlock
            {
                Text = string.Join("\n", code), TextWrapping = TextWrapping.Wrap,
                FontSize = 12.5, LineHeight = 21, Margin = new Thickness(12, 10, 12, 10),
                FontFamily = new FontFamily("Cascadia Code, Consolas")
            };
            body.SetResourceReference(TextBlock.ForegroundProperty, "NIRATextBrush");
            var frame = new Border { CornerRadius = new CornerRadius(10),
                Margin = new Thickness(0, 3, 0, 14), BorderThickness = new Thickness(1), Child = body };
            frame.SetResourceReference(Border.BackgroundProperty, "NIRAGlassStrongTintBrush");
            frame.SetResourceReference(Border.BorderBrushProperty, "NIRAGlassBorderBrush");
            root.Children.Add(frame);
            code.Clear();
        }

        for (int i = 0; i < lines.Length; i++)
        {
            currentLine = i;
            string line = lines[i].TrimEnd();
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph();
                if (inCode) FlushCode();
                inCode = !inCode;
                continue;
            }
            if (inCode) { code.Add(line); continue; }
            if (string.IsNullOrWhiteSpace(line)) { FlushParagraph(); continue; }

            // The model sometimes emits {'type':'video', 'title':'...',
            // 'text':'https://youtube.com/...'} in the answer itself.
            // Do not show raw UI metadata in the conversation, and do not
            // duplicate an existing typed or prose video player.
            if (line.Length <= 2200 && RawVideoRecord.IsMatch(line))
            {
                Match urlField = RawVideoUrl.Match(line);
                if (urlField.Success && IsYouTubePlaylist(urlField.Groups["url"].Value))
                {
                    FlushParagraph();
                    Match titleField = RawVideoTitle.Match(line);
                    string label = titleField.Success ? titleField.Groups["title"].Value.Trim() : "YouTube playlist";
                    if (label.Length == 0) label = "YouTube playlist";
                    if (label.Length > 100) label = label[..100] + "…";
                    // User/model title is display text only; do not treat it as markup.
                    label = label.Replace("[", "").Replace("]", "").Replace("(", "").Replace(")", "");
                    string playlistLink = $"[{label}]({urlField.Groups["url"].Value})";
                    root.Children.Add(MakeText(playlistLink, 13.5, 23,
                        new Thickness(1, 5, 0, 11)));
                    continue;
                }
                if (urlField.Success &&
                    NIRAVideoReferencePolicy.TryParse(urlField.Groups["url"].Value, out var rawVideo) &&
                    rawVideo != null)
                {
                    FlushParagraph();
                    Match titleField = RawVideoTitle.Match(line);
                    string rawTitle = titleField.Success
                        ? titleField.Groups["title"].Value.Trim() : "Watch video";
                    if (rawTitle.Length == 0) rawTitle = "Watch video";
                    if (rawTitle.Length > 125) rawTitle = rawTitle[..125] + "…";
                    queuedKeys.Remove(rawVideo.Key);
                    embeddedLinks.RemoveAll(item => item.Video.Key.Equals(rawVideo.Key,
                        StringComparison.OrdinalIgnoreCase));
                    if (knownVideoKeys.Add(rawVideo.Key))
                        AddVideo(new NIRARichBlock
                        {
                            Type = "video", Title = rawTitle, Text = rawVideo.OriginalUrl
                        });
                    continue;
                }
            }


            // A reply may cite several YouTube/Vimeo watch URLs INSIDE ordinary
            // paragraphs. Keep the original prose and links intact, but present
            // each distinct supported video once at the end of this reply.
            // Validation goes through the same strict video-reference policy.
            foreach (Match hit in Regex.Matches(line, @"https://[^\s<>\]\)\(]+",
                         RegexOptions.CultureInvariant | RegexOptions.IgnoreCase))
            {
                string candidate = hit.Value.TrimEnd('.', ',', ';', ':', '!', '?', '*');
                if (!NIRAVideoReferencePolicy.TryParse(candidate, out var inlineVideo) ||
                    inlineVideo is null || knownVideoKeys.Contains(inlineVideo.Key) ||
                    !queuedKeys.Add(inlineVideo.Key)) continue;
                string label = "Watch " + inlineVideo.Provider + " video";
                // Preserve an authored, concise Markdown label where available.
                int start = hit.Index;
                int open = line.LastIndexOf('[', Math.Max(0, start - 2));
                // Standard [label](https://...) shape (without space).
                if (start >= 2 && line.AsSpan(start - 2, 2).SequenceEqual("](".AsSpan()) &&
                    open >= 0 && start - open < 155)
                {
                    string userLabel = line[(open + 1)..(start - 2)].Trim('*', '_', ' ');
                    if (userLabel.Length is > 0 and <= 125) label = userLabel;
                }
                embeddedLinks.Add((inlineVideo, label));
            }

            // Markdown table: recognize an actual header + separator before
            // converting anything. Never guess that pipe-delimited prose is data.
            if (i + 2 < lines.Length &&
                TryTableHeader(lines[i], lines[i + 1], out string[] headers))
            {
                var rows = new List<IReadOnlyList<string>>();
                int next = i + 2;
                while (next < lines.Length && rows.Count < 40 &&
                       TryCells(lines[next], out string[] cells) && cells.Length == headers.Length)
                {
                    rows.Add(cells);
                    next++;
                }
                if (rows.Count > 0)
                {
                    FlushParagraph();
                    root.Children.Add(NIRARichBlockRenderer.Render(new NIRARichBlock
                    {
                        Type = "table", Columns = headers, Rows = rows
                    }));
                    i = next - 1;
                    continue;
                }
            }

            // Markdown task syntax has real interactive checkboxes. This is
            // local display state, not a NIRA goal, action, or claim of completion.
            if (Task.IsMatch(line))
            {
                var items = new List<string>();
                var checkedIndices = new List<int>();
                int next = i;
                while (next < lines.Length && items.Count < 24)
                {
                    Match match = Task.Match(lines[next]);
                    if (!match.Success) break;
                    if (!string.Equals(match.Groups["done"].Value, " ", StringComparison.Ordinal))
                        checkedIndices.Add(items.Count);
                    items.Add(match.Groups["body"].Value.Trim());
                    next++;
                }
                FlushParagraph();
                root.Children.Add(NIRARichBlockRenderer.Render(new NIRARichBlock
                {
                    Type = "checklist", Title = "Checklist", Items = items
                }, new NIRARichInteractionState { CheckedIndices = checkedIndices }));
                i = next - 1;
                continue;
            }

            // Videos already represented by a typed displayBlock must NOT be
            // repeated as a second player merely because NIRA cited its URL.
            // Ordinary video links receive the identical player without asking
            // cognition to change its existing answer construction.
            if (NIRAVideoReferencePolicy.TryParseStandaloneLine(line,
                out var video, out string videoTitle) && video != null)
            {
                FlushParagraph();
                queuedKeys.Remove(video.Key);
                embeddedLinks.RemoveAll(item => item.Video.Key.Equals(video.Key,
                    StringComparison.OrdinalIgnoreCase));
                if (knownVideoKeys.Add(video.Key))
                    AddVideo(new NIRARichBlock
                    {
                        Type = "video", Title = videoTitle, Text = video.OriginalUrl
                    });
                continue;
            }

            // A standalone Markdown source/reference is a compact themed
            // clickable resource row. Do not mistake an inline prose link for
            // a source card or imply that an LLM-authored URL was verified.
            if (NIRAResourceLinkPolicy.TryParse(line, out var resource) && resource != null)
            {
                FlushParagraph();
                root.Children.Add(MakeReferenceRow(resource));
                continue;
            }

            Match heading = Heading.Match(line);
            if (heading.Success)
            {
                FlushParagraph();
                int depth = heading.Groups[1].Length;
                double size = depth switch { 1 => 24, 2 => 20, 3 => 17, _ => 15 };
                var title = MakeText(heading.Groups[2].Value, size,
                    depth == 1 ? 33 : 28,
                    new Thickness(0, depth == 1 ? 19 : 15, 0, 12));
                title.FontWeight = FontWeights.SemiBold;
                if (depth <= 2)
                    title.SetResourceReference(TextBlock.ForegroundProperty, "NIRACyanBrush");
                root.Children.Add(title);
                PlacePhotosThrough(currentLine);
                continue;
            }
            Match bullet = Bullet.Match(line);
            if (bullet.Success)
            {
                FlushParagraph();
                var grid = new Grid { Margin = new Thickness(4, 4, 3, 9) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                string marker = Regex.IsMatch(bullet.Groups["mark"].Value, @"\d")
                    ? bullet.Groups["mark"].Value : "•";
                var symbol = MakeText(marker, 13.5, 24, new Thickness(0));
                symbol.FontWeight = FontWeights.SemiBold;
                symbol.SetResourceReference(TextBlock.ForegroundProperty, "NIRACyanBrush");
                var body = MakeText(bullet.Groups["body"].Value, 15, 25, new Thickness(0));
                Grid.SetColumn(body, 1);
                grid.Children.Add(symbol); grid.Children.Add(body);
                root.Children.Add(grid);
                // Place retrieved reference images immediately after their
                // matching explanatory bullet/caption, not after the summary.
                PlacePhotosThrough(currentLine);
                continue;
            }
            if (Regex.IsMatch(line, @"^\s*(?:-{3,}|\*{3,}|_{3,})\s*$"))
            {
                FlushParagraph();
                var rule = new Border { Height = 1, Margin = new Thickness(0, 14, 0, 20),
                    HorizontalAlignment = HorizontalAlignment.Stretch };
                rule.SetResourceReference(Border.BackgroundProperty, "NIRAGlassBorderBrush");
                root.Children.Add(rule);
                continue;
            }
            if (line.TrimStart().StartsWith('>'))
            {
                FlushParagraph();
                var quote = MakeText(line.TrimStart().TrimStart('>').TrimStart(), 14.5, 24,
                    new Thickness(15, 9, 9, 9));
                var panel = new Border { Child = quote, BorderThickness = new Thickness(3, 0, 0, 0),
                    Margin = new Thickness(0, 5, 0, 15) };
                panel.SetResourceReference(Border.BorderBrushProperty, "NIRACyanBrush");
                root.Children.Add(panel);
                continue;
            }
            paragraph.Add(line.Trim());
        }
        currentLine = lines.Length;
        FlushParagraph(); FlushCode();
        // Unmatched photos are NOT appended to the end of the response. The
        // rich block presenter may attach them to a strongly matching section.
        foreach (var item in embeddedLinks)
        {
            if (!knownVideoKeys.Add(item.Video.Key)) continue;
            AddVideo(new NIRARichBlock
            {
                Type = "video", Title = item.Title, Text = item.Video.OriginalUrl
            });
        }
    }

    private static Border MakeReferenceRow(NIRAResourceLinkPresentation resource)
    {
        var panel = new StackPanel { Margin = new Thickness(11, 7, 11, 7) };
        var heading = MakeText(resource.Kind + "   ·   " + resource.Host, 10.5, 16,
            new Thickness(0, 0, 0, 5));
        heading.FontWeight = FontWeights.SemiBold;
        heading.SetResourceReference(TextBlock.ForegroundProperty, "NIRASecondaryBrush");
        panel.Children.Add(heading);
        var link = MakeText($"[{resource.Label}  ↗]({resource.Address})", 14, 22,
            new Thickness(0));
        link.FontWeight = FontWeights.SemiBold;
        panel.Children.Add(link);
        var frame = new Border { Child = panel, CornerRadius = new CornerRadius(11),
            HorizontalAlignment = HorizontalAlignment.Left,
            MaxWidth = 600, BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 2, 0, 5) };
        frame.SetResourceReference(Border.BackgroundProperty, "NIRAGlassStrongTintBrush");
        frame.SetResourceReference(Border.BorderBrushProperty, "NIRAGlassBorderBrush");
        return frame;
    }

    // Only explicit Markdown table syntax is accepted: a heading row, a
    // --- separator row, then at least one complete data row.
    private static bool TryTableHeader(string header, string separator, out string[] columns)
    {
        columns = Array.Empty<string>();
        if (!TryCells(header, out string[] cells) || cells.Length < 2 || cells.Length > 8 ||
            !TryCells(separator, out string[] separators) || separators.Length != cells.Length ||
            separators.Any(cell => !TableSeparator.IsMatch(cell)) ||
            cells.Any(string.IsNullOrWhiteSpace)) return false;
        columns = cells;
        return true;
    }

    private static bool TryCells(string line, out string[] cells)
    {
        cells = Array.Empty<string>();
        string content = line.Trim();
        if (!content.Contains('|')) return false;
        // Split on unescaped bars. A displayed literal \| stays in its cell.
        var parts = Regex.Split(content, @"(?<!\\)\|").ToList();
        if (content.StartsWith('|')) parts.RemoveAt(0);
        if (content.EndsWith('|')) parts.RemoveAt(parts.Count - 1);
        if (parts.Count is < 2 or > 8) return false;
        cells = parts.Select(cell => cell.Trim().Replace(@"\|", "|", StringComparison.Ordinal))
            .ToArray();
        return true;
    }

    public static FrameworkElement MakeInlinePhoto(VisualArtifactViewModel photo, bool prominent)
    {
        // Plain image + quiet photo credit. No navy letterbox, no glass card,
        // outer border, badge or enclosing tinted panel.
        double width = prominent ? 345 : 325;
        var content = new StackPanel { MaxWidth = width };
        var picture = new Image
        {
            Source = photo.PreviewImage,
            Stretch = Stretch.Uniform,
            MaxWidth = width,
            MaxHeight = prominent ? 275 : 240,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
        var media = new Border
        {
            Child = picture,
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true,
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = "Open original image"
        };
        media.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            var viewer = new NIRAVisualArtifactViewerWindow(photo);
            var owner = Window.GetWindow(media);
            if (owner is { IsVisible: true }) viewer.Owner = owner;
            viewer.Show();
        };
        content.Children.Add(media);

        // Editorial title supplied by search engines is not necessarily a
        // useful photograph caption; prefer the concrete search subject.
        string label = string.IsNullOrWhiteSpace(photo.SubjectQuery)
            ? photo.Title.Trim() : photo.SubjectQuery.Trim();
        if (Regex.IsMatch(label, @"(?:__PID:|^[0-9a-f]{24,}(?:\.|$)|^\d+(?:st|nd|rd|th) image)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            label = "Reference photograph";
        if (label.Length > 76) label = label[..73] + "…";
        var title = MakeText(label, 13, 19, new Thickness(0, 6, 0, 1));
        title.FontWeight = FontWeights.Medium;
        content.Children.Add(title);
        if (photo.HasSourceUri)
        {
            // Keep provenance accessible without printing a source result
            // paragraph underneath every embedded figure.
            content.Children.Add(MakeText($"[Image credit]({photo.SourceUri})", 11, 16,
                new Thickness(0, 0, 0, 0)));
        }
        return new Border
        {
            Child = content,
            MaxWidth = width,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 15)
        };
    }

    private static TextBlock MakeText(string value, double size, double lineHeight, Thickness margin)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = size,
            LineHeight = lineHeight, Margin = margin, HorizontalAlignment = HorizontalAlignment.Stretch,
            TextTrimming = TextTrimming.None };
        text.SetResourceReference(TextBlock.ForegroundProperty, "NIRATextBrush");
        NIRAStyledChatText.SetText(text, value);
        return text;
    }
}
