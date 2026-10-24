using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
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
    // Fullscreen rehomes the live WebView2 surface. A chat-width SizeChanged
    // during this operation MUST NOT rebuild the deck and destroy the player.
    private static int _expandedPresentations;
    private static DateTime _rehostGraceUntilUtc = DateTime.MinValue;
    internal static bool IsPresentationExpanded => _expandedPresentations > 0 ||
        DateTime.UtcNow < _rehostGraceUntilUtc;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMonitorInfo
    {
        public int cbSize;
        public NativeRect rcMonitor;
        public NativeRect rcWork;
        public uint dwFlags;
    }
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref NativeMonitorInfo info);

    private static Rect ScreenBounds(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var monitor = MonitorFromWindow(hwnd, 2); // nearest monitor
        var info = new NativeMonitorInfo { cbSize = Marshal.SizeOf<NativeMonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
            return new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
        var source = PresentationSource.FromVisual(window);
        var matrix = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        Point tl = matrix.Transform(new Point(info.rcMonitor.Left, info.rcMonitor.Top));
        Point br = matrix.Transform(new Point(info.rcMonitor.Right, info.rcMonitor.Bottom));
        return new Rect(tl, br);
    }

    public static FrameworkElement Render(NIRARichBlock block, bool preloadYouTubePreview = false, IReadOnlyCollection<string>? relatedYouTubeIds = null)
    {
        if (!NIRAVideoReferencePolicy.TryParse(block.Text, out var video) || video is null)
            return NIRARichBlockRenderer.VideoLinkFallback(block);

        NIRADiagnosticTrace.Event("VIDEO-UI-VERSION", "InlineV3=True SeparateWindow=False");
        string heading = string.IsNullOrWhiteSpace(block.Title) ? "Video reference" : block.Title.Trim();
        // Research citations sometimes become titles such as "6] [Video title".
        // Strip only their leading reference marker, never the source/ID.
        heading = Regex.Replace(heading, @"^\s*(?:\^?\[?\d{1,3}\]\s*:?\s*)+", "")
            .Trim(' ', '[', ']');
        if (NIRAVideoTitleService.IsPlaceholder(heading))
            heading = NIRAVideoTitleService.DisplayTitle(heading, video);
        if (string.IsNullOrWhiteSpace(heading)) heading = "Watch video";
        bool expanded = false;
        var card = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 9, 0, 12),
            MaxWidth = 740,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            SnapsToDevicePixels = true
        };
        card.SetResourceReference(Border.BorderBrushProperty, "NIRAGlassBorderBrush");
        card.SetResourceReference(Border.BackgroundProperty, "NIRAGlassStrongTintBrush");

        var root = new StackPanel();
        card.Child = root;

        // Text hierarchy is intentionally quiet: playback is the focal point.
        var header = new StackPanel { Margin = new Thickness(3, 2, 3, 8) };
        var provider = Text($"NIRA  /  INLINE VIDEO  /  {video.Provider.ToUpperInvariant()}", 10.5,
            "NIRACyanBrush", FontWeights.SemiBold);
        header.Children.Add(provider);
        var title = Text(heading, 16, "NIRATextBrush", FontWeights.SemiBold);
        title.Margin = new Thickness(0, 4, 0, 0);
        NIRAVideoTitleService.Attach(title, block.Title, video);
        header.Children.Add(title);
        root.Children.Add(header);

        var stage = new Grid
        {
            Height = 338,
            ClipToBounds = true,
            Background = new SolidColorBrush(Night),
            SnapsToDevicePixels = true
        };
        var stageFrame = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(100, 80, 155, 230)),
            Child = stage,
            ClipToBounds = true
        };
        // Keep the native video in a responsive 16:9 viewport as the chat
        // column changes size, rather than a fixed 345px tall letterbox.
        stageFrame.SizeChanged += (_, args) =>
        {
            if (expanded || args.NewSize.Width < 1) return;
            double wanted = Math.Clamp((args.NewSize.Width - 2) * 9 / 16, 180, 390);
            if (Math.Abs(stage.Height - wanted) > 1) stage.Height = wanted;
        };
        root.Children.Add(stageFrame);

        // A real YouTube thumbnail is derived ONLY from the URL policy's
        // independently validated 11-character video ID. No unrelated image search,
        // invented still, provider scraping, or embedded network video starts here.
        var preview = new Grid
        {
            Background = new LinearGradientBrush(new GradientStopCollection
            {
                new(Night, 0), new(Blue, 0.50), new(Night, 1)
            }, new Point(0, 0), new Point(1, 1))
        };
        if (video.Provider == "YouTube")
        {
            var still = new Image
            {
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            still.ImageFailed += (_, _) =>
            {
                still.Visibility = Visibility.Collapsed;
                NIRADiagnosticTrace.Event("VIDEO-PREVIEW", "Provider=YouTube Result=ThumbnailUnavailable");
            };
            try
            {
                var stillUrl = $"https://i.ytimg.com/vi/{video.VideoId}/hqdefault.jpg";
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnDemand;
                bitmap.UriSource = new Uri(stillUrl, UriKind.Absolute);
                bitmap.EndInit();
                still.Source = bitmap;
                preview.Children.Add(still);
            }
            catch (Exception ex)
            {
                // Media preview is optional; never prevent the chat reply or
                // video playback itself because a thumbnail fails to initialize.
                NIRADiagnosticTrace.Event("VIDEO-PREVIEW",
                    "Provider=YouTube Result=Unavailable Reason=" + ex.GetType().Name);
            }
        }
        var wash = new Border
        {
            Background = new LinearGradientBrush(new GradientStopCollection
            {
                new(Color.FromArgb(72, 5, 18, 36), 0),
                new(Color.FromArgb(14, 9, 26, 49), 0.52),
                new(Color.FromArgb(84, 5, 18, 36), 1)
            }, new Point(0, 0), new Point(0, 1)),
            IsHitTestVisible = false
        };
        preview.Children.Add(wash);
        var center = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var play = ActionButton("▶  Play video", true);
        play.FontSize = 14;
        play.Padding = new Thickness(20, 10, 20, 10);
        // Familiar native-video visual cue. This button still invokes the
        // exact same vetted player-loading handler below.
        play.Background = new SolidColorBrush(Color.FromRgb(220, 35, 47));
        play.Foreground = Brushes.White;
        play.BorderBrush = new SolidColorBrush(Color.FromArgb(205, 255, 255, 255));
        play.HorizontalAlignment = HorizontalAlignment.Center;
        play.ToolTip = "Play " + video.Provider + " inside NIRA";
        center.Children.Add(play);
        center.Children.Add(new TextBlock
        {
            Text = video.Provider == "YouTube" ? "YouTube preview · watch in this chat" : "Watch in this chat",
            FontSize = 10.5,
            Foreground = new SolidColorBrush(Color.FromRgb(220, 233, 246)),
            Margin = new Thickness(0, 7, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        preview.Children.Add(center);
        stage.Children.Add(preview);
        // Reliable still fallback only. Player/iframe/WebView2 code stays intact.
        NIRAVideoThumbnailService.Attach(preview, video);

        // Fixed-size actions must never consume the status column and turn
        // "CONNECTING" into a vertical column of letters on narrow decks.
        // Keep exactly the two user-requested actions: Copy and Wide View.
        var footer = new Grid { Margin = new Thickness(0, 7, 0, 1), MinHeight = 31 };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var status = Text("READY", 9.5, "NIRASecondaryBrush", FontWeights.Medium);
        status.TextWrapping = TextWrapping.NoWrap;
        status.TextTrimming = TextTrimming.CharacterEllipsis;
        status.VerticalAlignment = VerticalAlignment.Center;
        status.Margin = new Thickness(1, 0, 7, 0);
        Grid.SetColumn(status, 0);
        footer.Children.Add(status);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(actions, 1);
        footer.Children.Add(actions);

        var copy = ActionButton("⧉  Copy");
        copy.Padding = new Thickness(8, 5, 8, 5);
        copy.MinHeight = 28;
        copy.Margin = new Thickness(0, 0, 6, 0);
        copy.ToolTip = "Copy the original video URL";
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(video.OriginalUrl); status.Text = "COPIED"; }
            catch (Exception ex) { Debug.WriteLine("[NIRA-VIDEO] Copy: " + ex.GetType().Name); }
        };
        actions.Children.Add(copy);
        root.Children.Add(footer);

        // Composition hosting follows WPF clipping/z-order, so videos no
        // longer paint over the header/composer when scrolling the chat.
        // Emergency compatibility fallback: NIRA_VIDEO_LEGACY_HOST=1 restores
        // the old HWND host without changing the verified iframe/origin logic.
        bool legacyHost = Environment.GetEnvironmentVariable("NIRA_VIDEO_LEGACY_HOST") == "1";
        // Wide View preserves the taskbar. Native full-monitor coverage is
        // exclusively initiated by YouTube's own fullscreen control.
        Grid? overlayShell = null;
        Grid? fullscreenOverlay = null;
        Grid? fullscreenSlot = null;
        Border? inlineSpacer = null;
        Window? ownerWindow = null;
        KeyEventHandler? keyHandler = null;
        int initialIndex = -1;
        bool nativeFullScreen = false;
        WindowState originalState = WindowState.Normal;
        WindowStyle originalStyle = WindowStyle.SingleBorderWindow;
        ResizeMode originalResizeMode = ResizeMode.CanResize;
        bool originalTopmost = false;
        Rect originalBounds = Rect.Empty;
        bool originalBoundsCaptured = false;
        var theater = ActionButton("⤢  Wide view");
        theater.ToolTip = "Expand the video while keeping the taskbar visible. Esc returns to chat.";
        theater.Padding = new Thickness(8, 5, 8, 5);
        theater.MinHeight = 28;
        actions.Children.Add(theater);

        void ApplyNativeWindow()
        {
            if (ownerWindow == null || nativeFullScreen) return;
            nativeFullScreen = true;
            Rect bounds = ScreenBounds(ownerWindow);
            // Explicit monitor bounds cover the taskbar; borderless Maximized
            // on Windows can otherwise reserve the taskbar work area.
            ownerWindow.WindowState = WindowState.Normal;
            ownerWindow.WindowStyle = WindowStyle.None;
            ownerWindow.ResizeMode = ResizeMode.NoResize;
            ownerWindow.Topmost = true;
            ownerWindow.Left = bounds.Left;
            ownerWindow.Top = bounds.Top;
            ownerWindow.Width = bounds.Width;
            ownerWindow.Height = bounds.Height;
        }

        void RestoreWindow()
        {
            if (ownerWindow == null || !originalBoundsCaptured) return;
            ownerWindow.WindowState = WindowState.Normal;
            ownerWindow.Topmost = originalTopmost;
            ownerWindow.WindowStyle = originalStyle;
            ownerWindow.ResizeMode = originalResizeMode;
            if (!originalBounds.IsEmpty)
            {
                ownerWindow.Left = originalBounds.Left;
                ownerWindow.Top = originalBounds.Top;
                ownerWindow.Width = originalBounds.Width;
                ownerWindow.Height = originalBounds.Height;
            }
            ownerWindow.WindowState = originalState;
            nativeFullScreen = false;
            originalBoundsCaptured = false;
        }

        void ExitFullscreen()
        {
            if (!expanded) return;
            expanded = false;
            // Keep size observers from rebuilding the deck while restoring
            // the WPF/WebView2 tree and original window dimensions.
            _rehostGraceUntilUtc = DateTime.UtcNow.AddMilliseconds(850);
            if (fullscreenSlot != null && fullscreenSlot.Children.Contains(stageFrame))
                fullscreenSlot.Children.Remove(stageFrame);
            if (overlayShell != null && fullscreenOverlay != null)
                overlayShell.Children.Remove(fullscreenOverlay);
            if (inlineSpacer != null) root.Children.Remove(inlineSpacer);
            if (!root.Children.Contains(stageFrame))
                root.Children.Insert(Math.Clamp(initialIndex, 0, root.Children.Count), stageFrame);
            if (ownerWindow != null && keyHandler != null)
                ownerWindow.PreviewKeyDown -= keyHandler;
            RestoreWindow();
            stageFrame.CornerRadius = new CornerRadius(10);
            stageFrame.BorderThickness = new Thickness(1);
            stage.Height = Math.Clamp((stageFrame.ActualWidth - 2) * 9 / 16, 180, 390);
            fullscreenSlot = null;
            fullscreenOverlay = null;
            overlayShell = null;
            inlineSpacer = null;
            keyHandler = null;
            theater.Content = "⤢  Wide view";
            _expandedPresentations = Math.Max(0, _expandedPresentations - 1);
            NIRADiagnosticTrace.Event("VIDEO-FULLSCREEN", "Mode=Inline Restored=True");
        }

        void EnterFullscreen(bool native)
        {
            if (expanded)
            {
                // Pressing YouTube's native fullscreen icon inside Theater upgrades
                // the same player to fullscreen without recreating WebView2.
                if (native && !nativeFullScreen) ApplyNativeWindow();
                return;
            }
            ownerWindow = Window.GetWindow(card);
            overlayShell = ownerWindow?.FindName("RootShell") as Grid;
            if (ownerWindow == null || overlayShell == null)
            {
                status.Text = "FULLSCREEN UNAVAILABLE  ·  STAY INLINE";
                return;
            }
            initialIndex = root.Children.IndexOf(stageFrame);
            if (initialIndex < 0) return;
            originalState = ownerWindow.WindowState;
            originalStyle = ownerWindow.WindowStyle;
            originalResizeMode = ownerWindow.ResizeMode;
            originalTopmost = ownerWindow.Topmost;
            Rect liveBounds = ownerWindow.WindowState == WindowState.Normal
                ? new Rect(ownerWindow.Left, ownerWindow.Top, ownerWindow.ActualWidth, ownerWindow.ActualHeight)
                : ownerWindow.RestoreBounds;
            originalBounds = liveBounds;
            originalBoundsCaptured = true;
            inlineSpacer = new Border { Height = stageFrame.ActualHeight, Background = Brushes.Transparent };
            fullscreenOverlay = new Grid
            {
                Background = Brushes.Black,
                Focusable = true,
                ClipToBounds = true
            };
            Grid.SetRow(fullscreenOverlay, 0);
            Grid.SetRowSpan(fullscreenOverlay, Math.Max(2, overlayShell.RowDefinitions.Count));
            Grid.SetColumnSpan(fullscreenOverlay, Math.Max(1, overlayShell.ColumnDefinitions.Count));
            Panel.SetZIndex(fullscreenOverlay, 100000);
            fullscreenSlot = new Grid { ClipToBounds = true, Margin = new Thickness(0), Background = Brushes.Black };
            fullscreenOverlay.Children.Add(fullscreenSlot);
            root.Children.RemoveAt(initialIndex);
            root.Children.Insert(initialIndex, inlineSpacer);
            // In viewer mode the actual image fills its viewport. No title bar,
            // arbitrary padding, button row, or decorative frame is rendered.
            stageFrame.CornerRadius = new CornerRadius(0);
            stageFrame.BorderThickness = new Thickness(0);
            fullscreenSlot.Children.Add(stageFrame);
            stage.Height = double.NaN;
            overlayShell.Children.Add(fullscreenOverlay);
            expanded = true;
            _expandedPresentations++;
            theater.Content = "↙  Back to chat";
            keyHandler = (_, e) =>
            {
                // Esc should be handled by YouTube first when its iframe owns
                // fullscreen. F11 is a NIRA emergency return key in either mode.
                if (e.Key == Key.F11 || (e.Key == Key.Escape && !nativeFullScreen))
                {
                    ExitFullscreen();
                    e.Handled = true;
                }
            };
            ownerWindow.PreviewKeyDown += keyHandler;
            if (native) ApplyNativeWindow();
            else ownerWindow.WindowState = WindowState.Maximized; // taskbar remains visible
            fullscreenOverlay.Focus();
            NIRADiagnosticTrace.Event("VIDEO-FULLSCREEN", "Mode=" + (native ? "NativeMonitor" : "Theater") + " Composition=True");
        }
        theater.Click += (_, _) =>
        {
            try { if (expanded) ExitFullscreen(); else EnterFullscreen(false); }
            catch (Exception ex)
            {
                NIRADiagnosticTrace.Event("VIDEO-FULLSCREEN", "Stage=TheaterFailed Kind=" + ex.GetType().Name);
            }
        };
        IWebView2? browser = null;
        int playbackVersion = 0;
        bool resetInProgress = false;

        // A browser initialization may still be awaiting WebView2 when chat is
        // refreshed, a view is removed, or Reset is clicked. Invalidate that
        // continuation BEFORE detaching its native composition surface.
        void Reset()
        {
            if (resetInProgress) return;
            resetInProgress = true;
            try
            {
                ++playbackVersion;
                var oldBrowser = browser;
                browser = null;
                if (expanded) ExitFullscreen();
                stage.Children.Clear();
                stage.Children.Add(preview);
                play.IsEnabled = true;
                status.Text = "READY  ·  INLINE";

                // Disposing WebView2 synchronously from WPF Unloaded / a
                // composition-tree mutation can re-enter native hosting code.
                // Detach now; dispose at an idle dispatcher boundary instead.
                if (oldBrowser is IDisposable disposable)
                {
                    card.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
                    {
                        try
                        {
                            disposable.Dispose();
                            NIRADiagnosticTrace.Event("VIDEO-PLAYER-DISPOSE", "Result=Completed");
                        }
                        catch (Exception ex)
                        {
                            NIRADiagnosticTrace.Event("VIDEO-PLAYER-DISPOSE",
                                "Result=Failed Error=" + ex.GetType().Name);
                        }
                    }));
                }
            }
            catch (Exception ex)
            {
                NIRADiagnosticTrace.Event("VIDEO-PLAYER-RESET",
                    "Result=Failed Error=" + ex);
                // Do not propagate exceptions from an Unloaded handler into
                // the WPF dispatcher and take down the entire conversation.
            }
            finally { resetInProgress = false; }
        }
        // WPF may raise Unloaded transiently while updating a visual tree.
        // Never destroy a WebView2 control synchronously inside that event.
        card.Unloaded += (_, _) =>
        {
            int seenVersion = playbackVersion;
            // Fullscreen is a transient visual-tree transition. Do not dispose
            // the embedded browser just because its WPF parent was rehosted.
            var grace = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            grace.Tick += (_, _) =>
            {
                grace.Stop();
                if (expanded || card.IsLoaded || browser == null ||
                    playbackVersion != seenVersion) return;
                NIRADiagnosticTrace.Event("VIDEO-PLAYER-LIFECYCLE",
                    "Stage=OwnerUnloaded Action=DeferredReset");
                Reset();
            };
            grace.Start();
        };

        // When the carousel requests a native poster, initialize ONLY the
        // centered YouTube iframe after the response layout has settled.
        // The iframe has no autoplay parameter: it shows the provider's own
        // cover/title/play overlay, which the user can click to play.
        // Do not initialize background preview cards or other providers.
        bool nativePosterQueued = false;
        if (preloadYouTubePreview && video.Provider == "YouTube")
        {
            card.Loaded += (_, _) =>
            {
                if (nativePosterQueued) return;
                nativePosterQueued = true;
                var delay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
                delay.Tick += (_, _) =>
                {
                    delay.Stop();
                    if (!card.IsLoaded || !play.IsEnabled ||
                        !stage.Children.Contains(preview)) return;
                    NIRADiagnosticTrace.Event("VIDEO-NATIVE-POSTER",
                        "Stage=LoadNativeYoutubeEmbed Autoplay=False");
                    play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, play));
                };
                delay.Start();
            };
        }

        play.Click += async (_, _) =>
        {
            if (!play.IsEnabled) return;
            play.IsEnabled = false;
            int thisPlayback = ++playbackVersion;
            status.Text = "LOADING VIDEO";
            NIRADiagnosticTrace.Event("VIDEO-PLAYER-LIFECYCLE",
                "Stage=Click Provider=" + video.Provider + " Surface=" +
                (legacyHost ? "Legacy" : "Composition"));
            try
            {
                NIRADiagnosticTrace.Event("VIDEO-PLAYER-LIFECYCLE", "Stage=CreateSurface");
                FrameworkElement playerSurface = legacyHost
                    ? new WebView2()
                    : new WebView2CompositionControl();
                playerSurface.HorizontalAlignment = HorizontalAlignment.Stretch;
                playerSurface.VerticalAlignment = VerticalAlignment.Stretch;
                var view = (IWebView2)playerSurface;
                browser = view;
                playerSurface.Opacity = 0;
                stage.Children.Clear();
                stage.Children.Add(preview);
                stage.Children.Add(playerSurface);
                NIRADiagnosticTrace.Event("VIDEO-PLAYER-LIFECYCLE", "Stage=EnsureCoreBegin");
                await view.EnsureCoreWebView2Async(null);
                if (thisPlayback != playbackVersion || browser != view || !card.IsLoaded) return;
                NIRADiagnosticTrace.Event("VIDEO-PLAYER-LIFECYCLE", "Stage=EnsureCoreComplete");
                if (view.CoreWebView2 == null) throw new InvalidOperationException("WebView2 core unavailable");
                view.CoreWebView2.ProcessFailed += (_, args) =>
                    NIRADiagnosticTrace.Event("VIDEO-PLAYER-PROCESS-FAILED",
                        "Kind=" + args.ProcessFailedKind);
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
                var playerPage = CreatePlayerPage(video, relatedYouTubeIds);
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
                    if (ev.IsSuccess)
                    {
                        card.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                        {
                            if (browser != view) return;
                            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180));
                            playerSurface.BeginAnimation(UIElement.OpacityProperty, fade);
                            status.Text = "READY  ·  " + video.Provider.ToUpperInvariant();
                        }));
                    }
                    else
                    {
                        status.Text = "EMBED UNAVAILABLE  ·  OPEN ORIGINAL";
                    }
                    NIRADiagnosticTrace.Event("VIDEO-PLAYER-NAVIGATION",
                        "Provider=" + video.Provider +
                        " LocalHttpsWrapper=True NavigationSucceeded=" + ev.IsSuccess);
                };

                if (video.Provider == "YouTube")
                {
                    // Official posters requested by the same browser engine that
                    // successfully loads the YouTube player. Successful image
                    // responses update ALL NIRA preview layers via shared cache.
                    NIRAVideoThumbnailService.ObserveBrowser(view.CoreWebView2);
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
                // Provider-driven fullscreen only. WebView2 tells the host
                // when YouTube's native fullscreen icon was pressed or exited.
                view.CoreWebView2.ContainsFullScreenElementChanged += (_, _) =>
                {
                    if (browser != view) return;
                    bool youtubeFullscreen = view.CoreWebView2?.ContainsFullScreenElement == true;
                    NIRADiagnosticTrace.Event("VIDEO-FULLSCREEN",
                        "Stage=ProviderNativeSignal Active=" + youtubeFullscreen);
                    card.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                    {
                        if (browser != view || thisPlayback != playbackVersion || !card.IsLoaded) return;
                        try
                        {
                            if (youtubeFullscreen) EnterFullscreen(true);
                            else if (!youtubeFullscreen && nativeFullScreen) ExitFullscreen();
                        }
                        catch (Exception ex)
                        {
                            NIRADiagnosticTrace.Event("VIDEO-FULLSCREEN",
                                "Stage=ProviderFullscreenFailed Kind=" + ex.GetType().Name);
                        }
                    }));
                };
                if (thisPlayback != playbackVersion || browser != view || !card.IsLoaded) return;
                NIRADiagnosticTrace.Event("VIDEO-PLAYER-START",
                    "Provider=" + video.Provider + " Mode=HttpsIframeWrapper Host=VirtualMapped" +
                    " Surface=" + (legacyHost ? "HwndLegacy" : "WpfComposition"));
                view.CoreWebView2.Navigate(playerPage.Url);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[NIRA-VIDEO] Player initialization: " + ex);
                NIRADiagnosticTrace.Event("VIDEO-PLAYER-FAILURE",
                    "Provider=" + video.Provider + " Error=" + ex);
                if (thisPlayback == playbackVersion)
                {
                    Reset();
                    status.Text = "EMBED UNAVAILABLE  ·  OPEN ORIGINAL";
                }
            }
        };
        return card;
    }

    // Files are small, local-only HTML documents and contain only a validated
    // provider URL. They do not proxy, download, or bypass streaming content.
    // This is intentionally an iframe page, not NavigateToString/about:blank,
    // so Chromium can derive an HTTP Referer during frame navigation.
    private sealed record PlayerPage(string Folder, string Url);

    private static PlayerPage CreatePlayerPage(NIRAVideoReference video, IReadOnlyCollection<string>? relatedYouTubeIds)
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
            ? $"https://www.youtube.com/embed/{video.VideoId}?rel=0&playsinline=1&fs=1&modestbranding=1"
            : video.EmbedUrl;
        string allowedFrameHost = video.Provider == "YouTube"
            ? "https://www.youtube.com" : "https://player.vimeo.com";
        string filename = (video.Provider == "YouTube" ? "yt-" : "vimeo-") +
            video.VideoId + ".html";
        string fullPath = System.IO.Path.Combine(folder, filename);
        string encodedFrameUrl = WebUtility.HtmlEncode(frameUrl);
        // Prefetch only known, validated YouTube posters: the WebView2 response
        // observer captures the artwork for each side preview. No autoplay.
        string posters = video.Provider != "YouTube" ? "" : string.Join("\n",
            (relatedYouTubeIds ?? Array.Empty<string>())
                .Where(id => id.Length == 11 && id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_'))
                .Distinct(StringComparer.Ordinal)
                .Take(12)
                .Select(id => $"<img loading=\"eager\" src=\"https://i.ytimg.com/vi/{id}/hqdefault.jpg\" alt=\"\">"));
        string html = $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <meta name="referrer" content="strict-origin-when-cross-origin">
              <meta http-equiv="Content-Security-Policy"
                    content="default-src 'none'; style-src 'unsafe-inline'; frame-src {{allowedFrameHost}}; img-src https://i.ytimg.com https://img.youtube.com; base-uri 'none'; form-action 'none'">
              <title>NIRA video</title>
              <style>
                html,body { margin:0; width:100%; height:100%; overflow:hidden; background:#08142a; }
                iframe { display:block; border:0; width:100%; height:100%; }
                .poster-preload { position:absolute; left:-99999px; top:0; width:1px; height:1px; overflow:hidden; pointer-events:none; }
              </style>
            </head>
            <body>
              <div class="poster-preload" aria-hidden="true">{{posters}}</div>
              <iframe src="{{encodedFrameUrl}}" title="Video player"
                      referrerpolicy="strict-origin-when-cross-origin"
                      allow="fullscreen; accelerometer; autoplay; encrypted-media; gyroscope; picture-in-picture; web-share"
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
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(10, 7, 10, 7),
            MinHeight = 32,
            Cursor = System.Windows.Input.Cursors.Hand,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(155, 85, 151, 231)),
            Background = emphasized ? new SolidColorBrush(Color.FromRgb(26, 115, 212))
                : new SolidColorBrush(Color.FromArgb(27, 70, 130, 200)),
            Foreground = emphasized ? Brushes.White : Brushes.Black
        };
        if (!emphasized)
            button.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty,
                "NIRATextBrush");
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