using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using Microsoft.Web.WebView2.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Web.WebView2.Wpf;
using NIRAAgent.Presentation;
using NIRAAgent.Diagnostics;

namespace NIRAAgent.UI.Presentation;

/// <summary>
/// NIRA-owned video frame, player controls and safe embed lifecycle. Only a
/// supported video provider's validated embed URL enters WebView2. All other
/// sources keep the existing clickable source-card behavior. No remote page
/// loads until the user explicitly clicks Load.
/// </summary>
public static class NIRAVideoPlayer
{
    // An actual HTTPS document origin is necessary for an embedded YouTube iframe.
    // A top-level navigation directly to /embed/{id} is NOT an embedding page;
    // giving that single request a custom Referer did not fix Error 153.
    // The reserved .example hostname is mapped only inside WebView2 (no DNS).
    private const string PlayerHost = "nira-video.example";
    private const string PlayerOrigin = "https://" + PlayerHost + "/";
    private static readonly Color Night = Color.FromRgb(8, 20, 42);
    private static readonly Color Blue = Color.FromRgb(15, 59, 105);
    private static readonly Color Cyan = Color.FromRgb(89, 202, 255);

    public static FrameworkElement Render(NIRARichBlock block)
    {
        if (!NIRAVideoReferencePolicy.TryParse(block.Text, out var video) || video is null)
            return NIRARichBlockRenderer.VideoLinkFallback(block);

        string heading = string.IsNullOrWhiteSpace(block.Title) ? "Video reference" : block.Title;
        var card = new Border
        {
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 10, 0, 13),
            MaxWidth = 770,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        card.SetResourceReference(Border.BorderBrushProperty, "NIRAGlassBorderBrush");
        card.SetResourceReference(Border.BackgroundProperty, "NIRAGlassStrongTintBrush");

        var root = new StackPanel();
        card.Child = root;
        var header = new DockPanel { Margin = new Thickness(5, 2, 5, 10) };
        var provider = Text($"NIRA  /  VIDEO  /  {video.Provider.ToUpperInvariant()}", 10,
            "NIRACyanBrush", FontWeights.SemiBold);
        DockPanel.SetDock(provider, Dock.Top);
        header.Children.Add(provider);
        var title = Text(heading, 15.5, "NIRATextBrush", FontWeights.SemiBold);
        title.Margin = new Thickness(0, 6, 0, 0);
        header.Children.Add(title);
        root.Children.Add(header);

        var stage = new Grid
        {
            Height = 345,
            ClipToBounds = true,
            Background = new SolidColorBrush(Night)
        };
        var stageFrame = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(110, 93, 163, 220)),
            Child = stage,
            ClipToBounds = true
        };
        root.Children.Add(stageFrame);

        var preview = new Grid
        {
            Background = new LinearGradientBrush(new GradientStopCollection
            {
                new(Night, 0), new(Blue, 0.56), new(Night, 1)
            }, new Point(0, 0), new Point(1, 1))
        };
        // Decorative, NIRA-themed video idle surface; NOT a fabricated thumbnail.
        preview.Children.Add(new Ellipse
        {
            Width = 270, Height = 270,
            Stroke = new SolidColorBrush(Color.FromArgb(52, Cyan.R, Cyan.G, Cyan.B)),
            StrokeThickness = 1,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        preview.Children.Add(new Ellipse
        {
            Width = 182, Height = 182,
            Stroke = new SolidColorBrush(Color.FromArgb(75, Cyan.R, Cyan.G, Cyan.B)),
            StrokeThickness = 1,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        var center = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var play = ActionButton("▶  Load video player", true);
        play.FontSize = 15;
        play.Padding = new Thickness(25, 13, 25, 13);
        play.HorizontalAlignment = HorizontalAlignment.Center;
        play.ToolTip = "Load this video from " + video.Provider + " (external network content)";
        center.Children.Add(play);
        var hint = new TextBlock
        {
            Text = "Loads on click · playback uses provider controls",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(183, 212, 239)),
            Margin = new Thickness(0, 13, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        center.Children.Add(hint);
        preview.Children.Add(center);
        stage.Children.Add(preview);

        var footer = new DockPanel { Margin = new Thickness(4, 12, 4, 1), LastChildFill = false };
        var status = Text("READY  ·  ON DEMAND", 10, "NIRASecondaryBrush", FontWeights.Medium);
        status.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(status, Dock.Left);
        footer.Children.Add(status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(actions, Dock.Right);
        footer.Children.Add(actions);

        var reset = ActionButton("Stop / Reset");
        reset.Visibility = Visibility.Collapsed;
        reset.Margin = new Thickness(0, 0, 7, 0);
        actions.Children.Add(reset);
        var copy = ActionButton("Copy link");
        copy.Margin = new Thickness(0, 0, 7, 0);
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(video.OriginalUrl); status.Text = "LINK COPIED"; }
            catch (Exception ex) { Debug.WriteLine("[NIRA-VIDEO] Copy: " + ex.GetType().Name); }
        };
        actions.Children.Add(copy);
        var original = ActionButton("Open original  ↗");
        original.ToolTip = "Open the original video page in your browser";
        original.Click += (_, _) => OpenOriginal(video.OriginalUrl);
        actions.Children.Add(original);
        root.Children.Add(footer);

        WebView2? browser = null;
        void Reset()
        {
            stage.Children.Clear();
            if (browser != null)
            {
                try { browser.Dispose(); }
                catch (Exception ex) { Debug.WriteLine("[NIRA-VIDEO] Dispose: " + ex.GetType().Name); }
                browser = null;
            }
            stage.Children.Add(preview);
            reset.Visibility = Visibility.Collapsed;
            play.IsEnabled = true;
            status.Text = "READY  ·  ON DEMAND";
        }
        reset.Click += (_, _) => Reset();
        // If a chat tab/window is unloaded, do not leave audio playing in an
        // invisible WebView2 instance.
        card.Unloaded += (_, _) => { if (browser != null) Reset(); };

        play.Click += async (_, _) =>
        {
            if (!play.IsEnabled) return;
            play.IsEnabled = false;
            status.Text = "CONNECTING TO " + video.Provider.ToUpperInvariant();
            try
            {
                var view = new WebView2
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch
                };
                browser = view;
                stage.Children.Clear();
                stage.Children.Add(view);
                reset.Visibility = Visibility.Visible;
                await view.EnsureCoreWebView2Async();
                if (browser != view || view.CoreWebView2 == null) return;
                view.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                view.CoreWebView2.Settings.AreDevToolsEnabled = false;
                view.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
                view.CoreWebView2.Settings.IsStatusBarEnabled = false;
                view.CoreWebView2.Settings.AreHostObjectsAllowed = false;
                view.CoreWebView2.Settings.IsZoomControlEnabled = false;
                // Allow the provider's own "Watch on YouTube" link to fall
                // back to the original browser, but never open arbitrary popups.
                view.CoreWebView2.NewWindowRequested += (_, ev) =>
                {
                    ev.Handled = true;
                    if (NIRAVideoReferencePolicy.TryParse(ev.Uri, out var destination) &&
                        destination?.Key == video.Key)
                        OpenOriginal(video.OriginalUrl);
                };
                // Host a tiny iframe page under a real (virtual) HTTPS origin.
                // WebView2 then automatically sends its page origin as the
                // iframe's Referer. Navigating directly to a bare YouTube embed
                // URL was the specific mistake behind the previous patch.
                var playerPage = CreatePlayerPage(video);
                view.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    PlayerHost, playerPage.Folder,
                    CoreWebView2HostResourceAccessKind.Deny);

                // The top-level navigation is permitted ONLY to our generated
                // local page. Provider embeds run in the nested iframe; external
                // navigations and pop-ups remain blocked as before.
                view.CoreWebView2.NavigationStarting += (_, ev) =>
                {
                    if (!string.Equals(ev.Uri, playerPage.Url,
                        StringComparison.OrdinalIgnoreCase))
                        ev.Cancel = true;
                };
                view.CoreWebView2.NavigationCompleted += (_, ev) =>
                {
                    if (browser != view) return;
                    // A successful *wrapper* navigation is NOT proof that the
                    // third-party player actually plays. Keep the fallback.
                    status.Text = ev.IsSuccess
                        ? "PLAYER OPENED  ·  IF BLOCKED, OPEN ORIGINAL"
                        : "PLAYER UNAVAILABLE  ·  OPEN ORIGINAL";
                    NIRADiagnosticTrace.Event("VIDEO-PLAYER-NAVIGATION",
                        "Provider=" + video.Provider +
                        " LocalHttpsWrapper=True NavigationSucceeded=" + ev.IsSuccess);
                };

                if (video.Provider == "YouTube")
                {
                    // Belt-and-braces for WebView2 builds that omit Referer on
                    // a nested frame navigation. Scope this to the EXACT video
                    // iframe document, never unrelated URLs or media requests.
                    string expectedFrame = $"https://www.youtube.com/embed/{video.VideoId}";
                    view.CoreWebView2.AddWebResourceRequestedFilter(
                        "https://www.youtube.com/embed/*",
                        CoreWebView2WebResourceContext.Document);
                    view.CoreWebView2.WebResourceRequested += (_, args) =>
                    {
                        if (browser != view ||
                            !Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out var target) ||
                            !string.Equals(target.GetLeftPart(UriPartial.Path), expectedFrame,
                                StringComparison.OrdinalIgnoreCase))
                            return;
                        bool hadReferer = args.Request.Headers.Contains("Referer");
                        if (!hadReferer)
                            args.Request.Headers.SetHeader("Referer", PlayerOrigin);
                        NIRADiagnosticTrace.Event("VIDEO-PLAYER-REQUEST",
                            "Provider=YouTube IframeDocument=True Referer=" +
                            (hadReferer ? "Present" : "Supplied") + " Origin=VirtualHttps");
                    };
                }
                NIRADiagnosticTrace.Event("VIDEO-PLAYER-START",
                    "Provider=" + video.Provider + " Mode=HttpsIframeWrapper Host=VirtualMapped");
                view.CoreWebView2.Navigate(playerPage.Url);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[NIRA-VIDEO] Player initialization: " + ex);
                NIRADiagnosticTrace.Event("VIDEO-PLAYER-FAILURE",
                    "Provider=" + video.Provider + " Exception=" + ex.GetType().Name);
                Reset();
                status.Text = "EMBED UNAVAILABLE  ·  OPEN ORIGINAL";
            }
        };
        return card;
    }

    // Files are small, local-only HTML documents and contain only a validated
    // provider URL. They do not proxy, download, or bypass streaming content.
    // This is intentionally an iframe page, not NavigateToString/about:blank,
    // so Chromium can derive an HTTP Referer during frame navigation.
    private sealed record PlayerPage(string Folder, string Url);

    private static PlayerPage CreatePlayerPage(NIRAVideoReference video)
    {
        string folder = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NIRAAgent", "video-player-pages");
        Directory.CreateDirectory(folder);

        // IDs are independently checked by NIRAVideoReferencePolicy: YouTube
        // [A-Za-z0-9_-]{11}, Vimeo [0-9]{5,12}. Recheck here for defense in depth.
        if (video.VideoId.Length is < 5 or > 12 ||
            video.VideoId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            throw new InvalidOperationException("Invalid video reference identifier.");

        // Official standard YouTube iframe URL is used for compatibility; the
        // existing video/source parser is unchanged. Vimeo keeps its URL.
        string frameUrl = video.Provider == "YouTube"
            ? $"https://www.youtube.com/embed/{video.VideoId}?rel=0&playsinline=1"
            : video.EmbedUrl;
        string allowedFrameHost = video.Provider == "YouTube"
            ? "https://www.youtube.com" : "https://player.vimeo.com";
        string filename = (video.Provider == "YouTube" ? "yt-" : "vimeo-") +
            video.VideoId + ".html";
        string fullPath = System.IO.Path.Combine(folder, filename);
        string encodedFrameUrl = WebUtility.HtmlEncode(frameUrl);
        string html = $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <meta name="referrer" content="strict-origin-when-cross-origin">
              <meta http-equiv="Content-Security-Policy"
                    content="default-src 'none'; style-src 'unsafe-inline'; frame-src {{allowedFrameHost}}; base-uri 'none'; form-action 'none'">
              <title>NIRA video</title>
              <style>
                html,body { margin:0; width:100%; height:100%; overflow:hidden; background:#08142a; }
                iframe { display:block; border:0; width:100%; height:100%; }
              </style>
            </head>
            <body>
              <iframe src="{{encodedFrameUrl}}" title="Video player"
                      referrerpolicy="strict-origin-when-cross-origin"
                      allow="accelerometer; autoplay; encrypted-media; gyroscope; picture-in-picture; web-share"
                      allowfullscreen></iframe>
            </body>
            </html>
            """;
        // Avoid writing over a file that another player window is reading.
        if (!File.Exists(fullPath) ||
            !string.Equals(File.ReadAllText(fullPath), html, StringComparison.Ordinal))
            File.WriteAllText(fullPath, html, new UTF8Encoding(false));
        return new PlayerPage(folder, PlayerOrigin + filename);
    }

    private static TextBlock Text(string content, double size, string brush, FontWeight weight)
    {
        var text = new TextBlock { Text = content, TextWrapping = TextWrapping.Wrap,
            FontSize = size, FontWeight = weight };
        text.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return text;
    }

    private static Button ActionButton(string label, bool emphasized = false)
    {
        var button = new Button
        {
            Content = label,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(11, 8, 11, 8),
            MinHeight = 32,
            Cursor = System.Windows.Input.Cursors.Hand,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(83, 159, 231)),
            Background = emphasized ? new SolidColorBrush(Color.FromRgb(31, 129, 222))
                : new SolidColorBrush(Color.FromArgb(35, 70, 130, 200)),
            Foreground = Brushes.White
        };
        button.Template = RoundedButtonTemplate();
        return button;
    }

    private static ControlTemplate RoundedButtonTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        border.SetBinding(Border.BackgroundProperty,
            new System.Windows.Data.Binding("Background") { RelativeSource =
                new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        border.SetBinding(Border.BorderBrushProperty,
            new System.Windows.Data.Binding("BorderBrush") { RelativeSource =
                new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetBinding(ContentPresenter.MarginProperty,
            new System.Windows.Data.Binding("Padding") { RelativeSource =
                new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        border.AppendChild(presenter);
        template.VisualTree = border;
        return template;
    }

    private static void OpenOriginal(string address)
    {
        try { Process.Start(new ProcessStartInfo(address) { UseShellExecute = true }); }
        catch (Exception ex) { Debug.WriteLine("[NIRA-VIDEO] Open original: " + ex.GetType().Name); }
    }
}