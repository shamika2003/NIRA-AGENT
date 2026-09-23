/*
 * filename: NIRAVisualArtifactToastWindow.xaml.cs
 */

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

using NIRAAgent.PC.Awareness;
using NIRAAgent.UI.ViewModels;
using NIRAAgent.UI.Theming;

namespace NIRAAgent.UI.Visuals;

public partial class NIRAVisualArtifactToastWindow : Window
{
    private const int GwlExStyle =
        -20;

    private const long WsExNoActivate =
        0x08000000L;

    private const long WsExToolWindow =
        0x00000080L;

    private const uint SwpNoSize =
        0x0001;

    private const uint SwpNoZOrder =
        0x0004;

    private const uint SwpNoActivate =
        0x0010;


    // Timings are deliberately toast-local: only floating visual previews auto-hide.
    private const int AutoHideAfterMilliseconds = 6500;
    // Hover pauses the ordinary dismissal, but never leaves a floating image
    // stuck on the desktop indefinitely.
    private const int MaximumLifetimeMilliseconds = 14500;
    private const int DissolveMilliseconds = 850;
    private const int DissolveParticleCount = 240;

    private readonly DispatcherTimer _autoHideTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(AutoHideAfterMilliseconds)
    };

    private readonly DispatcherTimer _maximumLifetimeTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(MaximumLifetimeMilliseconds)
    };

    private readonly Random _random = new();
    private bool _isDismissing;

    public event Action?
        OpenChatRequested;


    public event Action?
        ViewRequested;


    public NIRAVisualArtifactToastWindow(
        VisualArtifactViewModel artifact)
    {
        ArgumentNullException.ThrowIfNull(
            artifact);

        InitializeComponent();

        DataContext =
            artifact;

        SourceInitialized +=
            Toast_SourceInitialized;

        _autoHideTimer.Tick += AutoHideTimer_Tick;
        _maximumLifetimeTimer.Tick += MaximumLifetimeTimer_Tick;

        // Hover keeps the preview available while the user is reading or choosing an action.
        MouseEnter += (_, _) => _autoHideTimer.Stop();
        MouseLeave += (_, _) =>
        {
            if (!_isDismissing)
            {
                RestartAutoHide();
            }
        };

        Closed += (_, _) =>
        {
            _autoHideTimer.Stop();
            _autoHideTimer.Tick -= AutoHideTimer_Tick;
            _maximumLifetimeTimer.Stop();
            _maximumLifetimeTimer.Tick -= MaximumLifetimeTimer_Tick;
            DissolveParticles.Children.Clear();
        };
    }


    public void ShowAt(
        NIRAVisualToastPlacementService placement)
    {
        ArgumentNullException.ThrowIfNull(
            placement);

        Opacity =
            0.0;

        Show();
        UpdateLayout();

        Point topLeft =
            PointToScreen(
                new Point(0.0, 0.0));

        Point bottomRight =
            PointToScreen(
                new Point(
                    Math.Max(1.0, ActualWidth),
                    Math.Max(1.0, ActualHeight)));

        int physicalWidth =
            Math.Max(
                1,
                (int)Math.Ceiling(
                    Math.Abs(bottomRight.X - topLeft.X)));

        int physicalHeight =
            Math.Max(
                1,
                (int)Math.Ceiling(
                    Math.Abs(bottomRight.Y - topLeft.Y)));

        PcRectangle target =
            placement.Resolve(
                physicalWidth,
                physicalHeight);

        IntPtr handle =
            new WindowInteropHelper(this).Handle;

        if (handle !=
            IntPtr.Zero)
        {
            SetWindowPos(
                handle,
                IntPtr.Zero,
                target.Left,
                target.Top,
                0,
                0,
                SwpNoSize |
                SwpNoZOrder |
                SwpNoActivate);
        }

        Opacity =
            1.0;

        NIRAMotion.AnimateEntrance(
            ToastChrome,
            distance: 10.0,
            durationMs: 220);

        RestartAutoHide();
        _maximumLifetimeTimer.Stop();
        _maximumLifetimeTimer.Start();
    }


    private void Toast_SourceInitialized(
        object? sender,
        EventArgs e)
    {
        IntPtr handle =
            new WindowInteropHelper(this).Handle;

        long current =
            GetWindowLongPtr(
                handle,
                GwlExStyle)
            .ToInt64();

        SetWindowLongPtr(
            handle,
            GwlExStyle,
            new IntPtr(
                current |
                WsExNoActivate |
                WsExToolWindow));
    }


    private void AutoHideTimer_Tick(object? sender, EventArgs e)
    {
        _autoHideTimer.Stop();
        BeginDismiss();
    }

    private void MaximumLifetimeTimer_Tick(object? sender, EventArgs e)
    {
        _maximumLifetimeTimer.Stop();
        BeginDismiss();
    }


    private void RestartAutoHide()
    {
        if (_isDismissing || !IsVisible || IsMouseOver)
        {
            return;
        }

        _autoHideTimer.Stop();
        _autoHideTimer.Start();
    }


    private void BeginDismiss()
    {
        if (_isDismissing)
        {
            return;
        }

        _isDismissing = true;
        _autoHideTimer.Stop();
        _maximumLifetimeTimer.Stop();
        IsHitTestVisible = false;

        // The original preview remains accessible in chat and the archive.
        // Only this non-activating desktop toast is dismissed.
        SpawnDissolveParticles();

        ToastChrome.RenderTransformOrigin = new Point(0.5, 0.5);
        TranslateTransform translate =
            ToastChrome.RenderTransform as TranslateTransform
            ?? new TranslateTransform();
        ToastChrome.RenderTransform = translate;

        TimeSpan duration = TimeSpan.FromMilliseconds(DissolveMilliseconds);
        ToastChrome.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(1.0, 0.0, duration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            });

        translate.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(0.0, -12.0, duration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            });

        // A storyboard completion, rather than Task.Delay, avoids closing during
        // dispatcher stalls and keeps all WPF visual work on the UI thread.
        DoubleAnimation rootFade = new(
            1.0,
            0.0,
            TimeSpan.FromMilliseconds(DissolveMilliseconds + 60))
        {
            BeginTime = TimeSpan.FromMilliseconds(220),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        rootFade.Completed += (_, _) =>
        {
            if (IsVisible)
            {
                Close();
            }
        };
        ToastRoot.BeginAnimation(UIElement.OpacityProperty, rootFade);
    }


    private void SpawnDissolveParticles()
    {
        DissolveParticles.Children.Clear();
        double width = Math.Max(1.0, ToastChrome.ActualWidth);
        double height = Math.Max(1.0, ToastChrome.ActualHeight);

        // Tiny, mostly sub-two-DIP points like the NIRA orb, rather than
        // the previous 2-6.5 DIP confetti-sized ellipses.
        Color[] palette =
        {
            Color.FromRgb(120, 219, 255),
            Color.FromRgb(172, 229, 255),
            Color.FromRgb(85, 139, 255),
            Color.FromRgb(154, 117, 255)
        };

        for (int i = 0; i < DissolveParticleCount; i++)
        {
            double size = _random.NextDouble() < 0.09
                ? 1.5 + _random.NextDouble() * 0.65
                : 0.75 + _random.NextDouble() * 0.85;
            double x = _random.NextDouble() * Math.Max(0, width - size);
            double y = _random.NextDouble() * Math.Max(0, height - size);
            Color color = palette[_random.Next(palette.Length)];

            Ellipse particle = new()
            {
                Width = size,
                Height = size,
                Fill = new SolidColorBrush(color),
                Opacity = 0.48 + _random.NextDouble() * 0.34,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            Canvas.SetLeft(particle, x);
            Canvas.SetTop(particle, y);

            TranslateTransform drift = new();
            particle.RenderTransform = drift;
            DissolveParticles.Children.Add(particle);

            double seconds = (DissolveMilliseconds - 80) / 1000.0;
            TimeSpan duration = TimeSpan.FromSeconds(seconds);
            TimeSpan delay = TimeSpan.FromMilliseconds(_random.Next(0, 120));
            double driftX = (_random.NextDouble() - 0.5) * 52.0;
            double driftY = -6.0 - _random.NextDouble() * 39.0;

            particle.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(particle.Opacity, 0.0, duration)
                {
                    BeginTime = delay,
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                });
            drift.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation(0.0, driftX, duration)
                {
                    BeginTime = delay,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
            drift.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(0.0, driftY, duration)
                {
                    BeginTime = delay,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
        }
    }


    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        BeginDismiss();
    }


    private void ViewButton_Click(object sender, RoutedEventArgs e)
    {
        ViewRequested?.Invoke();
        BeginDismiss();
    }


    private void OpenChatButton_Click(object sender, RoutedEventArgs e)
    {
        OpenChatRequested?.Invoke();
        BeginDismiss();
    }


    private static IntPtr GetWindowLongPtr(
        IntPtr hWnd,
        int nIndex)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr64(hWnd, nIndex)
            : new IntPtr(GetWindowLong32(hWnd, nIndex));
    }


    private static IntPtr SetWindowLongPtr(
        IntPtr hWnd,
        int nIndex,
        IntPtr value)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, nIndex, value)
            : new IntPtr(SetWindowLong32(hWnd, nIndex, value.ToInt32()));
    }


    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(
        IntPtr hWnd,
        int nIndex);


    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(
        IntPtr hWnd,
        int nIndex);


    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(
        IntPtr hWnd,
        int nIndex,
        int newLong);


    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(
        IntPtr hWnd,
        int nIndex,
        IntPtr newLong);


    [DllImport(
        "user32.dll",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);
}
