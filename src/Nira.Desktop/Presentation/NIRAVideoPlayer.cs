using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Nira.Core;
using NIRAAgent.Presentation;

namespace NIRAAgent.UI.Presentation;

// Original NIRA card, stage, controls and HTTPS iframe wrapper, adapted to the new host.
public static class NIRAVideoPlayer
{
    private const string PlayerHost = "nira-video.example";
    private const string PlayerOrigin = "https://" + PlayerHost + "/";
    private static readonly Color Night = Color.FromRgb(8, 20, 42);
    private static readonly Color Blue = Color.FromRgb(15, 59, 105);
    // Multiple visible native previews may load. A provider playing notification releases the previous playing surface.
    private static Action? _activeReset;
    // Weak keys avoid a static reference keeping abandoned conversation cards alive.
    private static readonly ConditionalWeakTable<FrameworkElement, Action> ReleaseActions = new();
    internal static void Release(FrameworkElement card)
    {
        if (ReleaseActions.TryGetValue(card, out var stop)) stop();
    }
    public static bool DisableNetworkForOfflineAcceptance { get; set; }
    private static int _fullscreenCount;
    private static DateTime _rehostGraceUntilUtc;
    internal static bool IsPresentationExpanded => _fullscreenCount > 0 || DateTime.UtcNow < _rehostGraceUntilUtc;
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

