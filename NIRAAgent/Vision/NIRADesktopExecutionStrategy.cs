namespace NIRAAgent.Vision;

// A stable, app-independent execution policy. Native capabilities (filesystem,
// process, application APIs, shell) remain in the existing capability registry;
// only UI work reaches this desktop policy.
public enum NIRADesktopExecutionMode
{
    BackgroundAutomation,
    ForegroundInteraction
}

public static class NIRADesktopExecutionStrategy
{
    public static NIRADesktopExecutionMode ForAction(string? action, bool nativeEditorAvailable = false)
    {
        string kind = (action ?? "").Trim().ToLowerInvariant();
        return kind switch
        {
            "invoke" or "select" or "toggle" or "expand" or "collapse" or "setvalue" or "close" =>
                NIRADesktopExecutionMode.BackgroundAutomation,
            "type" when nativeEditorAvailable => NIRADesktopExecutionMode.BackgroundAutomation,
            "activate" or "focus" or "click" or "press" or "type" =>
                NIRADesktopExecutionMode.ForegroundInteraction,
            _ => throw new ArgumentException("Unknown desktop action.", nameof(action))
        };
    }

    public static bool NeedsForeground(string? action) =>
        ForAction(action) == NIRADesktopExecutionMode.ForegroundInteraction;
}

public enum NIRAForegroundLeaseTransition { None, Paused, Resumed }

// Pure, model-free state machine. Input never dispatches while Paused.
// Any caller can test this on Linux without a Windows window handle.
public sealed class NIRAForegroundLeaseState
{
    public bool IsPaused { get; private set; }

    public NIRAForegroundLeaseTransition Observe(bool targetIsForeground)
    {
        if (!targetIsForeground && !IsPaused)
        {
            IsPaused = true;
            return NIRAForegroundLeaseTransition.Paused;
        }
        if (targetIsForeground && IsPaused)
        {
            IsPaused = false;
            return NIRAForegroundLeaseTransition.Resumed;
        }
        return NIRAForegroundLeaseTransition.None;
    }
}
