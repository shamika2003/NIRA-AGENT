using System;
using System.ComponentModel;
using System.Diagnostics;
using NIRAAgent.Vision;

namespace NIRAAgent.Capabilities;

/// <summary>
/// One bounded, general-purpose, non-foreground Windows window-close procedure.
/// Does not terminate processes, select among multiple candidate windows, use
/// app-specific UI scripts, or elevate NIRA. The OS/backend verifies HWND/PID.
/// </summary>
public sealed class NIRADesktopCloseCapabilityHandler : INIRACapabilityHandler
{
    private readonly NIRAVisualEvidenceService _windows;
    private readonly INIRADesktopInteractionBackend _desktop;
    public NIRADesktopCloseCapabilityHandler(
        NIRAVisualEvidenceService windows, INIRADesktopInteractionBackend desktop)
    { _windows = windows; _desktop = desktop; }

    public NIRACapabilityDescriptor Descriptor { get; } = new()
    {
        Id = NIRACapabilityIds.DesktopClose,
        DefaultRisk = NIRACapabilityRisk.Modify,
        Description = "Gracefully close exactly one live external application window by verified process name. " +
            "Discovers its unique top-level HWND internally, uses UI Automation WindowPattern or WM_CLOSE, " +
            "then checks the window and PID. No foreground focus, forced termination, elevation or dialogs are bypassed. " +
            "Multiple windows, absent HWNDs and Windows access denials are reported, not guessed. " +
            "Prefer for ordinary named-application close requests; use desktop.inspect + desktop.act for individual windows when ambiguous.",
        Parameters = new[]
        {
            new NIRACapabilityParameterDescriptor { Name = "processName", Type = "string", Required = true,
                Description = "Exact currently known Windows process name (e.g. from process.list), with or without .exe. Not a guessed window title." },
            new NIRACapabilityParameterDescriptor { Name = "titleContains", Type = "string",
                Description = "Optional current window-title filter, only for narrowing an exact process identity." }
        }
    };
    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest _) => NIRACapabilityRisk.Modify;

    public async Task<NIRACapabilityHandlerResult> ExecuteAsync(
        NIRACapabilityRequest request, CancellationToken cancellationToken = default)
    {
        string processName = NIRACapabilityArguments.RequireString(request, "processName", 260);
        string? titleContains = NIRACapabilityArguments.GetOptionalString(request, "titleContains", 600);
        var w = _windows.ResolveUniqueProcessWindow(processName, titleContains);
        // ResolveUniqueProcessWindow selects exactly one live window, then
        // ActAsync checks HWND and PID again immediately before closing it.
        NIRADesktopActionResult action = await _desktop.ActAsync(
            new IntPtr(w.Handle), w.ProcessId, new NIRADesktopAction { Kind = "close" }, cancellationToken);

        // A Win32 HWND and a process PID are different objects. Verify both.
        bool windowGone = action.PostconditionVerified;
        bool? processAlive = null;
        int? remainingProcesses = null;
        int? remainingWindows = null;
        bool applicationGone = false;
        // A graceful close is asynchronous. Poll local OS state briefly instead
        // of paying for another LLM call or prematurely reporting an active PID.
        for (int attempt = 0; attempt < 7; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            processAlive = ProcessAlive(w.ProcessId);
            remainingProcesses = RemainingProcesses(w.ProcessName);
            try { remainingWindows = _windows.CountLiveProcessWindows(w.ProcessName); }
            catch (InvalidOperationException) { remainingWindows = null; }
            applicationGone = NIRADesktopCloseCompletionPolicy.ProvesApplicationGone(
                windowGone, processAlive, remainingWindows, remainingProcesses);
            if (applicationGone || !windowGone || attempt == 6) break;
            await Task.Delay(120, cancellationToken);
        }
        // WindowPattern proves only window disappearance. Process liveness was
        // separately checked above, so omit the backend's pre-check disclaimer.
        string transportEvidence = action.Evidence.Split(
            "; ProcessExitNotVerified=", StringSplitOptions.None)[0];
        string outcome = $"WindowHandle: 0x{w.Handle:X}\nProcessId: {w.ProcessId}\n" +
            $"ProcessName: {w.ProcessName}\nActionDispatched: {action.ActionDispatched}\n" +
            $"OriginalWindowGone: {windowGone}\n" +
            $"OriginalProcessAlive: {(processAlive.HasValue ? processAlive.Value.ToString() : "Unknown")}\n" +
            $"RemainingMatchingProcesses: {(remainingProcesses.HasValue ? remainingProcesses.Value.ToString() : "Unknown")}\n" +
            $"RemainingMatchingWindows: {(remainingWindows.HasValue ? remainingWindows.Value.ToString() : "Unknown")}\n" +
            $"CloseObjectiveVerified: {applicationGone}\n" + transportEvidence;
        return new NIRACapabilityHandlerResult
        {
            Succeeded = action.ActionDispatched,
            ChangedSystemState = action.ActionDispatched,
            Summary = applicationGone
                ? $"Graceful close verified for unique {w.ProcessName} HWND=0x{w.Handle:X}, PID={w.ProcessId}; " +
                  "target process exited and a fresh check found no matching processes or top-level windows."
                : windowGone
                    ? $"Graceful close verified for unique {w.ProcessName} HWND=0x{w.Handle:X}, PID={w.ProcessId}; " +
                      "other same-name instances may remain or the final application state is unknown."
                    : $"Close requested for {w.ProcessName} HWND=0x{w.Handle:X}, PID={w.ProcessId}; " +
                      "window remains or exit has not been verified; no success claim.",
            Output = outcome
        };
    }

    private static int? RemainingProcesses(string processName)
    {
        string name = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4] : processName;
        try
        {
            Process[] processes = Process.GetProcessesByName(name);
            try { return processes.Count(p => !p.HasExited); }
            finally { foreach (Process p in processes) p.Dispose(); }
        }
        catch (Win32Exception) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    private static bool? ProcessAlive(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return null; }
        catch (Win32Exception) { return null; }
    }
}
