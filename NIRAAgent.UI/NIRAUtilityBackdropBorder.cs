using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

using NIRAAgent.UI.Theming;

namespace NIRAAgent.UI;

/// <summary>
/// Local frosted-glass backdrop for Settings and Permissions.
/// It mirrors the current utility background image underneath the control,
/// crops to the control's real position, and applies a small Gaussian blur.
/// </summary>
public sealed class NIRAUtilityBackdropBorder : Border
{
    private static readonly BitmapImage EclipseImage = LoadBitmap("Assets/nira-utility-eclipse.png");
    private static readonly BitmapImage HaloImage = LoadBitmap("Assets/nira-utility-halo.png");

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
            typeof(NIRAUtilityBackdropBorder),
            new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BackdropOpacityProperty =
        DependencyProperty.Register(
            nameof(BackdropOpacity),
            typeof(double),
            typeof(NIRAUtilityBackdropBorder),
            new FrameworkPropertyMetadata(0.72, FrameworkPropertyMetadataOptions.AffectsRender, OnBackdropOpacityChanged));

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

    public NIRAUtilityBackdropBorder()
    {
        IsHitTestVisible = false;
        Background = _imageBrush;
        SnapsToDevicePixels = true;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        LayoutUpdated += OnLayoutUpdated;

        ApplyBlur();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == BlurRadiusProperty)
        {
            ApplyBlur();
        }
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

    private void OnLayoutUpdated(object? sender, EventArgs e) => UpdateBackdrop(force: false);

    private void OnThemeChanged(NIRAThemeMode mode) =>
        Dispatcher.BeginInvoke(new Action(() => UpdateBackdrop(force: true)));

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

        BitmapImage source = mode == NIRAThemeMode.Halo ? HaloImage : EclipseImage;

        if (source.PixelWidth <= 0 || source.PixelHeight <= 0)
        {
            return;
        }

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

        Rect viewbox = Rect.Intersect(
            new Rect(0, 0, source.PixelWidth, source.PixelHeight),
            new Rect(
                (bounds.X - offsetX) / scale,
                (bounds.Y - offsetY) / scale,
                bounds.Width / scale,
                bounds.Height / scale));

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
        if (dependencyObject is NIRAUtilityBackdropBorder border)
        {
            border.UpdateBackdrop(force: true);
        }
    }

    private static BitmapImage LoadBitmap(string assetPath)
    {
        string assemblyName = typeof(NIRAUtilityBackdropBorder).Assembly.GetName().Name ?? "NIRAAgent.UI";
        Uri uri = new($"pack://application:,,,/{assemblyName};component/{assetPath}", UriKind.Absolute);

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

