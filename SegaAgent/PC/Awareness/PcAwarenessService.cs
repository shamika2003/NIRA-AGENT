/*
 * filename: PcAwarenessService.cs
 */

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SegaAgent.PC.Awareness;

public sealed class PcAwarenessService
{
    // =========================================================
    // CONSTANTS
    // =========================================================

    private const uint MonitorDefaultToNearest =
        0x00000002;


    private const uint MonitorDefaultToPrimary =
        0x00000001;


    private const uint MonitorInfoPrimary =
        0x00000001;


    private const int FullscreenTolerance =
        2;


    // =========================================================
    // WIN32 - CURSOR
    // =========================================================

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    private static extern bool GetCursorPos(
        out NativePoint point);


    // =========================================================
    // WIN32 - FOREGROUND WINDOW
    // =========================================================

    [DllImport(
        "user32.dll")]
    private static extern IntPtr GetForegroundWindow();


    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern int GetWindowText(
        IntPtr hWnd,
        StringBuilder text,
        int count);


    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern int GetWindowTextLength(
        IntPtr hWnd);


    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern int GetClassName(
        IntPtr hWnd,
        StringBuilder className,
        int maxCount);


    [DllImport(
        "user32.dll",
        SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(
        IntPtr hWnd,
        out uint processId);


    [DllImport(
        "user32.dll",
        SetLastError = true)]
    private static extern bool GetWindowRect(
        IntPtr hWnd,
        out NativeRect rect);


    [DllImport(
        "user32.dll")]
    private static extern bool IsIconic(
        IntPtr hWnd);


    [DllImport(
        "user32.dll")]
    private static extern bool IsZoomed(
        IntPtr hWnd);


    // =========================================================
    // WIN32 - USER ACTIVITY
    // =========================================================

    [DllImport(
        "user32.dll")]
    private static extern bool GetLastInputInfo(
        ref LastInputInfo lastInputInfo);


    // =========================================================
    // WIN32 - MONITOR
    // =========================================================

    [DllImport(
        "user32.dll")]
    private static extern IntPtr MonitorFromWindow(
        IntPtr hWnd,
        uint flags);


    [DllImport(
        "user32.dll",
        SetLastError = true)]
    private static extern bool GetMonitorInfo(
        IntPtr monitor,
        ref MonitorInfo monitorInfo);


    // =========================================================
    // NATIVE STRUCTURES
    // =========================================================

    [StructLayout(
        LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;

        public int Y;
    }


    [StructLayout(
        LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;

        public int Top;

        public int Right;

        public int Bottom;
    }


    [StructLayout(
        LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint cbSize;

        public uint dwTime;
    }


    [StructLayout(
        LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint cbSize;

        public NativeRect rcMonitor;

        public NativeRect rcWork;

        public uint dwFlags;
    }


    // =========================================================
    // READ WORLD STATE
    // =========================================================

    public PcWorldState Read()
    {
        NativePoint mouse =
            ReadMousePosition();


        TimeSpan idleTime =
            ReadUserIdleTime();


        IntPtr foregroundHandle =
            GetForegroundWindow();


        PcDisplayState display =
            ReadDisplayState(
                foregroundHandle);


        PcForegroundWindowState foregroundWindow =
            ReadForegroundWindowState(
                foregroundHandle,
                display);


        return new PcWorldState
        {
            Timestamp =
                DateTime.UtcNow,

            User =
                new PcUserState
                {
                    IdleTime =
                        idleTime
                },

            Mouse =
                new PcMouseState
                {
                    X =
                        mouse.X,

                    Y =
                        mouse.Y
                },

            ForegroundWindow =
                foregroundWindow,

            Display =
                display
        };
    }


    // =========================================================
    // MOUSE
    // =========================================================

    private static NativePoint
        ReadMousePosition()
    {
        if (!GetCursorPos(
                out NativePoint point))
        {
            return new NativePoint
            {
                X = 0,
                Y = 0
            };
        }


        return point;
    }


    // =========================================================
    // USER IDLE TIME
    // =========================================================

    private static TimeSpan
        ReadUserIdleTime()
    {
        LastInputInfo info =
            new()
            {
                cbSize =
                    (uint)Marshal.SizeOf<
                        LastInputInfo>()
            };


        if (!GetLastInputInfo(
                ref info))
        {
            return TimeSpan.Zero;
        }


        /*
         * LASTINPUTINFO stores a 32-bit tick value.
         *
         * Converting TickCount64 back to uint preserves
         * the same wraparound behavior.
         */

        uint currentTick =
            unchecked(
                (uint)Environment.TickCount64);


        uint idleMilliseconds =
            unchecked(
                currentTick -
                info.dwTime);


        return TimeSpan.FromMilliseconds(
            idleMilliseconds);
    }


    // =========================================================
    // FOREGROUND WINDOW
    // =========================================================

    private static PcForegroundWindowState
        ReadForegroundWindowState(
            IntPtr handle,
            PcDisplayState display)
    {
        if (handle ==
            IntPtr.Zero)
        {
            return new PcForegroundWindowState();
        }


        uint processId =
            ReadProcessId(
                handle);


        string processName =
            ReadProcessName(
                processId);


        string title =
            ReadWindowTitle(
                handle);


        string className =
            ReadWindowClassName(
                handle);


        PcRectangle bounds =
            ReadWindowBounds(
                handle);


        bool minimized =
            IsIconic(
                handle);


        bool maximized =
            IsZoomed(
                handle);


        bool fullscreen =
            IsWindowFullscreen(
                bounds,
                display.MonitorBounds,
                minimized);


        return new PcForegroundWindowState
        {
            Handle =
                handle,

            ProcessId =
                unchecked(
                    (int)processId),

            ProcessName =
                processName,

            Title =
                title,

            ClassName =
                className,

            Bounds =
                bounds,

            IsMinimized =
                minimized,

            IsMaximized =
                maximized,

            IsFullscreen =
                fullscreen
        };
    }


    // =========================================================
    // PROCESS ID
    // =========================================================

    private static uint ReadProcessId(
        IntPtr handle)
    {
        _ =
            GetWindowThreadProcessId(
                handle,
                out uint processId);


        return processId;
    }


    // =========================================================
    // PROCESS NAME
    // =========================================================

    private static string ReadProcessName(
        uint processId)
    {
        if (processId == 0)
        {
            return string.Empty;
        }


        try
        {
            using Process process =
                Process.GetProcessById(
                    unchecked(
                        (int)processId));


            return
                process.ProcessName
                ?? string.Empty;
        }
        catch
        {
            /*
             * Processes can terminate between the Win32
             * observation and this lookup.
             *
             * Awareness should never crash because of that.
             */

            return string.Empty;
        }
    }


    // =========================================================
    // WINDOW TITLE
    // =========================================================

    private static string ReadWindowTitle(
        IntPtr handle)
    {
        if (handle ==
            IntPtr.Zero)
        {
            return string.Empty;
        }


        int length =
            GetWindowTextLength(
                handle);


        if (length <= 0)
        {
            return string.Empty;
        }


        /*
         * Avoid allocating an unexpectedly enormous buffer
         * from a malformed/native window.
         */

        int capacity =
            Math.Clamp(
                length + 1,
                2,
                4096);


        StringBuilder title =
            new(
                capacity);


        int result =
            GetWindowText(
                handle,
                title,
                title.Capacity);


        if (result <= 0)
        {
            return string.Empty;
        }


        return title.ToString();
    }


    // =========================================================
    // WINDOW CLASS
    // =========================================================

    private static string
        ReadWindowClassName(
            IntPtr handle)
    {
        if (handle ==
            IntPtr.Zero)
        {
            return string.Empty;
        }


        StringBuilder className =
            new(
                256);


        int result =
            GetClassName(
                handle,
                className,
                className.Capacity);


        if (result <= 0)
        {
            return string.Empty;
        }


        return className.ToString();
    }


    // =========================================================
    // WINDOW BOUNDS
    // =========================================================

    private static PcRectangle
        ReadWindowBounds(
            IntPtr handle)
    {
        if (handle ==
            IntPtr.Zero)
        {
            return default;
        }


        if (!GetWindowRect(
                handle,
                out NativeRect rect))
        {
            return default;
        }


        return ToRectangle(
            rect);
    }


    // =========================================================
    // DISPLAY
    // =========================================================

    private static PcDisplayState
        ReadDisplayState(
            IntPtr foregroundHandle)
    {
        uint monitorMode =
            foregroundHandle !=
            IntPtr.Zero
                ? MonitorDefaultToNearest
                : MonitorDefaultToPrimary;


        IntPtr monitor =
            MonitorFromWindow(
                foregroundHandle,
                monitorMode);


        if (monitor ==
            IntPtr.Zero)
        {
            return new PcDisplayState();
        }


        MonitorInfo info =
            new()
            {
                cbSize =
                    (uint)Marshal.SizeOf<
                        MonitorInfo>()
            };


        if (!GetMonitorInfo(
                monitor,
                ref info))
        {
            return new PcDisplayState();
        }


        return new PcDisplayState
        {
            MonitorBounds =
                ToRectangle(
                    info.rcMonitor),

            WorkArea =
                ToRectangle(
                    info.rcWork),

            IsPrimary =
                (
                    info.dwFlags &
                    MonitorInfoPrimary
                )
                != 0
        };
    }


    // =========================================================
    // FULLSCREEN
    // =========================================================

    private static bool
        IsWindowFullscreen(
            PcRectangle windowBounds,
            PcRectangle monitorBounds,
            bool minimized)
    {
        if (minimized)
        {
            return false;
        }


        if (windowBounds.IsEmpty ||
            monitorBounds.IsEmpty)
        {
            return false;
        }


        return
            NearlyEqual(
                windowBounds.Left,
                monitorBounds.Left)
            &&
            NearlyEqual(
                windowBounds.Top,
                monitorBounds.Top)
            &&
            NearlyEqual(
                windowBounds.Right,
                monitorBounds.Right)
            &&
            NearlyEqual(
                windowBounds.Bottom,
                monitorBounds.Bottom);
    }


    // =========================================================
    // NEARLY EQUAL
    // =========================================================

    private static bool NearlyEqual(
        int first,
        int second)
    {
        return Math.Abs(
                   first -
                   second)
               <=
               FullscreenTolerance;
    }


    // =========================================================
    // CONVERT RECTANGLE
    // =========================================================

    private static PcRectangle ToRectangle(
        NativeRect rect)
    {
        return new PcRectangle(
            rect.Left,
            rect.Top,
            rect.Right,
            rect.Bottom);
    }
}