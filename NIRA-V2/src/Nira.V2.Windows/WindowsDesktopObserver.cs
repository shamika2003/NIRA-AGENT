using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Nira.V2.Windows;

/// <summary>
/// Privacy-bounded observation of the interactive Windows desktop. The contract
/// deliberately cannot carry a window title, document name, PID, path, pixel, or
/// process command line. The host must opt in before this provider is exposed.
/// </summary>
public sealed record DesktopWindowSnapshot(DateTimeOffset ObservedUtc,
    IReadOnlyList<string> VisibleApplications, string? ForegroundApplication,
    int ObservedVisibleWindows, bool Truncated);

public interface IWindowsDesktopObserver
{
    Task<DesktopWindowSnapshot> ObserveAsync(CancellationToken cancellationToken);
}

public sealed class Win32DesktopObserver : IWindowsDesktopObserver
{
    private const uint GwOwner = 4;
    private const int MaxWindows = 256;
    private const int MaxApplications = 48; // Capture bound; smaller LLM exposure bound follows.

    public Task<DesktopWindowSnapshot> ObserveAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows desktop observation requires Windows.");
        cancellationToken.ThrowIfCancellationRequested();
        var apps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int windows = 0;
        bool truncated = false;
        IntPtr foreground = GetForegroundWindow();
        string? foregroundName = null;
        bool enumerationSucceeded = EnumWindows((handle, _) =>
        {
            if (cancellationToken.IsCancellationRequested) return false;
            if (!IsWindowVisible(handle) || GetWindow(handle, GwOwner) != IntPtr.Zero)
                return true;
            windows++;
            if (windows > MaxWindows)
            {
                truncated = true;
                return false;
            }
            string? name = TryGetApplicationName(handle);
            if (name is null) return true;
            if (apps.Count < MaxApplications) apps.Add(name);
            else if (!apps.Contains(name)) truncated = true;
            if (handle == foreground) foregroundName = name;
            return true;
        }, IntPtr.Zero);
        cancellationToken.ThrowIfCancellationRequested();
        if (!enumerationSucceeded && !truncated)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to enumerate windows.");
        // The foreground HWND sometimes belongs to an owned window or is absent
        // from top-level enumeration. Still return only its process name.
        foregroundName ??= foreground == IntPtr.Zero ? null : TryGetApplicationName(foreground);
        return Task.FromResult(new DesktopWindowSnapshot(DateTimeOffset.UtcNow,
            apps.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray(),
            foregroundName, Math.Min(windows, MaxWindows), truncated));
    }

    private static string? TryGetApplicationName(IntPtr hwnd)
    {
        _ = GetWindowThreadProcessId(hwnd, out uint processId);
        if (processId is 0 or > int.MaxValue) return null;
        try
        {
            using Process process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return null;
        }
    }

    private delegate bool EnumWindowsCallback(IntPtr handle, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr handle, uint command);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
