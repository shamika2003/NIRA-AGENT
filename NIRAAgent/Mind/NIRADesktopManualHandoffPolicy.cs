using NIRAAgent.Capabilities;

namespace NIRAAgent.Mind;

/// <summary>
/// Converts a positively identified operating-system desktop boundary into a
/// truthful, actionable user handoff. This does not bypass authorization or
/// infer that a window/process closed. Ordinary transient failures remain
/// eligible for evidence-based recovery.
/// </summary>
internal static class NIRADesktopManualHandoffPolicy
{
    internal static bool TryDescribeBlockedOperation(
        IReadOnlyList<NIRACapabilityRequest> issued,
        IReadOnlyList<NIRACapabilityResult> completed,
        out string message,
        out string kind)
    {
        message = string.Empty;
        kind = string.Empty;
        // A multi-operation batch can include unrelated completed work. It
        // needs the normal evidence-preserving Executive path, not this early
        // terminal single-operation shortcut.
        if (issued.Count != 1 || completed.Count != 1) return false;
        NIRACapabilityRequest request = issued[0];
        NIRACapabilityResult result = completed[0];
        return TryDescribeDeniedWindowClose(request, result, out message, out kind);
    }

    internal static bool TryDescribeDeniedWindowClose(
        NIRACapabilityRequest request,
        NIRACapabilityResult result,
        out string message,
        out string kind)
    {
        message = string.Empty;
        kind = string.Empty;
        if (!IsCloseRequest(request) || result.CapabilityId != request.CapabilityId ||
            result.Status != NIRACapabilityResultStatus.Failed || result.OutcomeUncertain ||
            !result.Summary.Contains("WINDOW_CLOSE_ACCESS_DENIED:", StringComparison.Ordinal))
            return false;

        kind = "WindowsCloseAccessDenied";
        message = "I found the target window, but Windows denied my normal close request. " +
            "I can't safely close it from here. If it's still open, switch to that " +
            "application and click its X (Close) button. If Windows shows a save " +
            "or confirmation dialog, choose how to proceed yourself. I haven't " +
            "verified that the window or its process exited, and I won't force-kill " +
            "it or bypass Windows security.";
        return true;
    }

    private static bool IsCloseRequest(NIRACapabilityRequest request)
    {
        if (request.CapabilityId == NIRACapabilityIds.DesktopClose) return true;
        if (request.CapabilityId != NIRACapabilityIds.DesktopAct) return false;
        string? action = NIRACapabilityArguments.GetOptionalString(request, "action", 40);
        return string.Equals(action, "close", StringComparison.OrdinalIgnoreCase);
    }
}
