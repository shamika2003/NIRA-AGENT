using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Shapes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using NIRAAgent.Diagnostics;
using NIRAAgent.Presentation;

namespace NIRAAgent.UI.Presentation;

/// <summary>
/// NIRA main-chat video carousel. One real, untransformed NIRAVideoPlayer lives
/// at the fixed center; surrounding layers are inert stills with exposed,
/// individually clickable surfaces. Every layer keeps its video identity while
/// the cyclic order, size and opacity animate. No WebView2 surface is animated.
/// </summary>
public sealed class NIRAVideoDepthDeck
{
    private const double Exposed = 0.86; // 86% exposure; keep comfortable taps and titles.
    private const int MotionMs = 410;
    private static readonly Color Cyan = Color.FromRgb(77, 193, 253);
    private static readonly Brush TextBrush = new SolidColorBrush(Color.FromRgb(230, 242, 255));
    private static readonly Brush MutedBrush = new SolidColorBrush(Color.FromRgb(137, 163, 192));

    private readonly List<(NIRARichBlock Block, NIRAVideoReference Reference)> _items = new();
    private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Grid _host = new()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        MinWidth = 0,
        MaxWidth = 1040,
        Margin = new Thickness(0, 5, 0, 12),
        SnapsToDevicePixels = true
    };

    private readonly Canvas _stage = new()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Background = Brushes.Transparent,
        ClipToBounds = true,
        SnapsToDevicePixels = true
    };

    private sealed class Layer
    {
        public required int Index { get; init; }
        public required Button Hit { get; init; }
        public required Grid Media { get; init; }
        public required TextBlock Title { get; init; }
        public required TextBlock Number { get; init; }
    }

    private readonly record struct Pose(double X, double Y, double Width, double MediaHeight,
        double Opacity, int Z, int Offset);

    private readonly Dictionary<int, Layer> _layers = new();
    private StackPanel? _footer;
    private FrameworkElement? _player;
    private int _activeIndex;
    private int _epoch;
    private bool _switching;
    private bool _inRebuild;
    private bool _resizedDuringSwitch;
    private double _lastWidth;
    private double _centerWidth;
    private double _stageWidth;
    private double _imageHeight;
    private DispatcherTimer? _depthTimer;
    private DispatcherTimer? _finishTimer;
    private int? _queuedSelection;

    public FrameworkElement View => _host;
    public int Count => _items.Count;

    public NIRAVideoDepthDeck()
    {
        _host.SizeChanged += (_, e) =>
        {
            if (NIRAVideoPlayer.IsPresentationExpanded) return;
            if (Math.Abs(e.NewSize.Width - _lastWidth) < 3) return;
            if (_inRebuild) return;
            _lastWidth = e.NewSize.Width;
            if (_switching) { _resizedDuringSwitch = true; return; }
            Rebuild();
        };
        _host.Loaded += (_, _) =>
        {
            if (_items.Count > 1 && _player == null && !_inRebuild &&
                !NIRAVideoPlayer.IsPresentationExpanded)
                Rebuild();
        };
        _host.Unloaded += (_, _) =>
        {
            _depthTimer?.Stop();
            _depthTimer = null;
            // Do not keep animation clocks alive after chat navigation.
            _finishTimer?.Stop();
            _finishTimer = null;
            _queuedSelection = null;
            _epoch++;
            _switching = false;
        };
    }

    public bool TryAdd(NIRARichBlock block)
    {
        if (!string.Equals(block.Type, "video", StringComparison.OrdinalIgnoreCase) ||
            !NIRAVideoReferencePolicy.TryParse(block.Text, out var video) || video is null)
            return false;
        if (!_keys.Add(video.Key)) return false;
        _items.Add((block, video));
        NIRADiagnosticTrace.Event("VIDEO-DEPTH-DECK",
            $"Stage=Add Total={_items.Count} Mode={(_items.Count == 1 ? "Single" : "OverlapCarousel")}");
        Rebuild();
        return true;
    }

    private static int RelativeOffset(int index, int active, int count)
    {
        // Five: 1,2,[3],4,5 -> 2,3,[4],5,1; two: second stays right.
        int forward = (index - active + count) % count;
        return forward <= count / 2 ? forward : forward - count;
    }

    // True 16:9 preview surfaces with independent explicit dimensions.
    // Side cards span most of their available lane. The previous version sized
    // them down to 58-90px wide, producing the apparent 30px-high "bars".
    private Pose GetPose(int index, int active)
    {
        int offset = RelativeOffset(index, active, _items.Count);
        int depth = Math.Abs(offset);
        double availableSide = Math.Max(40, (_stageWidth - _centerWidth) / 2 - 6);
        bool twoPerSide = _items.Count >= 4;

        // Near and far sizes divide the SAME physical side lane. Do not scale
        // their widths by allowance again: that was the thin-strip regression.
        double near = twoPerSide
            ? Math.Clamp(availableSide * 0.60 / Exposed, 135, 235)
            : Math.Clamp(availableSide * 0.90 / Exposed, 155, 265);
        double far = twoPerSide
            ? Math.Clamp(availableSide * 0.39 / Exposed, 112, 180)
            : Math.Clamp(near * 0.74, 112, 180);

        // When the chat column is narrow, proportionally fit both cards in
        // the side lane rather than clip them with the Canvas viewport.
        if (twoPerSide)
        {
            double required = Exposed * (near + far);
            if (required > availableSide && availableSide > 0)
            {
                double factor = availableSide / required;
                near *= factor;
                far *= factor;
            }
        }
        else if (Exposed * near > availableSide)
        {
            near = availableSide / Exposed;
        }

        double width = depth switch { 0 => _centerWidth, 1 => near, _ => far };
        double image = Math.Max(54, width * 9 / 16);
        double centerX = (_stageWidth - _centerWidth) / 2;
        double leftNear = centerX - Exposed * near;
        double rightNear = centerX + _centerWidth - (1 - Exposed) * near;
        double leftFar = leftNear - Exposed * far;
        double rightFar = rightNear + near - (1 - Exposed) * far;
        double x = offset switch
        {
            0 => centerX,
            -1 => leftNear,
            1 => rightNear,
            < -1 => leftFar - (depth - 2) * Exposed * far,
            _ => rightFar + (depth - 2) * Exposed * far
        };
        double y = depth switch { 0 => 0, 1 => 28, _ => 43 };
        double opacity = depth switch { 0 => 1, 1 => 0.94, _ => 0.79 };
        int z = depth switch { 0 => 40, 1 => 25, _ => 15 };
        return new Pose(x, y, width, image, opacity, z, offset);
    }

    private void Rebuild()
    {
        if (_inRebuild) return;
        _inRebuild = true;
        try
        {
            _epoch++;
            _depthTimer?.Stop();
            _depthTimer = null;
            _finishTimer?.Stop();
            _finishTimer = null;
            _switching = false;
            _layers.Clear();
            _player = null;
            _footer = null;
            _host.Children.Clear();
            // _stage is a retained Canvas. Removing the previous vertical host
            // from _host does not release this Canvas from that StackPanel's
            // logical-child collection. Detach it before a new vertical panel
            // adopts it during the third and later TryAdd/Rebuild calls.
            if (_stage.Parent is Panel previousOwner)
                previousOwner.Children.Remove(_stage);
            if (_items.Count == 0) return;
            if (_items.Count == 1)
            {
                var single = NIRAVideoPlayer.Render(_items[0].Block, preloadYouTubePreview: true);
                single.HorizontalAlignment = HorizontalAlignment.Center;
                _host.Children.Add(single);
                return;
            }

            _stageWidth = _host.ActualWidth > 100 ? _host.ActualWidth : 850;
            _stageWidth = Math.Max(350, _stageWidth);
            _centerWidth = Math.Clamp(_stageWidth * (_items.Count <= 3 ? 0.48 : 0.38), 250, 418);
            _centerWidth = Math.Min(_centerWidth, Math.Max(260, _stageWidth - 40));
            _imageHeight = _centerWidth * 9 / 16;
            // Stretch to the real chat width; never force the chat bubble wider
            // because a prior fallback measurement temporarily used 850 px.
            _stage.Width = double.NaN;
            // The player has its own status/actions and a quiet title below it.
            // Reserve their height here, then keep navigation immediately below.
            // The real player includes its own footer and title. Keep 16:9 previews
            // entirely inside the stage with room for their 2-line captions.
            _stage.Height = Math.Max(_imageHeight, 180) + 85;
            _stage.Children.Clear();

            var vertical = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch };
            _host.Children.Add(vertical);
            vertical.Children.Add(_stage);
            _footer = BuildFooter();
            vertical.Children.Add(_footer);

            // Keep all videos addressable by the footer, but show at most two
            // thumbnail layers on either side of the active player.
            for (int i = 0; i < _items.Count; i++)
            {
                var pose = GetPose(i, _activeIndex);
                if (Math.Abs(pose.Offset) > 2) continue;
                var layer = CreateLayer(i, pose);
                _layers.Add(i, layer);
                _stage.Children.Add(layer.Hit);
            }
            foreach (var layer in _layers.Values)
                ApplyPose(layer, GetPose(layer.Index, _activeIndex));
            MountPlayer();
            RefreshFooter();
        }
        finally { _inRebuild = false; }
    }

    private Layer CreateLayer(int index, Pose pose)
    {
        var item = _items[index];
        var media = new Grid
        {
            Background = new LinearGradientBrush(
                Color.FromRgb(14, 38, 64), Color.FromRgb(7, 17, 35), 25),
            ClipToBounds = true,
            Width = pose.Width,
            Height = pose.MediaHeight,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsHitTestVisible = false
        };
        // Clip the bitmap itself, not just the outer hit box. Thumbnail
        // services insert their Image into this Grid after construction.
        // RectangleGeometry tracks the media size throughout carousel motion.
        var roundedMediaClip = new RectangleGeometry { RadiusX = 11, RadiusY = 11 };
        media.Clip = roundedMediaClip;
        media.SizeChanged += (_, _) => roundedMediaClip.Rect =
            new Rect(0, 0, Math.Max(0, media.ActualWidth), Math.Max(0, media.ActualHeight));
        media.Children.Add(new TextBlock
        {
            Text = "▶", FontSize = 28, IsHitTestVisible = false,
            Foreground = new SolidColorBrush(Color.FromArgb(105, 117, 207, 252)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        NIRAVideoThumbnailService.Attach(media, item.Reference);
        media.Children.Add(new Border
        {
            IsHitTestVisible = false,
            Background = new LinearGradientBrush(new GradientStopCollection
            {
                new(Color.FromArgb(0, 0, 0, 0), 0),
                new(Color.FromArgb(32, 4, 15, 31), 1)
            }, new Point(0, 0), new Point(0, 1))
        });

        string title = NIRAVideoTitleService.DisplayTitle(item.Block.Title, item.Reference);
        var name = new TextBlock
        {
            Text = title,
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 36,
            TextAlignment = TextAlignment.Center,
            IsHitTestVisible = false
        };
        name.SetResourceReference(TextBlock.ForegroundProperty, "NIRATextBrush");
        NIRAVideoTitleService.Attach(name, item.Block.Title, item.Reference);
        var ordinal = new TextBlock
        {
            Text = $"{index + 1:00}",
            Foreground = new SolidColorBrush(Color.FromRgb(24, 117, 175)),
            FontSize = 10,
            TextAlignment = TextAlignment.Center,
            IsHitTestVisible = false,
            Margin = new Thickness(0, 3, 0, 0)
        };
        var content = new StackPanel
        {
            Background = Brushes.Transparent,
            IsHitTestVisible = false
        };
        content.Children.Add(media);
        name.Margin = new Thickness(2, 6, 2, 0);
        content.Children.Add(name);
        content.Children.Add(ordinal);
        var button = new Button
        {
            Content = content,
            Width = pose.Width,
            Height = pose.MediaHeight + 62, // stable clickable thumbnail + caption
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            FocusVisualStyle = null,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top,
            ToolTip = $"Select video {index + 1}: {title}",
            RenderTransformOrigin = new Point(0.5, 0.35),
            RenderTransform = new ScaleTransform(1, 1)
        };
        // A transparent full-size hit rectangle, not only a small sliver or
        // image label. Exposed portions retain their own hit target/z-order.
        var template = new ControlTemplate(typeof(Button));
        var hit = new FrameworkElementFactory(typeof(Border));
        hit.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Stretch);
        cp.SetValue(ContentPresenter.RecognizesAccessKeyProperty, false);
        hit.AppendChild(cp);
        template.VisualTree = hit;
        button.Template = template;
        button.Click += (_, _) => Select(index);
        // Exposed thumbnails remain navigable even while an earlier transition
        // is completing. Select() queues the latest requested destination.
        button.PreviewMouseLeftButtonDown += (_, _) =>
        {
            if (_switching) _queuedSelection = index;
        };
        button.MouseEnter += (_, _) =>
        {
            if (_switching || index == _activeIndex) return;
            name.SetResourceReference(TextBlock.ForegroundProperty, "NIRACyanBrush");
            Panel.SetZIndex(button, Math.Max(90, pose.Z + 20));
            if (button.RenderTransform is ScaleTransform scale)
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale.ScaleX, 1.065,
                    TimeSpan.FromMilliseconds(170)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale.ScaleY, 1.065,
                    TimeSpan.FromMilliseconds(170)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
            }
            button.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(button.Opacity, Math.Min(1, pose.Opacity + 0.10),
                TimeSpan.FromMilliseconds(170)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
        };
        button.MouseLeave += (_, _) =>
        {
            name.SetResourceReference(TextBlock.ForegroundProperty, "NIRATextBrush");
            if (button.RenderTransform is ScaleTransform scale)
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale.ScaleX, 1,
                    TimeSpan.FromMilliseconds(180)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale.ScaleY, 1,
                    TimeSpan.FromMilliseconds(180)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
            }
            button.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(button.Opacity, pose.Opacity,
                TimeSpan.FromMilliseconds(180)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
            if (!_switching) Panel.SetZIndex(button, GetPose(index, _activeIndex).Z);
        };
        Canvas.SetLeft(button, pose.X);
        Canvas.SetTop(button, pose.Y);
        Panel.SetZIndex(button, pose.Z);
        return new Layer { Index = index, Hit = button, Media = media, Title = name, Number = ordinal };
    }

    private void ApplyPose(Layer layer, Pose pose)
    {
        layer.Hit.BeginAnimation(FrameworkElement.WidthProperty, null);
        layer.Hit.BeginAnimation(FrameworkElement.HeightProperty, null);
        layer.Media.BeginAnimation(FrameworkElement.WidthProperty, null);
        layer.Hit.BeginAnimation(UIElement.OpacityProperty, null);
        layer.Hit.BeginAnimation(Canvas.LeftProperty, null);
        layer.Hit.BeginAnimation(Canvas.TopProperty, null);
        layer.Media.BeginAnimation(FrameworkElement.HeightProperty, null);
        if (layer.Hit.RenderTransform is ScaleTransform scale)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = 1;
            scale.ScaleY = 1;
        }
        layer.Hit.Width = pose.Width;
        layer.Hit.Height = pose.MediaHeight + 62;
        layer.Media.Width = pose.Width;
        layer.Media.Height = pose.MediaHeight;
        layer.Hit.Opacity = pose.Opacity;
        Canvas.SetLeft(layer.Hit, pose.X);
        Canvas.SetTop(layer.Hit, pose.Y);
        Panel.SetZIndex(layer.Hit, pose.Z);
        layer.Hit.Visibility = layer.Index == _activeIndex ? Visibility.Hidden : Visibility.Visible;
        layer.Hit.IsEnabled = layer.Index != _activeIndex && !_switching;
        layer.Title.FontSize = Math.Clamp(pose.Width * 0.078, 10.5, 12.2);
        layer.Number.Visibility = pose.Width < 80 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RemovePlayer()
    {
        if (_player != null)
            _stage.Children.Remove(_player);
        _player = null;
    }

    private void MountPlayer()
    {
        if (_layers.TryGetValue(_activeIndex, out var layer))
            layer.Hit.Visibility = Visibility.Hidden;
        var sourceBlock = _items[_activeIndex].Block;
        var playerBlock = new NIRARichBlock
        {
            Type = "video",
            Title = NIRAVideoTitleService.DisplayTitle(sourceBlock.Title, _items[_activeIndex].Reference),
            Text = sourceBlock.Text
        };
        var active = NIRAVideoPlayer.Render(playerBlock,
            preloadYouTubePreview: true,
            relatedYouTubeIds: _items.Where(entry => entry.Reference.Provider == "YouTube")
                .Select(entry => entry.Reference.VideoId).ToArray());
        // Keep player logic, controls, and fullscreen ownership unchanged.
        // Only suppress the extra glass CARD and redundant header for a clean
        // inline media surface within the overlapping carousel.
        if (active is Border shell)
        {
            shell.Padding = new Thickness(0);
            shell.Margin = new Thickness(0);
            shell.BorderThickness = new Thickness(0);
            shell.BorderBrush = Brushes.Transparent;
            shell.Background = Brushes.Transparent;
            shell.CornerRadius = new CornerRadius(0);
            shell.Effect = null;
            if (shell.Child is StackPanel children && children.Children.Count > 0 &&
                children.Children[0] is UIElement heading)
                heading.Visibility = Visibility.Collapsed;
        }
        active.Width = _centerWidth;
        active.HorizontalAlignment = HorizontalAlignment.Stretch;
        var root = new StackPanel { Width = _centerWidth, Background = Brushes.Transparent };
        root.Children.Add(active);
        var centerCaption = new TextBlock
        {
            Text = NIRAVideoTitleService.DisplayTitle(_items[_activeIndex].Block.Title, _items[_activeIndex].Reference),
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 6, 0, 0),
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        centerCaption.SetResourceReference(TextBlock.ForegroundProperty, "NIRATextBrush");
        NIRAVideoTitleService.Attach(centerCaption, _items[_activeIndex].Block.Title, _items[_activeIndex].Reference);
        root.Children.Add(centerCaption);
        Canvas.SetLeft(root, (_stageWidth - _centerWidth) / 2);
        Canvas.SetTop(root, 0);
        Panel.SetZIndex(root, 50);
        _player = root;
        _stage.Children.Add(root);
    }

    private void Select(int index)
    {
        if (index < 0 || index >= _items.Count) return;
        if (_switching) { _queuedSelection = index; return; }
        if (index == _activeIndex) return;
        // Lists longer than five still use the same centered presentation,
        // with direct access to any item via the footer. A distant selection
        // must rebuild first so its poster exists in the visible five-card set.
        if (_items.Count > 5)
        {
            _activeIndex = index;
            Rebuild();
            return;
        }
        int original = _activeIndex;
        int epoch = ++_epoch;
        _switching = true;
        _finishTimer?.Stop();
        _finishTimer = null;
        _depthTimer?.Stop();
        _depthTimer = null;
        RemovePlayer(); // WebView2 never receives a Scale/Translate animation.
        foreach (var layer in _layers.Values)
        {
            layer.Hit.Visibility = Visibility.Visible;
            layer.Hit.IsEnabled = true;
        }
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        foreach (var layer in _layers.Values)
        {
            var pose = GetPose(layer.Index, index);
            double fromX = Canvas.GetLeft(layer.Hit);
            double fromY = Canvas.GetTop(layer.Hit);
            double fromWidth = layer.Hit.Width;
            double fromHeight = layer.Hit.Height;
            double fromMedia = layer.Media.Height;
            double fromOpacity = layer.Hit.Opacity;
            // Set the BASE value to the destination before animation, so
            // removing the animation clocks at completion never jumps back.
            Canvas.SetLeft(layer.Hit, pose.X);
            Canvas.SetTop(layer.Hit, pose.Y);
            layer.Hit.Width = pose.Width;
            layer.Hit.Height = pose.MediaHeight + 62;
            layer.Media.Width = pose.Width;
            layer.Media.Height = pose.MediaHeight;
            layer.Hit.Opacity = pose.Opacity;
            Animate(layer.Hit, Canvas.LeftProperty, fromX, pose.X, ease);
            Animate(layer.Hit, Canvas.TopProperty, fromY, pose.Y, ease);
            Animate(layer.Hit, FrameworkElement.WidthProperty, fromWidth, pose.Width, ease);
            Animate(layer.Hit, FrameworkElement.HeightProperty, fromHeight, pose.MediaHeight + 62, ease);
            Animate(layer.Media, FrameworkElement.WidthProperty, fromWidth, pose.Width, ease);
            Animate(layer.Media, FrameworkElement.HeightProperty, fromMedia, pose.MediaHeight, ease);
            Animate(layer.Hit, UIElement.OpacityProperty, fromOpacity, pose.Opacity, ease);
        }

        _depthTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(MotionMs / 2) };
        _depthTimer.Tick += (_, _) =>
        {
            _depthTimer?.Stop();
            _depthTimer = null;
            if (epoch != _epoch) return;
            foreach (var layer in _layers.Values)
                Panel.SetZIndex(layer.Hit, GetPose(layer.Index, index).Z);
        };
        _depthTimer.Start();

        // Timed completion is independent of the Canvas animation clock; a
        // visual-tree rebuild or WebView2 unload must never leave clicks locked.
        _finishTimer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(MotionMs + 20)
        };
        _finishTimer.Tick += (_, _) =>
        {
            _finishTimer?.Stop();
            _finishTimer = null;
            if (epoch != _epoch) return;
            _activeIndex = index;
            // Unlock BEFORE applying poses; ApplyPose sets hit IsEnabled using
            // _switching. Previously it disabled every rear card permanently.
            _switching = false;
            foreach (var layer in _layers.Values)
                ApplyPose(layer, GetPose(layer.Index, _activeIndex));
            MountPlayer();
            RefreshFooter();
            NIRADiagnosticTrace.Event("VIDEO-DEPTH-DECK",
                $"Stage=Switch Old={original + 1} Active={index + 1} Total={_items.Count}");
            if (_resizedDuringSwitch)
            {
                _resizedDuringSwitch = false;
                Rebuild();
            }
            int? queued = _queuedSelection;
            _queuedSelection = null;
            if (queued.HasValue && queued.Value != _activeIndex)
                Select(queued.Value);
        };
        _finishTimer.Start();
    }

    private static void Animate(UIElement target, DependencyProperty property, double from,
        double to, IEasingFunction ease)
    {
        target.BeginAnimation(property, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(MotionMs))
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.HoldEnd
        });
    }

    private StackPanel BuildFooter()
    {
        return new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Orientation = Orientation.Vertical,
            Margin = new Thickness(0, -2, 0, 0)
        };
    }

    private void RefreshFooter()
    {
        if (_footer == null || _items.Count < 2) return;
        _footer.Children.Clear();
        var nav = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 0)
        };
        _footer.Children.Add(nav);
        nav.Children.Add(NavArrow("‹", "Previous video", () =>
            Select((_activeIndex - 1 + _items.Count) % _items.Count)));
        if (_items.Count <= 12)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                int index = i;
                nav.Children.Add(DotIndicator(i == _activeIndex,
                    "Select video " + (i + 1) + ": " + NIRAVideoTitleService.DisplayTitle(_items[i].Block.Title, _items[i].Reference),
                    () => Select(index)));
            }
        }
        nav.Children.Add(NavArrow("›", "Next video", () =>
            Select((_activeIndex + 1) % _items.Count)));
        var count = new TextBlock
        {
            Text = $"{_activeIndex + 1:00} / {_items.Count:00}",
            FontWeight = FontWeights.Medium,
            FontSize = 9,
            Margin = new Thickness(0, 0, 0, 0),
            Opacity = 0.65,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        count.SetResourceReference(TextBlock.ForegroundProperty, "NIRATextBrush");
        _footer.Children.Add(count);
    }

    private static ControlTemplate TransparentTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var root = new FrameworkElementFactory(typeof(Border));
        root.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        root.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        template.VisualTree = root;
        return template;
    }

    private static Button DotIndicator(bool active, string description, Action selected)
    {
        var ellipse = new Border
        {
            Width = active ? 16 : 7,
            Height = active ? 6 : 6,
            CornerRadius = new CornerRadius(4),
            Background = active
                ? new SolidColorBrush(Color.FromRgb(10, 122, 215))
                : new SolidColorBrush(Color.FromRgb(92, 117, 148)),
            BorderThickness = active ? new Thickness(1) : new Thickness(0),
            BorderBrush = new SolidColorBrush(Color.FromRgb(85, 204, 255)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        var button = new Button
        {
            Content = ellipse,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Width = active ? 22 : 16,
            Height = 18,
            Padding = new Thickness(0),
            Cursor = Cursors.Hand,
            ToolTip = description,
            FocusVisualStyle = null,
            Template = TransparentTemplate()
        };
        button.MouseEnter += (_, _) =>
        {
            if (!active) ellipse.Background = new SolidColorBrush(Color.FromRgb(14, 147, 234));
        };
        button.MouseLeave += (_, _) =>
        {
            if (!active) ellipse.Background = new SolidColorBrush(Color.FromRgb(92, 117, 148));
        };
        button.Click += (_, _) => selected();
        return button;
    }

    private static Button NavArrow(string symbol, string description, Action selected)
    {
        var button = new Button
        {
            Content = symbol,
            ToolTip = description,
            Foreground = new SolidColorBrush(Color.FromRgb(22, 118, 198)),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Width = 24,
            Height = 18,
            Padding = new Thickness(0),
            FocusVisualStyle = null,
            Template = TransparentTemplate()
        };
        button.Click += (_, _) => selected();
        return button;
    }
}