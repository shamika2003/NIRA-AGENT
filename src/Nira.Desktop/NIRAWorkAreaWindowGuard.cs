using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace NIRAAgent.UI;

/// <summary>
/// Keeps borderless WindowChrome windows inside the active monitor work area
/// when maximized, so the taskbar never covers the bottom or side of the UI.
/// </summary>
internal sealed class NIRAWorkAreaWindowGuard : IDisposable
{
    private const int WmGetMinMaxInfo = 0x0024;
    private const uint MonitorDefaultToNearest = 0x00000002;

    private readonly Window _window;
    private readonly bool _fitNormalWindow;
    private bool _normalWindowFitted;
    private HwndSource? _source;
    private bool _disposed;

    public NIRAWorkAreaWindowGuard(Window window, bool fitNormalWindow = false)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _fitNormalWindow = fitNormalWindow;
        _window.SourceInitialized += Window_SourceInitialized;
        if (_fitNormalWindow)
            _window.Loaded += Window_Loaded;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_normalWindowFitted || _disposed) return;
        // After WPF has selected the owner monitor, before entrance animation.
        FitNormalWindowToCurrentMonitor();
    }

    private void FitNormalWindowToCurrentMonitor()
    {
        if (!_fitNormalWindow || _normalWindowFitted || _window.WindowState != WindowState.Normal)
            return;

        IntPtr hwnd = new WindowInteropHelper(_window).Handle;
        if (hwnd == IntPtr.Zero) return;
        IntPtr monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero) return;

        NativeMonitorInfo info = new() { CbSize = Marshal.SizeOf<NativeMonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return;

        // Win32 monitor rectangles are device pixels, whereas WPF Window dimensions
        // are DIPs. Use the ACTUAL presentation-source transform for current DPI.
        Matrix fromDevice = _source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        Point topLeft = fromDevice.Transform(new Point(info.WorkArea.Left, info.WorkArea.Top));
        Point bottomRight = fromDevice.Transform(new Point(info.WorkArea.Right, info.WorkArea.Bottom));
        double workWidth = bottomRight.X - topLeft.X;
        double workHeight = bottomRight.Y - topLeft.Y;
        if (workWidth < 360 || workHeight < 320) return;

        // Limit the opening height to 84% of usable space. Unlike a fixed 780-DIP
        // window this still fits 768p laptops, higher DPI, and a relocated taskbar.
        double marginX = Math.Min(28, workWidth * 0.025);
        double marginY = Math.Min(24, workHeight * 0.03);
        double targetWidth = Math.Min(_window.Width, workWidth - 2 * marginX);
        double targetHeight = Math.Min(_window.Height,
            Math.Min(workHeight * 0.84, workHeight - 2 * marginY));

        // Fixed XAML minima must not defeat the available-screen calculation.
        _window.MinWidth = Math.Min(_window.MinWidth, targetWidth);
        _window.MinHeight = Math.Min(_window.MinHeight, targetHeight);
        _window.MaxWidth = workWidth;
        _window.MaxHeight = workHeight;
        _window.WindowStartupLocation = WindowStartupLocation.Manual;
        _window.Width = targetWidth;
        _window.Height = targetHeight;
        _window.Left = topLeft.X + (workWidth - targetWidth) / 2;
        _window.Top = topLeft.Y + (workHeight - targetHeight) / 2;
        _normalWindowFitted = true;
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        if (_disposed) return;

        IntPtr handle = new WindowInteropHelper(_window).Handle;
        if (handle == IntPtr.Zero) return;

        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WindowProc);
    }

    private IntPtr WindowProc(
        IntPtr hwnd,
        int msg,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (msg == WmGetMinMaxInfo)
        {
            ApplyWorkArea(hwnd, lParam);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private static void ApplyWorkArea(IntPtr hwnd, IntPtr lParam)
    {
        NativeMinMaxInfo info = Marshal.PtrToStructure<NativeMinMaxInfo>(lParam);
        IntPtr monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero) return;

        NativeMonitorInfo monitorInfo = new()
        {
            CbSize = Marshal.SizeOf<NativeMonitorInfo>()
        };

        if (!GetMonitorInfo(monitor, ref monitorInfo)) return;

        NativeRect work = monitorInfo.WorkArea;
        NativeRect screen = monitorInfo.MonitorArea;

        info.MaxPosition.X = work.Left - screen.Left;
        info.MaxPosition.Y = work.Top - screen.Top;
        info.MaxSize.X = work.Right - work.Left;
        info.MaxSize.Y = work.Bottom - work.Top;
        info.MaxTrackSize = info.MaxSize;

        Marshal.StructureToPtr(info, lParam, fDeleteOld: true);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _window.SourceInitialized -= Window_SourceInitialized;
        if (_fitNormalWindow)
            _window.Loaded -= Window_Loaded;
        if (_source != null)
        {
            _source.RemoveHook(WindowProc);
            _source = null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMonitorInfo
    {
        public int CbSize;
        public NativeRect MonitorArea;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref NativeMonitorInfo info);
}
