using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using NIRAAgent.Vision;

namespace NIRAAgent.UI.Vision;

// A different observation modality than PrintWindow/BitBlt. Read-only UI
// Automation can return selected navigation controls for windows whose
// surface cannot be rendered or is currently minimized. Works generically
// for applications exposing UIA, without claiming every app supports it.
public sealed class WindowsAccessibilityBackend : INIRADesktopAccessibilityBackend
{
    public async Task<NIRADesktopAccessibilityResult> InspectAsync(
        IntPtr windowHandle, int expectedProcessId,
        CancellationToken cancellationToken = default)
    {
        if (windowHandle == IntPtr.Zero || expectedProcessId <= 0 ||
            expectedProcessId == Environment.ProcessId)
            throw new InvalidOperationException("An external HWND and PID are required.");

        // UIA providers may be remote or temporarily unresponsive. This
        // operation is bounded for the caller and never traverses the desktop.
        return await Task.Run(() => ReadTree(windowHandle, expectedProcessId, cancellationToken),
            cancellationToken).WaitAsync(TimeSpan.FromSeconds(6), cancellationToken);
    }

    private static NIRADesktopAccessibilityResult ReadTree(
        IntPtr hwnd, int pid, CancellationToken token)
    {
        if (!IsWindow(hwnd))
            throw new InvalidOperationException("The target HWND no longer exists.");
        _ = GetWindowThreadProcessId(hwnd, out uint nativePid);
        if (nativePid != (uint)pid)
            throw new InvalidOperationException("Target HWND changed process identity.");
        AutomationElement root = AutomationElement.FromHandle(hwnd)
            ?? throw new InvalidOperationException("Windows UI Automation cannot inspect the selected HWND.");
        StringBuilder output = new();
        Queue<(AutomationElement Node, int Depth)> todo = new();
        todo.Enqueue((root, 0));
        int examined = 0;
        const int limit = 220;
        const int charLimit = 14500;
        TreeWalker walker = TreeWalker.ControlViewWalker;
        while (todo.Count > 0 && examined < limit && output.Length < charLimit)
        {
            token.ThrowIfCancellationRequested();
            var (node, depth) = todo.Dequeue();
            examined++;
            try
            {
                string kind = node.Current.ControlType?.ProgrammaticName ?? "Unknown";
                string name = Clean(node.Current.Name, 180);
                string autoId = Clean(node.Current.AutomationId, 120);
                bool selected = false;
                bool hasSelection = false;
                if (node.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object pattern) &&
                    pattern is SelectionItemPattern selection)
                {
                    selected = selection.Current.IsSelected;
                    hasSelection = true;
                }
                if (name.Length > 0 || autoId.Length > 0 || hasSelection)
                    output.Append("Depth=").Append(depth).Append(" | Control=").Append(kind)
                        .Append(" | Name='").Append(name).Append("' | AutomationId='")
                        .Append(autoId).Append('\'')
                        .Append(hasSelection ? " | IsSelected=" + selected : "")
                        .AppendLine();
                if (depth >= 12) continue;
                AutomationElement? child = walker.GetFirstChild(node);
                int siblings = 0;
                while (child != null && siblings++ < 90 && todo.Count < limit)
                {
                    todo.Enqueue((child, depth + 1));
                    child = walker.GetNextSibling(child);
                }
            }
            catch (ElementNotAvailableException) { }
            catch (InvalidOperationException) { }
            catch (COMException) { }
        }
        if (!IsWindow(hwnd))
            throw new InvalidOperationException("The target window disappeared during accessibility inspection.");
        _ = GetWindowThreadProcessId(hwnd, out nativePid);
        if (nativePid != (uint)pid)
            throw new InvalidOperationException("The target process changed during accessibility inspection.");
        if (output.Length == 0)
            throw new InvalidOperationException("The selected application exposed no readable UI Automation controls.");
        return new NIRADesktopAccessibilityResult
        {
            ControlTree = output.ToString(),
            ElementsRead = examined,
            Truncated = todo.Count != 0 || output.Length >= charLimit
        };
    }

    private static string Clean(string? value, int limit)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        string clean = value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
        return clean.Length > limit ? clean[..limit] : clean;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
}
