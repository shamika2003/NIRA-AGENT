using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace SegaAgent.UI.Companion;

internal static class MousePosition
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;

        public int Y;
    }


    [DllImport(
        "user32.dll",
        SetLastError = true)]
    private static extern bool GetCursorPos(
        out NativePoint point);


    public static Point Get()
    {
        if (!GetCursorPos(
                out NativePoint point))
        {
            return new Point(
                0,
                0);
        }


        return new Point(
            point.X,
            point.Y);
    }
}