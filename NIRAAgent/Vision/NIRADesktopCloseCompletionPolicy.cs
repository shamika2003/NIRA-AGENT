using NIRAAgent.Capabilities;

namespace NIRAAgent.Vision;

// A process.start PID may be a short-lived launcher or an app activation
// proxy. It is not authoritative evidence identifying the final GUI HWND.
// Only current OS readback is evidence that a named application's windows
// and processes have gone. No model-written summary is trusted here.
public static class NIRADesktopCloseCompletionPolicy
{
    public static bool ProvesApplicationGone(bool targetWindowGone,
        bool? targetProcessAlive, int? sameNameWindowCount,
        int? sameNameProcessCount) =>
        targetWindowGone && targetProcessAlive == false &&
        sameNameWindowCount == 0 && sameNameProcessCount == 0;

    public static bool IsVerifiedReceipt(NIRACapabilityResult result) =>
        result.CapabilityId == NIRACapabilityIds.DesktopClose &&
        result.Status == NIRACapabilityResultStatus.Succeeded &&
        !result.OutcomeUncertain &&
        HasLine(result.Output, "OriginalWindowGone: True") &&
        HasLine(result.Output, "OriginalProcessAlive: False") &&
        HasLine(result.Output, "RemainingMatchingProcesses: 0") &&
        HasLine(result.Output, "RemainingMatchingWindows: 0") &&
        HasLine(result.Output, "CloseObjectiveVerified: True");

    private static bool HasLine(string output, string expected) =>
        output.Split('\n').Any(line => line.Trim().Equals(expected, StringComparison.OrdinalIgnoreCase));

    public static string DescribeVerifiedReceipt(NIRACapabilityResult result)
    {
        if (!IsVerifiedReceipt(result)) return string.Empty;
        string name = result.Output.Split('\n')
            .FirstOrDefault(line => line.StartsWith("ProcessName: ", StringComparison.OrdinalIgnoreCase))?
            .Substring("ProcessName: ".Length).Trim() ?? "the application";
        return $"TRUSTED WINDOW-CLOSE POSTCONDITION: {name} had one verified live target " +
            "and that target closed. A fresh OS read found zero remaining same-name " +
            "top-level windows and processes. The application-close portion is VERIFIED. " +
            "Any process.start PID reported earlier can be a transient launcher, " +
            "not the owner of the top-level HWND. Do not demand equality with an " +
            "earlier launch PID or re-run a close for a process that is already absent. " +
            "Check any OTHER requested user objectives separately.";
    }
}
