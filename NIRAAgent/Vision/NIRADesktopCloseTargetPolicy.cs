using System;
using System.Collections.Generic;
using System.Linq;

namespace NIRAAgent.Vision;

// Pure, testable, app-independent target selection. Never close a 
// guessed highest-scoring or foreground window when a selector is ambiguous.
public static class NIRADesktopCloseTargetPolicy
{
    public static NIRACaptureWindowSnapshot SelectUnique(
        IEnumerable<NIRACaptureWindowSnapshot> liveWindows,
        string processName, string? titleContains = null)
    {
        ArgumentNullException.ThrowIfNull(liveWindows);
        string name = (processName ?? string.Empty).Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A grounded process name is required.");
        NIRACaptureWindowSnapshot[] matches = liveWindows
            .Where(w => w.Handle > 0 && w.ProcessId > 0 &&
                string.Equals(w.ProcessName, name,
                    StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(titleContains) ||
                 w.Title.Contains(titleContains, StringComparison.OrdinalIgnoreCase)))
            .GroupBy(w => w.Handle)
            .Select(g => g.First())
            .Take(2).ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException(
                "WINDOW_SELECTION_NOT_UNIQUE: A unique live top-level window " +
                "was not established. No close was dispatched; inspect exact HWNDs first.");
        return matches[0];
    }
}
