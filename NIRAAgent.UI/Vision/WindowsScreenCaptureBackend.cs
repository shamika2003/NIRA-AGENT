/*
 * filename: WindowsScreenCaptureBackend.cs
 */

using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

using NIRAAgent.PC.Awareness;
using NIRAAgent.Vision;
namespace NIRAAgent.UI.Vision;


// =============================================================
// WINDOWS VISUAL CAPTURE BACKEND
//
// Two permanent capture paths are intentionally kept separate:
//
// 1. Desktop rectangle / monitor / region -> BitBlt
// 2. Specific HWND, including an occluded background window ->
//    PrintWindow(PW_RENDERFULLCONTENT), with normal PrintWindow as
//    a compatibility fallback.
//
// The core vision layer decides WHICH window is relevant. This
// backend only reads Windows state and pixels for that exact target.
// A later Windows.Graphics.Capture path can be added ahead of the
// PrintWindow path without changing NIRA's evidence contracts;
// PrintWindow remains a useful native fallback for compatible apps.
// =============================================================

public sealed class WindowsScreenCaptureBackend
    : INIRAScreenCaptureBackend
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


    private const uint PwRenderFullContent =
        0x00000002;


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


    // =========================================================
    // LIVE TOP-LEVEL WINDOW DISCOVERY
    //
    // Background capture must not require an application to have
    // been foreground after NIRA started. Enumerating top-level
    // Windows HWNDs gives the core vision service a live catalogue
    // of currently discoverable application windows. No application
    // names are special-cased here.
    // =========================================================

    public IReadOnlyList<NIRACaptureWindowSnapshot> EnumerateTopLevelWindows()
    {
        List<NIRACaptureWindowSnapshot> windows =
            new();


        EnumWindowsProc callback =
            (windowHandle, _) =>
            {
                if (windowHandle == IntPtr.Zero ||
                    !IsWindowVisible(windowHandle))
                {
                    return true;
                }


                // Some visible top-level HWNDs (shell/overlay placeholders)
                // report zero-size rectangles. Skip them before ReadWindow so
                // routine enumeration does not throw first-chance exceptions
                // on every foreground/context refresh. A capture explicitly
                // targeting a bad HWND still retains its normal error path.
                if (!GetWindowRect(windowHandle, out NativeRect candidateRect) ||
                    candidateRect.Right <= candidateRect.Left ||
                    candidateRect.Bottom <= candidateRect.Top)
                    return true;

                try
                {
                    NIRACaptureWindowSnapshot window =
                        ReadWindow(
                            windowHandle);


                    if (window.ProcessId <= 0 ||
                        window.Bounds.IsEmpty ||
                        string.IsNullOrWhiteSpace(window.Title))
                    {
                        return true;
                    }


                    windows.Add(
                        window);
                }
                catch
                {
                    // A top-level HWND can disappear while EnumWindows is
                    // walking the desktop. Skip only that transient entry.
                }


                return true;
            };


        if (!EnumWindows(
                callback,
                IntPtr.Zero))
        {
            throw LastWin32(
                "EnumWindows failed while discovering visual window targets.");
        }


        return windows;
    }


    // =========================================================
    // READ ONE ARBITRARY HWND
    // =========================================================

    public NIRACaptureWindowSnapshot ReadWindow(
        IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero ||
            !IsWindow(windowHandle))
        {
            throw new InvalidOperationException(
                "The requested visual window handle is no longer valid.");
        }


        _ =
            GetWindowThreadProcessId(
                windowHandle,
                out uint processId);


        if (processId == 0)
        {
            throw new InvalidOperationException(
                "Windows could not resolve a process for the requested visual window.");
        }


        if (!GetWindowRect(
                windowHandle,
                out NativeRect rect))
        {
            throw LastWin32(
                "GetWindowRect failed for the requested visual window.");
        }


        PcRectangle bounds =
            new(
                rect.Left,
                rect.Top,
                rect.Right,
                rect.Bottom);


        if (bounds.IsEmpty)
        {
            throw new InvalidOperationException(
                "The requested visual window has empty bounds.");
        }


        return new NIRACaptureWindowSnapshot
        {
            Handle =
                windowHandle.ToInt64(),

            ProcessId =
                unchecked((int)processId),

            ProcessName =
                ReadProcessName(
                    processId),

            Title =
                ReadWindowTitle(
                    windowHandle),

            ClassName =
                ReadWindowClassName(
                    windowHandle),

            Bounds =
                bounds,

            IsMinimized =
                IsIconic(
                    windowHandle),

            IsMaximized =
                IsZoomed(
                    windowHandle)
        };
    }


    // =========================================================
    // DESKTOP / MONITOR / REGION PIXELS
    // =========================================================

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


        string fullPath =
            PrepareDestination(
                destinationPath,
                out string temporaryPath);


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
                    "SelectObject failed while preparing the desktop capture bitmap.");
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


            SaveBitmapHandleAsPng(
                bitmapHandle,
                temporaryPath,
                fullPath,
                cancellationToken);


            return Task.CompletedTask;
        }
        finally
        {
            RestoreAndReleaseGdi(
                memoryDc,
                bitmapHandle,
                previousObject);


            if (screenDc != IntPtr.Zero)
            {
                ReleaseDC(
                    IntPtr.Zero,
                    screenDc);
            }


            TryDelete(
                temporaryPath);
        }
    }


    // =========================================================
    // BACKGROUND / EXPLICIT HWND PIXELS
    //
    // PrintWindow asks the target window to render itself into NIRA's
    // memory DC. Therefore the target does not have to be foreground
    // and it may be covered by another ordinary window.
    // =========================================================

    public Task<NIRAWindowCaptureBackendResult> CaptureWindowPngAsync(
        IntPtr windowHandle,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();


        NIRACaptureWindowSnapshot before =
            ReadWindow(
                windowHandle);


        if (before.IsMinimized)
        {
            throw new InvalidOperationException(
                "The requested visual window is minimized. Background capture does not claim reliable pixels for minimized windows.");
        }


        string fullPath =
            PrepareDestination(
                destinationPath,
                out string temporaryPath);


        IntPtr referenceDc =
            IntPtr.Zero;


        IntPtr memoryDc =
            IntPtr.Zero;


        IntPtr bitmapHandle =
            IntPtr.Zero;


        IntPtr previousObject =
            IntPtr.Zero;


        string captureMethod =
            "PrintWindow(PW_RENDERFULLCONTENT)";


        try
        {
            referenceDc =
                GetDC(
                    IntPtr.Zero);


            if (referenceDc == IntPtr.Zero)
            {
                throw LastWin32(
                    "GetDC failed while preparing background window capture.");
            }


            memoryDc =
                CreateCompatibleDC(
                    referenceDc);


            if (memoryDc == IntPtr.Zero)
            {
                throw LastWin32(
                    "CreateCompatibleDC failed while preparing background window capture.");
            }


            bitmapHandle =
                CreateCompatibleBitmap(
                    referenceDc,
                    before.Bounds.Width,
                    before.Bounds.Height);


            if (bitmapHandle == IntPtr.Zero)
            {
                throw LastWin32(
                    "CreateCompatibleBitmap failed while preparing background window capture.");
            }


            previousObject =
                SelectObject(
                    memoryDc,
                    bitmapHandle);


            if (previousObject == IntPtr.Zero ||
                previousObject == new IntPtr(-1))
            {
                throw LastWin32(
                    "SelectObject failed while preparing the background window bitmap.");
            }


            bool rendered =
                PrintWindow(
                    windowHandle,
                    memoryDc,
                    PwRenderFullContent);


            if (!rendered)
            {
                captureMethod =
                    "PrintWindow(default)";


                rendered =
                    PrintWindow(
                        windowHandle,
                        memoryDc,
                        0);
            }


            if (!rendered)
            {
                throw LastWin32(
                    "Windows could not render the requested background window into the capture surface.");
            }


            cancellationToken.ThrowIfCancellationRequested();


            NIRACaptureWindowSnapshot after =
                ReadWindow(
                    windowHandle);


            if (after.ProcessId != before.ProcessId)
            {
                throw new InvalidOperationException(
                    "The requested window handle changed process identity during capture, so the image was discarded.");
            }


            SaveBitmapHandleAsPng(
                bitmapHandle,
                temporaryPath,
                fullPath,
                cancellationToken);


            return Task.FromResult(
                new NIRAWindowCaptureBackendResult
                {
                    Window =
                        after,

                    CaptureMethod =
                        captureMethod
                });
        }
        finally
        {
            RestoreAndReleaseGdi(
                memoryDc,
                bitmapHandle,
                previousObject);


            if (referenceDc != IntPtr.Zero)
            {
                ReleaseDC(
                    IntPtr.Zero,
                    referenceDc);
            }


            TryDelete(
                temporaryPath);
        }
    }


    // =========================================================
    // PNG OUTPUT
    // =========================================================

    private static string PrepareDestination(
        string destinationPath,
        out string temporaryPath)
    {
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


        temporaryPath =
            fullPath +
            ".tmp-" +
            Guid.NewGuid().ToString("N");


        return fullPath;
    }


    private static void SaveBitmapHandleAsPng(
        IntPtr bitmapHandle,
        string temporaryPath,
        string fullPath,
        CancellationToken cancellationToken)
    {
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
    }


    private static void RestoreAndReleaseGdi(
        IntPtr memoryDc,
        IntPtr bitmapHandle,
        IntPtr previousObject)
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
    }


    // =========================================================
    // WINDOW METADATA
    // =========================================================

    private static string ReadProcessName(
        uint processId)
    {
        try
        {
            using Process process =
                Process.GetProcessById(
                    unchecked((int)processId));


            return process.ProcessName
                ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }


    private static string ReadWindowTitle(
        IntPtr windowHandle)
    {
        int length =
            GetWindowTextLength(
                windowHandle);


        if (length <= 0)
        {
            return string.Empty;
        }


        StringBuilder builder =
            new(
                length + 1);


        _ =
            GetWindowText(
                windowHandle,
                builder,
                builder.Capacity);


        return builder.ToString();
    }


    private static string ReadWindowClassName(
        IntPtr windowHandle)
    {
        StringBuilder builder =
            new(512);


        int length =
            GetClassName(
                windowHandle,
                builder,
                builder.Capacity);


        return length <= 0
            ? string.Empty
            : builder.ToString();
    }


    private static void TryDelete(
        string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }


    private static Win32Exception LastWin32(
        string message) =>
        new(
            Marshal.GetLastWin32Error(),
            message);


    // =========================================================
    // USER32
    // =========================================================

    [DllImport(
        "user32.dll")]
    private static extern IntPtr GetForegroundWindow();


    [UnmanagedFunctionPointer(
        CallingConvention.Winapi)]
    private delegate bool EnumWindowsProc(
        IntPtr windowHandle,
        IntPtr parameter);


    [DllImport(
        "user32.dll",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(
        EnumWindowsProc callback,
        IntPtr parameter);


    [DllImport(
        "user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(
        IntPtr windowHandle);


    [DllImport(
        "user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(
        IntPtr windowHandle);


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
        "user32.dll",
        SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(
        IntPtr windowHandle,
        out uint processId);


    [DllImport(
        "user32.dll",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(
        IntPtr windowHandle,
        out NativeRect rectangle);


    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern int GetWindowTextLength(
        IntPtr windowHandle);


    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern int GetWindowText(
        IntPtr windowHandle,
        StringBuilder text,
        int count);


    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern int GetClassName(
        IntPtr windowHandle,
        StringBuilder className,
        int maxCount);


    [DllImport(
        "user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(
        IntPtr windowHandle);


    [DllImport(
        "user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(
        IntPtr windowHandle);


    [DllImport(
        "user32.dll",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(
        IntPtr windowHandle,
        IntPtr destinationDc,
        uint flags);


    // =========================================================
    // GDI32
    // =========================================================

    [DllImport(
        "gdi32.dll",
        SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(
        IntPtr deviceContext);


    [DllImport(
        "gdi32.dll",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
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
    [return: MarshalAs(UnmanagedType.Bool)]
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


    [StructLayout(
        LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;

        public int Top;

        public int Right;

        public int Bottom;
    }
}

