using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SegaAgent.UI;

/// <summary>
/// Keeps borderless WindowChrome windows inside the active monitor work area
/// when maximized, so the taskbar never covers the bottom or side of the UI.
/// </summary>
internal sealed class SegaWorkAreaWindowGuard : IDisposable
{
    private const int WmGetMinMaxInfo = 0x0024;
    private const uint MonitorDefaultToNearest = 0x00000002;

    private readonly Window _window;
    private HwndSource? _source;
    private bool _disposed;

    public SegaWorkAreaWindowGuard(Window window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _window.SourceInitialized += Window_SourceInitialized;
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
