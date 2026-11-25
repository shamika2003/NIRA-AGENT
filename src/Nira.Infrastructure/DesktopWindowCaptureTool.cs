using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nira.Core;

namespace Nira.Infrastructure;

public sealed record DesktopWindowCapture(DesktopWindow Window, byte[] Png, int Width, int Height);

public interface IDesktopWindowCaptureBackend
{
    DesktopWindowCapture Capture(string windowId, string title);
}

/// <summary>Explicitly approved window-only capture. No screen-wide fallback or automatic upload.</summary>
public sealed class DesktopWindowCaptureTool(WorkspacePathResolver paths, IDesktopWindowCaptureBackend backend)
    : IAgentTool, IToolEvidenceRequirement
{
    public const int MaximumBytes = 12 * 1024 * 1024;
    public ToolDescriptor Descriptor { get; } = new("desktop.window_capture",
        "Capture one exact observed application window as a local PNG (may contain private visible content). " +
        "Requires exact observed windowId and title (not an invented description) and explicit approval. Returns a local PNG for chat preview, OCR or separately approved vision. " +
        "May fail for minimized, protected or unsupported windows. No whole-screen capture or upload.",
        ToolRisk.Dangerous, JsonToolSchema.Schema("""
        {"type":"object","properties":{"windowId":{"type":"string","minLength":8,"maxLength":100},
          "title":{"type":"string","minLength":1,"maxLength":180}},"required":["windowId","title"],"additionalProperties":false}
        """), HasSideEffects: true);

    public string GetTarget(JsonElement args) => "Capture pixels of window: " +
        args.GetProperty("title").GetString() + " (" + DesktopToolInput.Window(args) +
        "). Save a local PNG in the workspace. No network transfer.";

    public string? MissingEvidence(ConversationState state, JsonElement args)
    {
        string id = DesktopToolInput.Window(args), title = args.GetProperty("title").GetString()!;
        if (title.Any(char.IsControl)) return "Invalid window title.";
        foreach (var op in state.Operations.AsEnumerable().Reverse())
        {
            if (op.Status != OperationStatus.Succeeded || op.ResultJson is null ||
                op.ToolName is not ("desktop.windows_list" or "desktop.window_inspect")) continue;
            try
            {
                using var doc = JsonDocument.Parse(op.ResultJson);
                var root = doc.RootElement;
                if (!root.GetProperty("Success").GetBoolean()) continue;
                var data = root.GetProperty("Data");
                var windows = op.ToolName == "desktop.windows_list"
                    ? data.GetProperty("windows").EnumerateArray().ToArray() : new[] { data.GetProperty("Window") };
                foreach (var window in windows)
                {
                    if (window.GetProperty("WindowId").GetString() != id) continue;
                    string? observedTitle = window.GetProperty("Title").GetString();
                    if (window.GetProperty("IsMinimized").GetBoolean())
                        return "The observed window is minimized. Obtain a fresh visible-window observation before capture.";
                    return observedTitle == title ? null : "The title must exactly match the recorded window title: " +
                        JsonSerializer.Serialize(observedTitle) + ". Do not invent a description or rename the window in the arguments.";
                }
                // The newest complete window list supersedes older observations.
                if (op.ToolName == "desktop.windows_list") break;
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException) { break; }
        }
        return "Capture requires an exact window ID and title from a successful current-task window listing or inspection.";
    }

    public async Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        if (!JsonToolSchema.Validate(Descriptor.Parameters, args, out string error))
            return ToolReceipt.Fail("ARGUMENT_SCHEMA", error);
        string id = DesktopToolInput.Window(args), title = args.GetProperty("title").GetString()!;
        if (title.Any(char.IsControl)) return ToolReceipt.Fail("CAPTURE_TITLE", "Invalid window title.");
        try
        {
            // PrintWindow is a synchronous native call and cannot be interrupted safely.
            // The background operation only holds memory, never writes files or sends data.
            using var captureDeadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            captureDeadline.CancelAfter(TimeSpan.FromSeconds(8));
            CancellationToken captureToken = captureDeadline.Token;
            Task<DesktopWindowCapture> pending = Task.Run(() => {
                captureToken.ThrowIfCancellationRequested();
                return backend.Capture(id, title);
            }, captureToken);
            _ = pending.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            var capture = await pending.WaitAsync(captureToken);
            ct.ThrowIfCancellationRequested();
            if (capture.Window.WindowId != id || capture.Window.Title != title || capture.Window.IsMinimized ||
                capture.Png.Length is < 24 or > MaximumBytes || !DimensionsAllowed(capture.Width, capture.Height) ||
                !PngHeaderMatches(capture))
                return ToolReceipt.Fail("CAPTURE_INVALID", "The capture did not match the approved target or image limits.");
            string path = paths.Resolve("window-capture-" + Guid.NewGuid().ToString("N") + ".png");
            string hash = Convert.ToHexString(SHA256.HashData(capture.Png)).ToLowerInvariant();
            // A unique temporary file is verified before publishing the final path.
            string temp = paths.Resolve(".nira-capture-" + Guid.NewGuid().ToString("N") + ".tmp");
            bool created = false;
            try
            {
                using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                {
                    created = true;
                    output.Write(capture.Png);
                    output.Flush(flushToDisk: true);
                    output.Position = 0;
                    if (!string.Equals(Convert.ToHexString(SHA256.HashData(output)), hash, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Capture read-back did not match.");
                }
                ct.ThrowIfCancellationRequested();
                File.Move(temp, path, overwrite: false);
                created = false;
            }
            catch
            {
                if (created)
                {
                    try { File.Delete(temp); }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        return ToolReceipt.Ok("A temporary screenshot could not be removed after a failed save. Inspect the local temporary file; no automatic retry.",
                            new { temporaryPath = temp }, changedState: true, recovery: ToolRecovery.UnverifiedEffect);
                    }
                }
                throw;
            }
            return ToolReceipt.Ok("Captured and saved the approved window as a locally verified PNG. Pixels have not been interpreted or uploaded.",
                new { path, sha256 = hash, capture.Window, capture.Width, capture.Height, bytes = capture.Png.Length,
                    capturedUtc = DateTimeOffset.UtcNow, contentVerified = true, visuallyInterpreted = false, uploaded = false }, changedState: true);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return ToolReceipt.Fail("CAPTURE_TIMEOUT", "Window capture timed out. No screenshot was saved. A blocked native capture may still be draining."); }
        catch (DesktopAutomationException e)
        {
            // The native backend failed before any file was published. Let the model
            // reobserve the target through the existing recovery contract; no auto-replay.
            return ToolReceipt.Fail(e.Code, e.Message, recovery: ToolRecovery.Reobserve);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or COMException)
        { return ToolReceipt.Fail("CAPTURE_FAILED", "Could not capture or save the window (" + e.GetType().Name + "). No verified screenshot is available."); }
    }

    private static bool PngHeaderMatches(DesktopWindowCapture capture)
    {
        return TryReadPngDimensions(capture.Png, out int width, out int height) &&
            width == capture.Width && height == capture.Height;
    }

    public static bool TryReadPngDimensions(ReadOnlySpan<byte> png, out int width, out int height)
    {
        width = height = 0;
        if (png.Length is < 24 or > MaximumBytes ||
            !png[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
            BinaryPrimitives.ReadUInt32BigEndian(png[8..12]) != 13 || !png[12..16].SequenceEqual("IHDR"u8)) return false;
        width = BinaryPrimitives.ReadInt32BigEndian(png[16..20]);
        height = BinaryPrimitives.ReadInt32BigEndian(png[20..24]);
        return DimensionsAllowed(width, height);
    }

    public static bool DimensionsAllowed(int width, int height) => width is > 0 and <= 8000 &&
        height is > 0 and <= 8000 && (long)width * height <= 24000000;
}

public sealed partial class WindowsDesktopAutomationBackend : IDesktopWindowCaptureBackend
{
    private static int _captureBusy;

    public DesktopWindowCapture Capture(string windowId, string title)
    {
        if (Interlocked.CompareExchange(ref _captureBusy, 1, 0) != 0)
            throw new DesktopAutomationException("CAPTURE_BUSY", "A previous native window capture has not finished.");
        try { return CaptureWindow(windowId, title); }
        finally { Volatile.Write(ref _captureBusy, 0); }
    }

    private static DesktopWindowCapture CaptureWindow(string windowId, string title)
    {
        var window = ResolveWindow(windowId);
        if (window.Info.Title != title || window.Info.IsMinimized || !GetWindowRect(window.Handle, out var rect))
            throw new DesktopAutomationException("CAPTURE_STALE", "The selected window changed, is minimized, or has no usable bounds. No screenshot saved.");
        // Respect any detectable window capture protection; do not attempt a bypass.
        if (GetWindowDisplayAffinity(window.Handle, out uint affinity) && affinity != 0)
            throw new DesktopAutomationException("CAPTURE_PROTECTED", "Windows marks this window as protected from capture.");
        long w = (long)rect.Right - rect.Left, h = (long)rect.Bottom - rect.Top;
        if (w > int.MaxValue || h > int.MaxValue || w <= 0 || h <= 0 || !DesktopWindowCaptureTool.DimensionsAllowed((int)w, (int)h))
            throw new DesktopAutomationException("CAPTURE_SIZE", "Window dimensions exceed capture limits.");
        int width = (int)w, height = (int)h;
        IntPtr screen = IntPtr.Zero, dc = IntPtr.Zero, bitmap = IntPtr.Zero, previous = IntPtr.Zero;
        try
        {
            // The screen DC is used only for a compatible pixel format, never as a pixel source.
            screen = GetDC(IntPtr.Zero);
            if (screen != IntPtr.Zero) dc = CreateCompatibleDC(screen);
            if (dc != IntPtr.Zero) bitmap = CreateCompatibleBitmap(screen, width, height);
            if (bitmap != IntPtr.Zero) previous = SelectObject(dc, bitmap);
            if (previous == IntPtr.Zero || previous == new IntPtr(-1) || !PatBlt(dc, 0, 0, width, height, 0x00000042))
                throw new DesktopAutomationException("CAPTURE_SURFACE", "Windows could not allocate a window capture surface.");
            // Only the target HWND. Never copy the desktop or another overlapping window.
            if (ResolveWindow(windowId).Handle != window.Handle || IsIconic(window.Handle) || !PrintWindow(window.Handle, dc, 2))
                throw new DesktopAutomationException("CAPTURE_UNSUPPORTED", "Windows did not render the selected window. No desktop-wide fallback was attempted.");
            var current = ResolveWindow(windowId);
            if (current.Handle != window.Handle || current.Info.IsMinimized || current.Info.Title != title ||
                !GetWindowRect(window.Handle, out var after) || after.Right - (long)after.Left != width || after.Bottom - (long)after.Top != height)
                throw new DesktopAutomationException("CAPTURE_STALE", "The window changed during capture. The image was discarded.");
            // Deselect before extracting pixels and keep no native bitmap reference in the receipt.
            _ = SelectObject(dc, previous); previous = IntPtr.Zero;
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgr24, null, 0);
            int stride = checked(width * 3);
            byte[] pixels = new byte[checked(stride * height)];
            converted.CopyPixels(pixels, stride, 0);
            bool varied = false;
            for (int i = 3; i < pixels.Length; i += 3)
                if (pixels[i] != pixels[0] || pixels[i + 1] != pixels[1] || pixels[i + 2] != pixels[2]) { varied = true; break; }
            if (!varied) throw new DesktopAutomationException("CAPTURE_BLANK", "The application returned a uniform image; capture may be blocked or unsupported.");
            var clean = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null, pixels, stride);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(clean));
            using var stream = new MemoryStream(); encoder.Save(stream);
            if (stream.Length > DesktopWindowCaptureTool.MaximumBytes)
                throw new DesktopAutomationException("CAPTURE_SIZE", "Captured PNG exceeds 12 MiB. No screenshot saved.");
            return new DesktopWindowCapture(current.Info, stream.ToArray(), width, height);
        }
        finally
        {
            if (previous != IntPtr.Zero && previous != new IntPtr(-1)) _ = SelectObject(dc, previous);
            if (bitmap != IntPtr.Zero) _ = DeleteObject(bitmap);
            if (dc != IntPtr.Zero) _ = DeleteDC(dc);
            if (screen != IntPtr.Zero) _ = ReleaseDC(IntPtr.Zero, screen);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CaptureRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out CaptureRect rect);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("user32.dll")] private static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool PatBlt(IntPtr dc, int x, int y, int width, int height, uint rop);
}
