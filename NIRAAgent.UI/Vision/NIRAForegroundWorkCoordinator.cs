using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NIRAAgent.Diagnostics;
using NIRAAgent.Vision;

namespace NIRAAgent.UI.Vision;

// Never fakes keystrokes in another foreground window. A lease follows the
// actual HWND/PID across one or more capability calls. The first foreground
// action may activate the target; after a user switch, it never steals focus
// back automatically. Waiting is local: zero LLM calls, cancellable by the run.
internal sealed class NIRAForegroundWorkCoordinator
{
    private readonly object _gate = new();
    private readonly Dictionary<IntPtr, int> _acquired = new();
    private readonly Dictionary<IntPtr, NIRAForegroundLeaseState> _states = new();

    internal bool AwaitTarget(IntPtr hwnd, int pid, CancellationToken cancellationToken)
    {
        bool first;
        lock (_gate)
        {
            first = !_acquired.TryGetValue(hwnd, out int original) || original != pid;
            if (first)
            {
                _acquired[hwnd] = pid;
                _states[hwnd] = new NIRAForegroundLeaseState();
            }
        }

        CheckWindow(hwnd, pid);
        if (first && GetForegroundWindow() != hwnd)
        {
            if (IsIconic(hwnd)) ShowWindowAsync(hwnd, 9);
            SetForegroundWindow(hwnd); // only the initial activation
        }

        try
        {
            while (GetForegroundWindow() != hwnd)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CheckWindow(hwnd, pid);
                bool announce;
                lock (_gate) announce = _states[hwnd].Observe(false) == NIRAForegroundLeaseTransition.Paused;
                if (announce)
                {
                    NIRADiagnosticTrace.Event("DESKTOP-FOCUS-PAUSED", $"HWND=0x{hwnd.ToInt64():X} PID={pid}");
                    var caption = new StringBuilder(180);
                    GetWindowText(hwnd, caption, caption.Capacity);
                    NIRAForegroundWorkNotice.Display(true, caption.ToString());
                }
                // No UI thread blocking and no model call while awaiting owner focus.
                if (cancellationToken.WaitHandle.WaitOne(180))
                    cancellationToken.ThrowIfCancellationRequested();
            }
            CheckWindow(hwnd, pid);
            bool resumed;
            lock (_gate) resumed = _states[hwnd].Observe(true) == NIRAForegroundLeaseTransition.Resumed;
            if (resumed)
            {
                NIRADiagnosticTrace.Event("DESKTOP-FOCUS-RESUMED", $"HWND=0x{hwnd.ToInt64():X} PID={pid}");
                NIRAForegroundWorkNotice.Display(false);
            }
            return resumed;
        }
        catch
        {
            lock (_gate)
            {
                _states.Remove(hwnd);
                _acquired.Remove(hwnd);
            }
            NIRAForegroundWorkNotice.Display(false);
            throw;
        }
    }

    private static void CheckWindow(IntPtr hwnd, int pid)
    {
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
            throw new InvalidOperationException("The foreground task's target window has closed. Paused work cannot resume.");
        GetWindowThreadProcessId(hwnd, out uint livePid);
        if (livePid != (uint)pid)
            throw new InvalidOperationException("Target HWND was reused by another process. Paused work cannot resume.");
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxLength);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(IntPtr hwnd, int cmd);
}

// A small, non-modal, non-activating warning. It never becomes the foreground
// window itself, so it cannot interfere with the user or the paused worker.
internal static class NIRAForegroundWorkNotice
{
    private static Window? _notice;
    internal static void Display(bool paused, string? windowTitle = null)
    {
        Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted) return;
        dispatcher.BeginInvoke(new Action(() =>
        {
            if (paused)
            {
                if (_notice is null)
                {
                    var text = new TextBlock
                    {
                        Text = "NIRA paused desktop control. Bring the working application to the foreground to resume.",
                        Foreground = Brushes.White,
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 13
                    };
                    var border = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(22, 27, 40)),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(60, 71, 91)),
                        BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
                        Padding = new Thickness(18, 14, 18, 14), Child = text
                    };
                    _notice = new Window
                    {
                        Title = "NIRA — Waiting for application focus",
                        WindowStyle = WindowStyle.None,
                        AllowsTransparency = true,
                        Background = Brushes.Transparent,
                        ShowActivated = false,
                        ShowInTaskbar = false,
                        Topmost = true,
                        ResizeMode = ResizeMode.NoResize,
                        Width = 390, Height = 112,
                        Content = border
                    };
                    _notice.Closed += (_, _) => _notice = null;
                }
                if (_notice.Content is Border noticeBorder &&
                    noticeBorder.Child is TextBlock noticeText)
                {
                    string caption = string.IsNullOrWhiteSpace(windowTitle) ? "the working application"
                        : windowTitle.Length > 90 ? windowTitle[..90] + "…" : windowTitle;
                    noticeText.Text = "NIRA is waiting for " + caption +
                        ". Bring that window to the foreground to resume; you can keep using other apps.";
                }
                Rect area = SystemParameters.WorkArea;
                _notice.Left = area.Right - _notice.Width - 20;
                _notice.Top = area.Bottom - _notice.Height - 20;
                if (!_notice.IsVisible) _notice.Show();
            }
            else if (_notice is not null)
            {
                _notice.Close();
                _notice = null;
            }
        }), DispatcherPriority.Background);
    }
}
