/*
 * filename: PcContextFormatter.cs
 */

namespace NIRAAgent.PC.Awareness;

public static class PcContextFormatter
{
    public static string Format(
        PcWorldState state)
    {
        ArgumentNullException.ThrowIfNull(
            state);


        PcForegroundWindowState window =
            state.ForegroundWindow;


        PcDisplayState display =
            state.Display;


        PcNIRAPresenceState NIRA =
            state.NIRA;


        return $"""
            CURRENT PC WORLD STATE

            Timestamp:
            {state.Timestamp:yyyy-MM-dd HH:mm:ss} UTC

            ==================================================
            USER ACTIVITY
            ==================================================

            Idle Time:
            {state.User.IdleTime.TotalSeconds:F1} seconds

            ==================================================
            MOUSE
            ==================================================

            X:
            {state.Mouse.X}

            Y:
            {state.Mouse.Y}

            ==================================================
            FOREGROUND APPLICATION
            ==================================================

            Process:
            {Normalize(window.ProcessName)}

            Process ID:
            {window.ProcessId}

            Window Title:
            {Normalize(window.Title)}

            Window Class:
            {Normalize(window.ClassName)}

            ==================================================
            FOREGROUND WINDOW GEOMETRY
            ==================================================

            Left:
            {window.Bounds.Left}

            Top:
            {window.Bounds.Top}

            Width:
            {window.Bounds.Width}

            Height:
            {window.Bounds.Height}

            Minimized:
            {window.IsMinimized}

            Maximized:
            {window.IsMaximized}

            Fullscreen:
            {window.IsFullscreen}

            ==================================================
            ACTIVE MONITOR
            ==================================================

            Monitor Left:
            {display.MonitorBounds.Left}

            Monitor Top:
            {display.MonitorBounds.Top}

            Monitor Width:
            {display.MonitorBounds.Width}

            Monitor Height:
            {display.MonitorBounds.Height}

            Work Area Left:
            {display.WorkArea.Left}

            Work Area Top:
            {display.WorkArea.Top}

            Work Area Width:
            {display.WorkArea.Width}

            Work Area Height:
            {display.WorkArea.Height}

            Primary Monitor:
            {display.IsPrimary}

            ==================================================
            NIRA PHYSICAL PRESENCE
            ==================================================

            Available:
            {NIRA.IsAvailable}

            Visible:
            {NIRA.IsVisible}

            Faded:
            {NIRA.IsFaded}

            Body Left:
            {NIRA.BodyBounds.Left}

            Body Top:
            {NIRA.BodyBounds.Top}

            Body Width:
            {NIRA.BodyBounds.Width}

            Body Height:
            {NIRA.BodyBounds.Height}

            NIRA Monitor Left:
            {NIRA.Display.MonitorBounds.Left}

            NIRA Monitor Top:
            {NIRA.Display.MonitorBounds.Top}

            NIRA Monitor Width:
            {NIRA.Display.MonitorBounds.Width}

            NIRA Monitor Height:
            {NIRA.Display.MonitorBounds.Height}

            Mouse Over NIRA:
            {NIRA.IsMouseOverBody}

            Mouse Distance From NIRA:
            {NIRA.MouseDistanceFromBodyCenter:F1} pixels

            Same Monitor As Foreground:
            {NIRA.SharesMonitorWithForeground}

            Overlapping Foreground Window:
            {NIRA.OverlapsForegroundWindow}

            Foreground Overlap:
            {NIRA.ForegroundOverlapRatio:P1}

            Overlapping Fullscreen Content:
            {NIRA.OverlapsFullscreenContent}
            """;
    }


    // =========================================================
    // NORMALIZE
    // =========================================================

    private static string Normalize(
        string? value)
    {
        return string.IsNullOrWhiteSpace(
                value)
            ? "Unknown"
            : value.Trim();
    }
}

