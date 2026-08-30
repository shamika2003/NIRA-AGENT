/*
 * filename: PcContextFormatter.cs
 */

namespace SegaAgent.PC.Awareness;

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