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
        bool allowForegroundRecovery,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();


        NIRACaptureWindowSnapshot before =
            ReadWindow(
                windowHandle);


        // Restoring a minimized HWND and obtaining foreground focus are different
        // Windows operations. The user can authorize a temporary restore without
        // Windows granting SetForegroundWindow. Try actual rendering after restore.
        bool initiallyMinimized = before.IsMinimized;
        if (initiallyMinimized && !allowForegroundRecovery)
            throw new InvalidOperationException(
                "The selected window is minimized and foreground recovery was disabled.");

        try
        {
            if (initiallyMinimized)
            {
                if (!TryRestoreMinimizedTarget(windowHandle, before.ProcessId, cancellationToken))
                    throw new InvalidOperationException(
                        "Windows refused to restore the selected minimized window. " +
                        "No other window's pixels were captured. An accessibility-tree " +
                        "inspection may still be possible without changing its visibility.");
                before = ReadWindow(windowHandle);
            }
            return CaptureVisibleWindowPngAsync(windowHandle, before, destinationPath,
                allowForegroundRecovery, cancellationToken);
        }
        finally
        {
            // Preserve the user's originally minimized workspace. No arbitrary
            // application is launched, closed or granted elevated authority.
            if (initiallyMinimized && IsWindow(windowHandle) && !IsIconic(windowHandle))
                _ = ShowWindowAsync(windowHandle, SwMinimize);
        }
    }

    private Task<NIRAWindowCaptureBackendResult> CaptureVisibleWindowPngAsync(
        IntPtr windowHandle,
        NIRACaptureWindowSnapshot before,
        string destinationPath,
        bool allowForegroundRecovery,
        CancellationToken cancellationToken)
    {
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
                // A verified foreground BitBlt is the simplest compatible
                // fallback. Windows may refuse focus; that must NOT prevent a
                // different, temporarily exposed HWND capture from being tried.
                bool alreadyForeground = GetForegroundWindow() == windowHandle;
                bool acquiredForeground = alreadyForeground ||
                    (allowForegroundRecovery &&
                     TryBringTargetToForeground(windowHandle, before.ProcessId,
                         restoreMinimized: false, cancellationToken: cancellationToken));

                if (acquiredForeground)
                {
                    NIRACaptureWindowSnapshot focused = ReadWindow(windowHandle);
                    if (!IsMostlyOnScreen(focused.Bounds) ||
                        !NIRAWindowCaptureIdentityPolicy.SameRenderableWindow(before, focused) ||
                        GetForegroundWindow() != windowHandle)
                        throw new InvalidOperationException(
                            "The selected window was not verified as foreground, fully " +
                            "on-screen and unchanged. Unrelated pixels were not used.");

                    CapturePngAsync(focused.Bounds, fullPath, cancellationToken)
                        .GetAwaiter().GetResult();
                    NIRACaptureWindowSnapshot verified = ReadWindow(windowHandle);
                    if (GetForegroundWindow() != windowHandle ||
                        verified.Bounds != focused.Bounds ||
                        !NIRAWindowCaptureIdentityPolicy.SameRenderableWindow(focused, verified))
                    {
                        TryDelete(fullPath);
                        throw new InvalidOperationException(
                            "The selected window changed while its screen pixels were captured.");
                    }
                    return Task.FromResult(new NIRAWindowCaptureBackendResult
                    {
                        Window = verified,
                        CaptureMethod = alreadyForeground
                            ? "VerifiedForegroundBitBlt(PrintWindowFallback)"
                            : "VerifiedTargetActivationBitBlt(PrintWindowFallback)"
                    });
                }

                if (!allowForegroundRecovery)
                    throw new InvalidOperationException(
                        "The selected HWND rejected background rendering. Foreground " +
                        "recovery was disabled; no unrelated window was captured.");

                // Second native path: Windows can refuse foreground focus while
                // still allowing an ordinary application's window to appear on
                // the desktop. Temporarily expose THIS HWND without activation,
                // verify physical hit-testing across its surface, capture the
                // pixels, then restore its topmost state even on failure.
                return CaptureVerifiedExposedWindow(windowHandle, before,
                    fullPath, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();


            NIRACaptureWindowSnapshot after =
                ReadWindow(
                    windowHandle);


            if (!NIRAWindowCaptureIdentityPolicy.SameRenderableWindow(before, after))
            {
                throw new InvalidOperationException(
                    "The requested window changed identity or minimized during capture, so the image was discarded.");
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


    private static bool TryRestoreMinimizedTarget(
        IntPtr handle, int expectedProcessId, CancellationToken cancellationToken)
    {
        if (!IsWindow(handle) || expectedProcessId <= 0 ||
            expectedProcessId == Environment.ProcessId)
            return false;
        _ = ShowWindowAsync(handle, SwRestore);
        for (int i = 0; i < 8; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Thread.Sleep(85);
            if (!IsWindow(handle)) return false;
            _ = GetWindowThreadProcessId(handle, out uint pid);
            if (pid != (uint)expectedProcessId) return false;
            if (!IsIconic(handle) && IsWindowVisible(handle)) return true;
        }
        return false;
    }

    private bool IsMostlyOnScreen(PcRectangle bounds)
    {
        PcRectangle desktop = VirtualScreenBounds;
        if (bounds.IsEmpty) return false;
        // DWM often adds invisible resize margins (-8px at the top/left)
        // to maximized windows. Accept a mostly visible target; its sample
        // points still have to hit this exact HWND before/after BitBlt.
        int l = Math.Max(bounds.Left, desktop.Left);
        int t = Math.Max(bounds.Top, desktop.Top);
        int r = Math.Min(bounds.Right, desktop.Right);
        int b = Math.Min(bounds.Bottom, desktop.Bottom);
        if (r <= l || b <= t) return false;
        double shown = (double)(r - l) * (b - t);
        double total = (double)bounds.Width * bounds.Height;
        return shown / total >= 0.90;
    }

    private Task<NIRAWindowCaptureBackendResult> CaptureVerifiedExposedWindow(
        IntPtr handle, NIRACaptureWindowSnapshot before,
        string fullPath, CancellationToken cancellationToken)
    {
        const uint flags = SwpNoMove | SwpNoSize | SwpNoActivate;
        bool alreadyTopmost = (GetWindowLong(handle, GwlExStyle) & WsExTopmost) != 0;
        bool raised = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsWindowVisible(handle) || IsIconic(handle) ||
                before.ProcessId == Environment.ProcessId || !IsMostlyOnScreen(before.Bounds))
                throw new InvalidOperationException(
                    "The selected window is not independently observable on the current desktop.");

            raised = SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0, flags);
            if (!raised)
                throw LastWin32("Windows refused to expose the selected window for a verified capture.");
            Thread.Sleep(170); // Allow the desktop compositor to redraw.
            cancellationToken.ThrowIfCancellationRequested();
            NIRACaptureWindowSnapshot exposed = ReadWindow(handle);
            if (!NIRAWindowCaptureIdentityPolicy.SameRenderableWindow(before, exposed) ||
                exposed.Bounds != before.Bounds || !IsMostlyOnScreen(exposed.Bounds) ||
                !WindowSurfaceBelongsToTarget(handle, exposed.Bounds))
                throw new InvalidOperationException(
                    "The selected window did not cover its verified screen rectangle. " +
                    "Desktop pixels were not attributed to that application.");

            CapturePngAsync(exposed.Bounds, fullPath, cancellationToken)
                .GetAwaiter().GetResult();
            NIRACaptureWindowSnapshot verified = ReadWindow(handle);
            if (!NIRAWindowCaptureIdentityPolicy.SameRenderableWindow(exposed, verified) ||
                verified.Bounds != exposed.Bounds ||
                !WindowSurfaceBelongsToTarget(handle, verified.Bounds))
            {
                TryDelete(fullPath);
                throw new InvalidOperationException(
                    "The selected window was obscured or changed during capture; image discarded.");
            }
            return Task.FromResult(new NIRAWindowCaptureBackendResult
            {
                Window = verified,
                CaptureMethod = "VerifiedTemporaryExposureBitBlt(PrintWindowFallback)"
            });
        }
        finally
        {
            if (raised && !alreadyTopmost && IsWindow(handle))
                _ = SetWindowPos(handle, HwndNotTopmost, 0, 0, 0, 0, flags);
        }
    }

    private static bool WindowSurfaceBelongsToTarget(IntPtr handle, PcRectangle rect)
    {
        // Verify sampled pixels, not merely that SetWindowPos returned success.
        // Each sample may hit a native child; its root must be the exact HWND.
        int matches = 0;
        foreach (double x in new[] { 0.22, 0.50, 0.78 })
        foreach (double y in new[] { 0.22, 0.50, 0.78 })
        {
            var point = new NativePoint
            {
                X = rect.Left + (int)(rect.Width * x),
                Y = rect.Top + (int)(rect.Height * y)
            };
            IntPtr hit = WindowFromPoint(point);
            if (hit != IntPtr.Zero && GetAncestor(hit, 2) == handle)
                matches++;
        }
        return matches >= 8;
    }

    // A recoverable Windows foreground request is NOT an authorization bypass.
    // SetForegroundWindow can legitimately be refused by the OS. Never synthesize
    // Alt/keyboard input or attach to an unrelated process to force focus.
    private bool TryBringTargetToForeground(
        IntPtr handle, int expectedProcessId, bool restoreMinimized,
        CancellationToken cancellationToken)
    {
        if (handle == IntPtr.Zero || !IsWindow(handle) ||
            expectedProcessId <= 0 || expectedProcessId == Environment.ProcessId)
            return false;

        if (restoreMinimized && IsIconic(handle))
            _ = ShowWindowAsync(handle, SwRestore);

        for (int attempt = 0; attempt < 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                NIRACaptureWindowSnapshot current = ReadWindow(handle);
                if (current.ProcessId != expectedProcessId)
                    return false;
                if (current.IsMinimized || !IsWindowVisible(handle))
                {
                    Thread.Sleep(70);
                    continue;
                }
                if (GetForegroundWindow() == handle)
                    return true;
                _ = SetForegroundWindow(handle);
                if (GetForegroundWindow() == handle)
                {
                    // The target may need a short compositor/UI paint cycle
                    // after Windows acknowledges the foreground transition.
                    Thread.Sleep(100);
                    return GetForegroundWindow() == handle && !IsIconic(handle);
                }
            }
            catch
            {
                return false;
            }
            Thread.Sleep(70);
        }
        return GetForegroundWindow() == handle && !IsIconic(handle);
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


    private const int SwRestore = 9;
    private const int SwMinimize = 6;
    private const int GwlExStyle = -20;
    private const int WsExTopmost = 0x00000008;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNotTopmost = new(-2);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr windowHandle, int index);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter,
        int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr handle, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(IntPtr windowHandle, int showCommand);

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


    [StructLayout(LayoutKind.Sequential)]
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
}