    public static FrameworkElement Render(NIRARichBlock block, bool compact = false, IReadOnlyCollection<string>? relatedYouTubeIds = null)
    {
        if (!NIRAVideoReferencePolicy.TryParse(block.Text, out var video) || video is null)
            return NIRARichBlockRenderer.VideoLinkFallback(block);
        string heading = string.IsNullOrWhiteSpace(block.Title) ? "Watch video" : block.Title.Trim();
        bool expanded = false;
        bool miniDocked = false;
        bool rehosting = false;
        Nira.Desktop.MainWindow? miniOwner = null;
        var card = new Border
        {
            Tag = "NIRA.VideoPlayer.AutoLoad",
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
        header.Children.Add(title);
        root.Children.Add(header);
        if (compact) header.Visibility = Visibility.Collapsed;

        var stage = new Grid
        {
            Height = compact ? 180 : 338,
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
            if (expanded || rehosting || args.NewSize.Width < 1) return;
            double wanted = Math.Clamp((args.NewSize.Width - 2) * 9 / 16,
                miniDocked ? 108 : compact ? 1 : 180, miniDocked ? 220 : 390);
            if (Math.Abs(stage.Height - wanted) > 1) stage.Height = wanted;
        };
        root.Children.Add(stageFrame);

        var preview = new Grid
        {
            Background = new LinearGradientBrush(new GradientStopCollection
            {
                new(Night, 0), new(Blue, 0.50), new(Night, 1)
            }, new Point(0, 0), new Point(1, 1))
        };
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
        NIRAVideoThumbnailService.Attach(preview, video);
        preview.Children.Add(wash);
        // Native provider UI is the only play control. The poster is merely a
        // noninteractive loading background; there is no extra NIRA play gate.
        stage.Children.Add(preview);
        var footer = new Grid { Margin = new Thickness(0, 7, 0, 1), MinHeight = 31 };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var status = Text("WAITING FOR PLAYER", 9.5, "NIRASecondaryBrush", FontWeights.Medium);
        status.TextWrapping = TextWrapping.NoWrap;
        status.TextTrimming = TextTrimming.CharacterEllipsis;
        status.VerticalAlignment = VerticalAlignment.Center;
        status.Margin = new Thickness(1, 0, 7, 0);
        Grid.SetColumn(status, 0);
        footer.Children.Add(status);
        var actions = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(actions, 1);
        if (compact)
        {
            footer.ColumnDefinitions.Clear();
            footer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            footer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(actions, 0); Grid.SetRow(actions, 1);
            status.Margin = new Thickness(0, 0, 0, 3);
            actions.HorizontalAlignment = HorizontalAlignment.Center;
        }
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
        // Embedding can be refused by the video provider (including YouTube
        // error 153), independently of WebView2's successful page navigation.
        // The original validated provider URL is always available as a reliable
        // user-initiated playback path; never imply an iframe actually played.
        var openOriginal = ActionButton("↗  Open video");
        openOriginal.Padding = new Thickness(8, 5, 8, 5);
        openOriginal.MinHeight = 28;
        openOriginal.Margin = new Thickness(0, 0, 6, 0);
        openOriginal.ToolTip = "Watch this validated video in your default browser";
        openOriginal.Click += (_, _) => OpenOriginal(video.OriginalUrl);
        actions.Children.Add(openOriginal);
        root.Children.Add(footer);

        // Original theater/mini presentation: move the existing composition surface, never create a second player.
        var wide = ActionButton("⤢ Wide view");
        wide.ToolTip = "Expand the current player. Escape returns to chat.";
        actions.Children.Add(wide);
        var miniButton = ActionButton("↙ Mini");
        miniButton.ToolTip = "Move the current player to the left sidebar";
        actions.Children.Add(miniButton);
        wide.Padding = miniButton.Padding = new Thickness(8, 5, 8, 5);
        wide.MinHeight = miniButton.MinHeight = 28;
        wide.Margin = miniButton.Margin = new Thickness(0, 0, 6, 0);
        wide.IsEnabled = miniButton.IsEnabled = false;
        Border? placeholder = null;
        Grid? overlay = null;
        Grid? shell = null;
        Window? owner = null;
        int inlineIndex = -1;
        bool nativeFullscreen = false;
        WindowState oldState = WindowState.Normal;
        ResizeMode oldResize = ResizeMode.CanResize;
        WindowStyle oldStyle = WindowStyle.None;
        bool oldTopmost = false;
        double oldMaxWidth = double.PositiveInfinity, oldMaxHeight = double.PositiveInfinity;
        Rect oldBounds = Rect.Empty;
        KeyEventHandler? escapeHandler = null;
        WebView2CompositionControl? browser = null;
        void DetachStage()
        {
            if (stageFrame.Parent is Panel panel) panel.Children.Remove(stageFrame);
        }
        void MakePlaceholder(string message)
        {
            inlineIndex = root.Children.IndexOf(stageFrame);
            if (inlineIndex < 0) return;
            placeholder = new Border { Height = Math.Max(80, stageFrame.ActualHeight), Background = new SolidColorBrush(Night),
                CornerRadius = new CornerRadius(10), Child = new TextBlock { Text = message, Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            root.Children.Remove(stageFrame);
            root.Children.Insert(inlineIndex, placeholder);
        }
        void RestoreInline()
        {
            DetachStage();
            if (placeholder is not null) root.Children.Remove(placeholder);
            placeholder = null;
            if (!root.Children.Contains(stageFrame)) root.Children.Insert(Math.Clamp(inlineIndex, 0, root.Children.Count), stageFrame);
            stage.Height = Math.Clamp((card.ActualWidth - 2 * card.Padding.Left - 2) * 9 / 16, compact ? 1 : 180, 390);
        }
        void ApplyNativeWindow()
        {
            if (owner is null || nativeFullscreen) return;
            Rect bounds = ScreenBounds(owner);
            nativeFullscreen = true;
            if (owner is Nira.Desktop.MainWindow main) main.SetVideoFullscreen(true);
            owner.WindowState = WindowState.Normal;
            owner.WindowStyle = WindowStyle.None; owner.ResizeMode = ResizeMode.NoResize; owner.Topmost = true;
            owner.MaxWidth = double.PositiveInfinity; owner.MaxHeight = double.PositiveInfinity;
            owner.Left = bounds.Left; owner.Top = bounds.Top; owner.Width = bounds.Width; owner.Height = bounds.Height;
        }
        void ExitFullscreen()
        {
            if (!expanded) return;
            rehosting = true;
            try
            {
                DetachStage();
                if (shell is not null && overlay is not null) shell.Children.Remove(overlay);
                if (owner is not null && escapeHandler is not null) owner.PreviewKeyDown -= escapeHandler;
                if (nativeFullscreen && owner is not null)
                {
                    owner.WindowState = WindowState.Normal; owner.Topmost = oldTopmost;
                    owner.WindowStyle = oldStyle; owner.ResizeMode = oldResize;
                    owner.MaxWidth = oldMaxWidth; owner.MaxHeight = oldMaxHeight;
                    if (owner is Nira.Desktop.MainWindow main) main.SetVideoFullscreen(false);
                    if (!oldBounds.IsEmpty) { owner.Left = oldBounds.Left; owner.Top = oldBounds.Top; owner.Width = oldBounds.Width; owner.Height = oldBounds.Height; }
                    owner.WindowState = oldState;
                }
                stageFrame.CornerRadius = new CornerRadius(10); stageFrame.BorderThickness = new Thickness(1);
                if (miniDocked && miniOwner is not null)
                { miniOwner.ResumeMiniPlayerSurface(stageFrame); stage.Height = 124; }
                else RestoreInline();
            }
            finally
            {
                expanded = nativeFullscreen = rehosting = false;
                _fullscreenCount = Math.Max(0, _fullscreenCount - 1);
                _rehostGraceUntilUtc = DateTime.UtcNow.AddMilliseconds(850);
                overlay = shell = null; owner = null; escapeHandler = null;
            }
        }
        void EnterFullscreen(bool native)
        {
            if (expanded) { if (native) ApplyNativeWindow(); return; }
            owner = miniDocked ? miniOwner : Window.GetWindow(card);
            shell = owner?.FindName("RootShell") as Grid;
            if (owner is null || shell is null || (!miniDocked && !root.Children.Contains(stageFrame))) return;
            oldState = owner.WindowState; oldResize = owner.ResizeMode; oldStyle = owner.WindowStyle;
            oldTopmost = owner.Topmost; oldMaxWidth = owner.MaxWidth; oldMaxHeight = owner.MaxHeight;
            oldBounds = oldState == WindowState.Normal ? new Rect(owner.Left, owner.Top, owner.ActualWidth, owner.ActualHeight) : owner.RestoreBounds;
            rehosting = expanded = true; _fullscreenCount++;
            try
            {
                if (miniDocked) miniOwner!.SuspendMiniPlayerSurface(stageFrame);
                else MakePlaceholder("Video is expanded");
                overlay = new Grid { Background = Brushes.Black, Focusable = true, ClipToBounds = true };
                Grid.SetRowSpan(overlay, Math.Max(1, shell.RowDefinitions.Count));
                Grid.SetColumnSpan(overlay, Math.Max(1, shell.ColumnDefinitions.Count)); Panel.SetZIndex(overlay, 100000);
                stageFrame.CornerRadius = new CornerRadius(0); stageFrame.BorderThickness = new Thickness(0); stage.Height = double.NaN;
                overlay.Children.Add(stageFrame);
                var back = ActionButton("↙ Back to chat"); back.HorizontalAlignment = HorizontalAlignment.Right;
                back.VerticalAlignment = VerticalAlignment.Top; back.Margin = new Thickness(12); Panel.SetZIndex(back, 10);
                back.Click += async (_, _) => await RequestExitFullscreenAsync(); overlay.Children.Add(back);
                shell.Children.Add(overlay);
                escapeHandler = async (_, e) =>
                {
                    if (e.Key != Key.F11 && (e.Key != Key.Escape || nativeFullscreen)) return;
                    e.Handled = true; await RequestExitFullscreenAsync();
                };
                owner.PreviewKeyDown += escapeHandler;
                if (native) ApplyNativeWindow();
                overlay.Focus();
            }
            catch (Exception error) { Debug.WriteLine(error); ExitFullscreen(); }
            finally { rehosting = false; }
        }
        async Task RequestExitFullscreenAsync()
        {
            // The provider owns native fullscreen. Ask it to exit before restoring the host.
            try
            {
                if (nativeFullscreen && browser?.CoreWebView2 is { } core)
                    await core.ExecuteScriptAsync("if(document.fullscreenElement) document.exitFullscreen().catch(()=>{});");
            }
            catch (Exception error) { Debug.WriteLine(error); }
            ExitFullscreen();
        }
        bool RestoreFromMini()
        {
            if (!miniDocked || miniOwner is null || !card.IsLoaded || placeholder is null || !root.Children.Contains(placeholder)) return false;
            if (expanded) ExitFullscreen();
            rehosting = true;
            try
            {
                miniOwner.DetachMiniPlayer(stageFrame); miniDocked = false; miniOwner = null;
                RestoreInline(); miniButton.IsEnabled = true; card.BringIntoView(); return true;
            }
            finally { rehosting = false; }
        }
        var stop = ActionButton("■  Close");
        stop.Visibility = Visibility.Collapsed;
        actions.Children.Add(stop);
        var reload = ActionButton("↻  Reload player");
        reload.Visibility = Visibility.Collapsed;
        actions.Children.Add(reload);
        int version = 0;
        bool suppressed = false;
        ScrollViewer? viewport = null;
        // Defer creation until layout settles; scrolling and deck transitions
        // should not spin up WebView2 for a card that has already disappeared.
        var loadTimer = new DispatcherTimer(DispatcherPriority.Background, card.Dispatcher)
        { Interval = TimeSpan.FromMilliseconds(150) };
        // Covers initialization and top-level wrapper navigation. A hung provider
        // must not keep the card in a permanent loading state.
        var startupTimer = new DispatcherTimer(DispatcherPriority.Background, card.Dispatcher)
        { Interval = TimeSpan.FromSeconds(20) };
        bool resetting = false;
        Action? reset = null;
        reset = () => { suppressed = true; Reset(); };
        ReleaseActions.Add(card, reset);
        void FailPlayer(string message)
        {
            suppressed = true;
            Reset();
            status.Text = message;
        }
        startupTimer.Tick += (_, _) =>
        {
            startupTimer.Stop();
            if (browser is not null) FailPlayer("PLAYER TIMED OUT · RELOAD OR OPEN VIDEO");
        };
        wide.Click += (_, _) => { if (browser?.CoreWebView2 is not null) EnterFullscreen(false); };
        miniButton.Click += (_, _) =>
        {
            if (browser?.CoreWebView2 is null || expanded || miniDocked || Window.GetWindow(card) is not Nira.Desktop.MainWindow main) return;
            rehosting = true;
            try
            {
                MakePlaceholder("Video in left mini-player · Chat to return");
                miniDocked = true; miniOwner = main;
                if (!main.TryAttachMiniPlayer(stageFrame, heading, RestoreFromMini, () => { suppressed = true; Reset(); }))
                {
                    miniDocked = false; miniOwner = null; RestoreInline(); status.Text = "OPEN LEFT SIDEBAR FIRST"; return;
                }
                stage.Height = 124; miniButton.IsEnabled = false;
            }
            finally { rehosting = false; }
        };
        void Reset()
        {
            if (resetting) return;
            resetting = true;
            try
            {
            loadTimer.Stop();
            startupTimer.Stop();
            version++;
            if (expanded) ExitFullscreen();
            if (miniDocked && miniOwner is not null)
            {
                rehosting = true;
                miniOwner.DetachMiniPlayer(stageFrame); miniDocked = false; miniOwner = null;
                RestoreInline(); rehosting = false;
            }
            wide.IsEnabled = miniButton.IsEnabled = false;
            if (_activeReset == reset) _activeReset = null;
            var old = browser; browser = null;
            if (old is not null) old.Visibility = Visibility.Collapsed;
            stage.Children.Clear(); stage.Children.Add(preview);
            stop.Visibility = Visibility.Collapsed;
            status.Text = suppressed ? "PLAYER CLOSED" : "WAITING FOR PLAYER";
            reload.Visibility = suppressed ? Visibility.Visible : Visibility.Collapsed;
            if (old is not null) card.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            { try { old.Dispose(); } catch (Exception error) { Debug.WriteLine(error); } }));
            }
            finally { resetting = false; }
        }
        bool InViewport()
        {
            if (!card.IsLoaded || !card.IsVisible || !stage.IsLoaded || !stage.IsVisible ||
                stage.ActualWidth < 1 || stage.ActualHeight < 1) return false;
            if (viewport is null) return true;
            try
            {
                var bounds = stage.TransformToAncestor(viewport).TransformBounds(new Rect(0, 0, stage.ActualWidth, stage.ActualHeight));
                return IsInViewport(bounds, new Rect(0, 0, viewport.ActualWidth, viewport.ActualHeight));
            }
            catch (InvalidOperationException) { return false; }
        }
        void RefreshViewport()
        {
            if (DisableNetworkForOfflineAcceptance || resetting || rehosting || expanded || miniDocked) return;
            if (!InViewport())
            {
                loadTimer.Stop();
                if (browser is not null) Reset();
                return;
            }
            if (!suppressed && browser is null && !loadTimer.IsEnabled) loadTimer.Start();
        }
        void OnScroll(object sender, ScrollChangedEventArgs args) => RefreshViewport();
        loadTimer.Tick += async (_, _) =>
        {
            loadTimer.Stop();
            if (InViewport() && !suppressed && browser is null) await OpenPlayerAsync();
        };
        stop.Click += (_, _) => { suppressed = true; Reset(); };
        reload.Click += (_, _) => { suppressed = false; reload.Visibility = Visibility.Collapsed; RefreshViewport(); };
        card.Loaded += (_, _) =>
        {
            if (viewport is not null) viewport.ScrollChanged -= OnScroll;
            viewport = null;
            for (DependencyObject? ancestor = VisualTreeHelper.GetParent(card); ancestor is not null; ancestor = VisualTreeHelper.GetParent(ancestor))
                if (ancestor is ScrollViewer scroll) { viewport = scroll; break; }
            if (viewport is not null) viewport.ScrollChanged += OnScroll;
            RefreshViewport();
        };
        card.IsVisibleChanged += (_, _) => RefreshViewport();
        stage.SizeChanged += (_, _) => RefreshViewport();
        card.Unloaded += (_, _) =>
        {
            if (viewport is not null) viewport.ScrollChanged -= OnScroll;
            viewport = null;
            loadTimer.Stop();
            // The stage can move, but the conversation card is never rehosted.
            // A removed card must not leave a docked or fullscreen browser behind.
            // Defer until WPF finishes the current visual-tree mutation.
            card.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (!card.IsLoaded && browser is not null) Reset();
            }));
        };
        async Task OpenPlayerAsync()
        {
            if (DisableNetworkForOfflineAcceptance || browser is not null || suppressed || !InViewport()) return;
            stop.Visibility = Visibility.Visible;
            status.Text = "LOADING PLAYER";
            startupTimer.Start();
            int current = ++version;
            try
            {
                var view = new WebView2CompositionControl(); browser = view;
                // The poster remains mounted above the initializing composition surface.
                stage.Children.Insert(0, view);
                preview.IsHitTestVisible = false;
                await ((IWebView2)view).EnsureCoreWebView2Async(null);
                if (browser != view || current != version) return;
                if (!card.IsLoaded || !InViewport()) { Reset(); return; }
                var core = view.CoreWebView2 ?? throw new InvalidOperationException("WebView2 unavailable");
                var ids = (relatedYouTubeIds ?? Array.Empty<string>()).Concat(video.Provider == "YouTube" ? new[] { video.VideoId } : Array.Empty<string>())
                    .Where(NIRAVideoThumbnailService.ValidId).Distinct(StringComparer.Ordinal).Take(6).ToArray();
                var page = CreatePlayerPage(video, ids);
                NIRAVideoThumbnailService.ObserveBrowser(core, ids);
                core.ContainsFullScreenElementChanged += (_, _) =>
                {
                    if (browser != view) return;
                    bool native = core.ContainsFullScreenElement;
                    card.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (browser != view || current != version) return;
                        if (native) EnterFullscreen(true);
                        else if (nativeFullscreen) ExitFullscreen();
                    }));
                };
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.AreHostObjectsAllowed = false;
                core.Settings.IsWebMessageEnabled = true;
                core.Settings.IsStatusBarEnabled = false;
                core.NewWindowRequested += (_, e) => e.Handled = true;
                core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
                core.DownloadStarting += (_, e) => e.Cancel = true;
                core.NavigationStarting += (_, e) => { if (!string.Equals(e.Uri, page.Url, StringComparison.Ordinal)) e.Cancel = true; };
                core.WebMessageReceived += (_, e) =>
                {
                    if (browser != view || !string.Equals(e.Source, page.Url, StringComparison.Ordinal)) return;
                    try
                    {
                        string message = e.TryGetWebMessageAsString();
                        if (message == "nira-video:provider-error") status.Text = "EMBED REFUSED · OPEN VIDEO";
                        else if (message == "nira-video:provider-playing")
                        {
                            // Do not evict another visible preview just for loading.
                            // A real provider playback notification can stop previous playback.
                            if (_activeReset != reset) _activeReset?.Invoke();
                            _activeReset = reset;
                            status.Text = "PLAYING";
                        }
                    }
                    catch (Exception error) { Debug.WriteLine(error); }
                };
                core.NavigationCompleted += (_, e) =>
                {
                    if (browser != view) return;
                    startupTimer.Stop();
                    if (!e.IsSuccess)
                    {
                        card.Dispatcher.BeginInvoke(new Action(() =>
                        { if (browser == view && current == version) FailPlayer("EMBED FAILED · RELOAD OR OPEN VIDEO"); }));
                        return;
                    }
                    stage.Children.Remove(preview);
                    if (status.Text == "LOADING PLAYER") status.Text = "PLAYER LOADED";
                    wide.IsEnabled = miniButton.IsEnabled = true;
                };
                core.ProcessFailed += (_, _) => card.Dispatcher.BeginInvoke(new Action(() =>
                { if (browser == view && current == version) FailPlayer("PLAYER FAILED · RELOAD OR OPEN VIDEO"); }));
                core.SetVirtualHostNameToFolderMapping(PlayerHost, page.Folder, CoreWebView2HostResourceAccessKind.Deny);
                core.Navigate(page.Url);
            }
            catch (Exception error)
            {
                Debug.WriteLine(error);
                if (current == version) FailPlayer("PLAYER UNAVAILABLE · RELOAD OR OPEN VIDEO");
            }
        }
        return card;
    }

    private static bool IsInViewport(Rect bounds, Rect viewport) =>
        bounds.Width > 0 && bounds.Height > 0 && viewport.Width > 0 && viewport.Height > 0 &&
        Math.Min(bounds.Right, viewport.Right) > Math.Max(bounds.Left, viewport.Left) &&
        Math.Min(bounds.Bottom, viewport.Bottom) > Math.Max(bounds.Top, viewport.Top);

    private sealed record PlayerPage(string Folder, string Url);

    private static PlayerPage CreatePlayerPage(NIRAVideoReference video, IReadOnlyCollection<string> relatedYouTubeIds)
    {
        string folder = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ELVARA", "NIRA-Clean", "video-player-pages");
        Directory.CreateDirectory(folder);

        // IDs are independently checked by NIRAVideoReferencePolicy: YouTube
        // [A-Za-z0-9_-]{11}, Vimeo [0-9]{5,12}. Recheck here for defense in depth.
        if (video.VideoId.Length is < 5 or > 12 ||
            video.VideoId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            throw new InvalidOperationException("Invalid video reference identifier.");

        // Official standard YouTube iframe URL is used for compatibility; the
        // existing video/source parser is unchanged. Vimeo keeps its URL.
        string frameUrl = video.Provider == "YouTube"
            ? $"https://www.youtube.com/embed/{video.VideoId}?autoplay=0&rel=0&playsinline=1&fs=1&enablejsapi=1&origin=https%3A%2F%2Fnira-video.example"
            : video.EmbedUrl + "&autoplay=0";
        string allowedFrameHost = video.Provider == "YouTube"
            ? "https://www.youtube.com" : "https://player.vimeo.com";
        string posters = NIRAVideoThumbnailService.PosterMarkup(relatedYouTubeIds);
        string group = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(posters)))[..16];
        string filename = (video.Provider == "YouTube" ? "yt-" : "vimeo-") +
            video.VideoId + "-" + group + ".html";
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
                    content="default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; frame-src {{allowedFrameHost}}; img-src https://i.ytimg.com https://img.youtube.com; base-uri 'none'; form-action 'none'">
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
                      allow="fullscreen; accelerometer; encrypted-media; gyroscope; picture-in-picture; web-share"
                      allowfullscreen></iframe>
              <script>
                // An embedded provider can fail after this local wrapper loads.
                // Only provider-origin events can update playback status. The
                // host executes NO commands based on these messages.
                const frame = document.querySelector('iframe');
                frame.addEventListener('load', () => {
                  if ('{{allowedFrameHost}}' === 'https://www.youtube.com')
                    frame.contentWindow?.postMessage(JSON.stringify({event:'listening',id:'nira-player'}), '{{allowedFrameHost}}');
                  else frame.contentWindow?.postMessage({method:'addEventListener',value:'play'}, '{{allowedFrameHost}}');
                });
                window.addEventListener('message', event => {
                  if (event.source !== frame.contentWindow) return;
                  if (event.origin !== '{{allowedFrameHost}}' &&
                      event.origin !== 'https://www.youtube-nocookie.com') return;
                  let payload = event.data;
                  if (typeof payload === 'string') {
                    try { payload = JSON.parse(payload); } catch { return; }
                  }
                  if (!payload || typeof payload !== 'object') return;
                  if (payload.event === 'onReady' && event.origin === 'https://www.youtube.com') {
                    for (const name of ['onStateChange','onError'])
                      frame.contentWindow?.postMessage(JSON.stringify({event:'command',func:'addEventListener',args:[name]}), event.origin);
                  }
                  if (payload.event === 'onError' || payload.event === 'error')
                    window.chrome?.webview?.postMessage('nira-video:provider-error');
                  else if ((payload.event === 'onStateChange' &&
                           (payload.info === 1 || payload.info === 'playing')) ||
                           (payload.event === 'infoDelivery' && payload.info?.playerState === 1))
                    window.chrome?.webview?.postMessage('nira-video:provider-playing');
                  else if (payload.event === 'play' || payload.event === 'playing')
                    window.chrome?.webview?.postMessage('nira-video:provider-playing');
                });
              </script>
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
