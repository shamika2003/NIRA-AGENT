using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

using NIRAAgent.UI.Theming;

namespace NIRAAgent.UI;

/// <summary>
/// Paints the same themed background image that sits behind the main window,
/// cropped to this element's real screen position and blurred locally.
///
/// WPF has no native per-control backdrop blur. This keeps the UI fully WPF,
/// avoids fake opaque card fills, and produces a real glass/backdrop effect
/// without stealing the whole-window DWM surface.
/// </summary>
public sealed class NIRABackdropBorder : Border
{
    private static readonly BitmapImage EclipseImage = LoadBitmap(
        "Assets/nira-background-eclipse.png");

    private static readonly BitmapImage HaloImage = LoadBitmap(
        "Assets/nira-background-halo.png");

    private readonly ImageBrush _imageBrush = new()
    {
        Stretch = Stretch.Fill,
        TileMode = TileMode.None,
        ViewboxUnits = BrushMappingMode.Absolute,
        AlignmentX = AlignmentX.Left,
        AlignmentY = AlignmentY.Top
    };

    private Rect _lastBounds;
    private Size _lastRootSize;
    private NIRAThemeMode _lastMode = (NIRAThemeMode)(-1);
    private FrameworkElement? _root;

    public static readonly DependencyProperty BlurRadiusProperty =
        DependencyProperty.Register(
            nameof(BlurRadius),
            typeof(double),
            typeof(NIRABackdropBorder),
            new FrameworkPropertyMetadata(
                22.0,
                FrameworkPropertyMetadataOptions.AffectsRender,
                (_, _) => { }));

    public static readonly DependencyProperty BackdropOpacityProperty =
        DependencyProperty.Register(
            nameof(BackdropOpacity),
            typeof(double),
            typeof(NIRABackdropBorder),
            new FrameworkPropertyMetadata(
                0.94,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnBackdropOpacityChanged));

    public double BlurRadius
    {
        get => (double)GetValue(BlurRadiusProperty);
        set => SetValue(BlurRadiusProperty, value);
    }

    public double BackdropOpacity
    {
        get => (double)GetValue(BackdropOpacityProperty);
        set => SetValue(BackdropOpacityProperty, value);
    }

    public NIRABackdropBorder()
    {
        IsHitTestVisible = false;
        Background = _imageBrush;
        SnapsToDevicePixels = true;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        LayoutUpdated += OnLayoutUpdated;

        ApplyBlur();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _root = ResolveRoot();
        NIRAThemeManager.ThemeChanged += OnThemeChanged;
        UpdateBackdrop(force: true);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        NIRAThemeManager.ThemeChanged -= OnThemeChanged;
        _root = null;
    }

    private void OnThemeChanged(NIRAThemeMode mode) =>
        Dispatcher.BeginInvoke(new Action(() => UpdateBackdrop(force: true)));

    private void OnLayoutUpdated(object? sender, EventArgs e) =>
        UpdateBackdrop(force: false);

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == BlurRadiusProperty)
        {
            ApplyBlur();
        }
    }

    private void ApplyBlur()
    {
        Effect = new BlurEffect
        {
            Radius = Math.Max(0.0, BlurRadius),
            KernelType = KernelType.Gaussian,
            RenderingBias = RenderingBias.Performance
        };
    }

    private void UpdateBackdrop(bool force)
    {
        if (!IsLoaded || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        _root ??= ResolveRoot();

        if (_root == null || _root.ActualWidth <= 0 || _root.ActualHeight <= 0)
        {
            return;
        }

        Rect bounds;

        try
        {
            GeneralTransform transform = TransformToAncestor(_root);
            bounds = transform.TransformBounds(new Rect(0, 0, ActualWidth, ActualHeight));
        }
        catch (InvalidOperationException)
        {
            return;
        }

        Size rootSize = new(_root.ActualWidth, _root.ActualHeight);
        NIRAThemeMode mode = NIRAThemeManager.CurrentMode;

        if (!force &&
            NearlyEqual(bounds, _lastBounds) &&
            NearlyEqual(rootSize, _lastRootSize) &&
            mode == _lastMode)
        {
            return;
        }

        BitmapImage source = mode == NIRAThemeMode.Halo
            ? HaloImage
            : EclipseImage;

        if (source.PixelWidth <= 0 || source.PixelHeight <= 0)
        {
            return;
        }

        // Main background uses UniformToFill. Reproduce that exact geometry,
        // then crop the image to this card's position before applying blur.
        double scale = Math.Max(
            rootSize.Width / source.PixelWidth,
            rootSize.Height / source.PixelHeight);

        if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale))
        {
            return;
        }

        double displayedWidth = source.PixelWidth * scale;
        double displayedHeight = source.PixelHeight * scale;
        double offsetX = (rootSize.Width - displayedWidth) * 0.5;
        double offsetY = (rootSize.Height - displayedHeight) * 0.5;

        double sourceX = (bounds.X - offsetX) / scale;
        double sourceY = (bounds.Y - offsetY) / scale;
        double sourceWidth = bounds.Width / scale;
        double sourceHeight = bounds.Height / scale;

        Rect sourceBounds = new(0, 0, source.PixelWidth, source.PixelHeight);
        Rect viewbox = Rect.Intersect(
            sourceBounds,
            new Rect(sourceX, sourceY, sourceWidth, sourceHeight));

        if (viewbox.IsEmpty || viewbox.Width <= 0 || viewbox.Height <= 0)
        {
            return;
        }

        _imageBrush.ImageSource = source;
        _imageBrush.Viewbox = viewbox;
        _imageBrush.Opacity = Math.Clamp(BackdropOpacity, 0.0, 1.0);

        _lastBounds = bounds;
        _lastRootSize = rootSize;
        _lastMode = mode;
    }

    private FrameworkElement? ResolveRoot()
    {
        Window? window = Window.GetWindow(this);

        if (window?.FindName("RootShell") is FrameworkElement namedRoot)
        {
            return namedRoot;
        }

        DependencyObject? current = this;
        FrameworkElement? last = null;

        while (current != null)
        {
            if (current is FrameworkElement element)
            {
                last = element;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return last;
    }

    private static bool NearlyEqual(Rect a, Rect b) =>
        Math.Abs(a.X - b.X) < 0.35 &&
        Math.Abs(a.Y - b.Y) < 0.35 &&
        Math.Abs(a.Width - b.Width) < 0.35 &&
        Math.Abs(a.Height - b.Height) < 0.35;

    private static bool NearlyEqual(Size a, Size b) =>
        Math.Abs(a.Width - b.Width) < 0.35 &&
        Math.Abs(a.Height - b.Height) < 0.35;

    private static void OnBackdropOpacityChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is NIRABackdropBorder border)
        {
            border.UpdateBackdrop(force: true);
        }
    }

    private static BitmapImage LoadBitmap(string assetPath)
    {
        string assemblyName =
            typeof(NIRABackdropBorder).Assembly.GetName().Name
            ?? "NIRAAgent.UI";

        Uri uri = new(
            $"pack://application:,,,/{assemblyName};component/{assetPath}",
            UriKind.Absolute);

        BitmapImage bitmap = new();
        bitmap.BeginInit();
        bitmap.UriSource = uri;
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}

