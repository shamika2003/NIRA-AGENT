/*
 * filename: WindowsScreenCaptureBackend.cs
 */

using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

using SegaAgent.PC.Awareness;
using SegaAgent.Vision;

namespace SegaAgent.UI.Vision;


// =============================================================
// WINDOWS DESKTOP PIXEL CAPTURE BACKEND
//
// Low-level pixel acquisition only. It does not know about Sega's
// goals/cognition or interpret the resulting image.
// =============================================================

public sealed class WindowsScreenCaptureBackend
    : ISegaScreenCaptureBackend
{
    private const int SmXVirtualScreen =
        76;


    private const int SmYVirtualScreen =
        77;


    private const int SmCxVirtualScreen =
        78;


    private const int SmCyVirtualScreen =
        79;


    private const uint Srccopy =
        0x00CC0020;


    private const uint CaptureBlt =
        0x40000000;


    public PcRectangle VirtualScreenBounds
    {
        get
        {
            int left =
                GetSystemMetrics(
                    SmXVirtualScreen);


            int top =
                GetSystemMetrics(
                    SmYVirtualScreen);


            int width =
                GetSystemMetrics(
                    SmCxVirtualScreen);


            int height =
                GetSystemMetrics(
                    SmCyVirtualScreen);


            if (width <= 0 || height <= 0)
            {
                throw new InvalidOperationException(
                    "Windows did not report a valid virtual desktop size.");
            }


            return new PcRectangle(
                left,
                top,
                checked(left + width),
                checked(top + height));
        }
    }


    public IntPtr GetForegroundWindowHandle() =>
        GetForegroundWindow();


    public Task CapturePngAsync(
        PcRectangle bounds,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();


        if (bounds.IsEmpty)
        {
            throw new InvalidOperationException(
                "Cannot capture an empty screen rectangle.");
        }


        ArgumentException.ThrowIfNullOrWhiteSpace(
            destinationPath);


        string fullPath =
            Path.GetFullPath(
                destinationPath);


        string? directory =
            Path.GetDirectoryName(
                fullPath);


        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException(
                "Visual evidence path must include a directory.");
        }


        Directory.CreateDirectory(
            directory);


        string temporaryPath =
            fullPath +
            ".tmp-" +
            Guid.NewGuid().ToString("N");


        IntPtr screenDc =
            IntPtr.Zero;


        IntPtr memoryDc =
            IntPtr.Zero;


        IntPtr bitmapHandle =
            IntPtr.Zero;


        IntPtr previousObject =
            IntPtr.Zero;


        try
        {
            screenDc =
                GetDC(
                    IntPtr.Zero);


            if (screenDc == IntPtr.Zero)
            {
                throw LastWin32(
                    "GetDC failed while capturing the desktop.");
            }


            memoryDc =
                CreateCompatibleDC(
                    screenDc);


            if (memoryDc == IntPtr.Zero)
            {
                throw LastWin32(
                    "CreateCompatibleDC failed while capturing the desktop.");
            }


            bitmapHandle =
                CreateCompatibleBitmap(
                    screenDc,
                    bounds.Width,
                    bounds.Height);


            if (bitmapHandle == IntPtr.Zero)
            {
                throw LastWin32(
                    "CreateCompatibleBitmap failed while capturing the desktop.");
            }


            previousObject =
                SelectObject(
                    memoryDc,
                    bitmapHandle);


            if (previousObject == IntPtr.Zero ||
                previousObject == new IntPtr(-1))
            {
                throw LastWin32(
                    "SelectObject failed while preparing the capture bitmap.");
            }


            bool copied =
                BitBlt(
                    memoryDc,
                    0,
                    0,
                    bounds.Width,
                    bounds.Height,
                    screenDc,
                    bounds.Left,
                    bounds.Top,
                    Srccopy | CaptureBlt);


            if (!copied)
            {
                throw LastWin32(
                    "BitBlt failed while copying desktop pixels.");
            }


            cancellationToken.ThrowIfCancellationRequested();


            BitmapSource source =
                Imaging.CreateBitmapSourceFromHBitmap(
                    bitmapHandle,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());


            source.Freeze();


            PngBitmapEncoder encoder =
                new();


            encoder.Frames.Add(
                BitmapFrame.Create(
                    source));


            using (
                FileStream stream =
                    new(
                        temporaryPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        64 * 1024,
                        FileOptions.SequentialScan))
            {
                encoder.Save(
                    stream);


                stream.Flush(
                    flushToDisk: true);
            }


            cancellationToken.ThrowIfCancellationRequested();


            File.Move(
                temporaryPath,
                fullPath,
                overwrite: true);


            return Task.CompletedTask;
        }
        finally
        {
            if (previousObject != IntPtr.Zero &&
                previousObject != new IntPtr(-1) &&
                memoryDc != IntPtr.Zero)
            {
                SelectObject(
                    memoryDc,
                    previousObject);
            }


            if (bitmapHandle != IntPtr.Zero)
            {
                DeleteObject(
                    bitmapHandle);
            }


            if (memoryDc != IntPtr.Zero)
            {
                DeleteDC(
                    memoryDc);
            }


            if (screenDc != IntPtr.Zero)
            {
                ReleaseDC(
                    IntPtr.Zero,
                    screenDc);
            }


            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(
                        temporaryPath);
                }
            }
            catch
            {
            }
        }
    }


    private static Win32Exception LastWin32(
        string message) =>
        new(
            Marshal.GetLastWin32Error(),
            message);


    [DllImport(
        "user32.dll")]
    private static extern IntPtr GetForegroundWindow();


    [DllImport(
        "user32.dll")]
    private static extern int GetSystemMetrics(
        int index);


    [DllImport(
        "user32.dll",
        SetLastError = true)]
    private static extern IntPtr GetDC(
        IntPtr windowHandle);


    [DllImport(
        "user32.dll",
        SetLastError = true)]
    private static extern int ReleaseDC(
        IntPtr windowHandle,
        IntPtr deviceContext);


    [DllImport(
        "gdi32.dll",
        SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(
        IntPtr deviceContext);


    [DllImport(
        "gdi32.dll",
        SetLastError = true)]
    private static extern bool DeleteDC(
        IntPtr deviceContext);


    [DllImport(
        "gdi32.dll",
        SetLastError = true)]
    private static extern IntPtr CreateCompatibleBitmap(
        IntPtr deviceContext,
        int width,
        int height);


    [DllImport(
        "gdi32.dll",
        SetLastError = true)]
    private static extern IntPtr SelectObject(
        IntPtr deviceContext,
        IntPtr gdiObject);


    [DllImport(
        "gdi32.dll",
        SetLastError = true)]
    private static extern bool DeleteObject(
        IntPtr gdiObject);


    [DllImport(
        "gdi32.dll",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(
        IntPtr destinationDc,
        int destinationX,
        int destinationY,
        int width,
        int height,
        IntPtr sourceDc,
        int sourceX,
        int sourceY,
        uint rasterOperation);
}
