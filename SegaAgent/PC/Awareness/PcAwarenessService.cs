/*
 * filename: PcAwarenessService.cs
 */



using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SegaAgent.PC.Awareness;

public sealed class PcAwarenessService
{
    // =========================================================
    // WIN32
    // =========================================================

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    private static extern bool GetCursorPos(
        out NativePoint point);

    [DllImport(
        "user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(
        IntPtr hWnd,
        StringBuilder text,
        int count);

    [DllImport(
        "user32.dll")]
    private static extern bool GetLastInputInfo(
        ref LastInputInfo lastInputInfo);

    [DllImport(
        "kernel32.dll")]
    private static extern uint GetTickCount();

    // =========================================================
    // NATIVE STRUCTURES
    // =========================================================

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint cbSize;
        public uint dwTime;
    }

    // =========================================================
    // READ PC STATE
    // =========================================================

    public PcState Read()
    {
        NativePoint mouse =
            GetMousePosition();

        return new PcState
        {
            Timestamp =
                DateTime.UtcNow,

            MouseX =
                mouse.X,

            MouseY =
                mouse.Y,

            ScreenWidth =
                GetScreenWidth(),

            ScreenHeight =
                GetScreenHeight(),

            UserIdleTime =
                GetUserIdleTime(),

            ActiveApplication =
                GetActiveApplication()
        };
    }

    // =========================================================
    // MOUSE
    // =========================================================

    private static NativePoint GetMousePosition()
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
    // SCREEN
    // =========================================================

    private static int GetScreenWidth()
    {
        return GetSystemMetrics(
            SystemMetric.ScreenWidth);
    }

    private static int GetScreenHeight()
    {
        return GetSystemMetrics(
            SystemMetric.ScreenHeight);
    }

    [DllImport(
        "user32.dll")]
    private static extern int GetSystemMetrics(
        SystemMetric index);

    private enum SystemMetric
    {
        ScreenWidth = 0,

        ScreenHeight = 1
    }

    // =========================================================
    // USER IDLE TIME
    // =========================================================

    private static TimeSpan GetUserIdleTime()
    {
        LastInputInfo info =
            new()
            {
                cbSize =
                    (uint)Marshal.SizeOf<
                        LastInputInfo>()
            };

        bool success =
            GetLastInputInfo(
                ref info);

        if (!success)
        {
            return TimeSpan.Zero;
        }

        uint current =
            GetTickCount();

        uint idleMilliseconds =
            current -
            info.dwTime;

        return TimeSpan.FromMilliseconds(
            idleMilliseconds);
    }

    // =========================================================
    // ACTIVE APPLICATION
    // =========================================================

    private static string GetActiveApplication()
    {
        IntPtr handle =
            GetForegroundWindow();

        if (handle == IntPtr.Zero)
            return string.Empty;

        StringBuilder title =
            new(256);

        int length =
            GetWindowText(
                handle,
                title,
                title.Capacity);

        if (length <= 0)
            return string.Empty;

        return title.ToString();
    }
}