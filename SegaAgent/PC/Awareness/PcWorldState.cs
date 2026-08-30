/*
 * filename: PcWorldState.cs
 */

namespace SegaAgent.PC.Awareness;


// =============================================================
// PC WORLD STATE
//
// This is Sega's current local snapshot of the Windows
// environment.
//
// It contains observations only.
//
// It does NOT make decisions.
// It does NOT call the AI.
// It does NOT perform actions.
// =============================================================

public sealed class PcWorldState
{
    // =========================================================
    // TIME
    // =========================================================

    public DateTime Timestamp
    {
        get;
        init;
    }


    // =========================================================
    // USER
    // =========================================================

    public PcUserState User
    {
        get;
        init;
    } = new();


    // =========================================================
    // MOUSE
    // =========================================================

    public PcMouseState Mouse
    {
        get;
        init;
    } = new();


    // =========================================================
    // FOREGROUND WINDOW
    // =========================================================

    public PcForegroundWindowState ForegroundWindow
    {
        get;
        init;
    } = new();


    // =========================================================
    // DISPLAY
    // =========================================================

    public PcDisplayState Display
    {
        get;
        init;
    } = new();
}


// =============================================================
// USER STATE
// =============================================================

public sealed class PcUserState
{
    public TimeSpan IdleTime
    {
        get;
        init;
    }
}


// =============================================================
// MOUSE STATE
// =============================================================

public sealed class PcMouseState
{
    public int X
    {
        get;
        init;
    }


    public int Y
    {
        get;
        init;
    }
}


// =============================================================
// FOREGROUND WINDOW STATE
// =============================================================

public sealed class PcForegroundWindowState
{
    // =========================================================
    // NATIVE WINDOW
    //
    // Keep the handle inside the world model because future
    // Windows tools will need it.
    //
    // We do NOT expose it to the language model in the
    // formatted context.
    // =========================================================

    public IntPtr Handle
    {
        get;
        init;
    }


    // =========================================================
    // PROCESS
    // =========================================================

    public int ProcessId
    {
        get;
        init;
    }


    public string ProcessName
    {
        get;
        init;
    } = string.Empty;


    // =========================================================
    // WINDOW
    // =========================================================

    public string Title
    {
        get;
        init;
    } = string.Empty;


    public string ClassName
    {
        get;
        init;
    } = string.Empty;


    public PcRectangle Bounds
    {
        get;
        init;
    }


    // =========================================================
    // WINDOW STATE
    // =========================================================

    public bool IsMinimized
    {
        get;
        init;
    }


    public bool IsMaximized
    {
        get;
        init;
    }


    public bool IsFullscreen
    {
        get;
        init;
    }


    // =========================================================
    // VALID
    // =========================================================

    public bool IsValid =>
        Handle != IntPtr.Zero;
}


// =============================================================
// DISPLAY STATE
// =============================================================

public sealed class PcDisplayState
{
    // =========================================================
    // PHYSICAL MONITOR AREA
    // =========================================================

    public PcRectangle MonitorBounds
    {
        get;
        init;
    }


    // =========================================================
    // USABLE WORK AREA
    //
    // Normally excludes the Windows taskbar.
    // =========================================================

    public PcRectangle WorkArea
    {
        get;
        init;
    }


    // =========================================================
    // PRIMARY
    // =========================================================

    public bool IsPrimary
    {
        get;
        init;
    }
}


// =============================================================
// RECTANGLE
// =============================================================

public readonly record struct PcRectangle(
    int Left,
    int Top,
    int Right,
    int Bottom)
{
    public int Width =>
        Math.Max(
            0,
            Right - Left);


    public int Height =>
        Math.Max(
            0,
            Bottom - Top);


    public bool IsEmpty =>
        Width <= 0 ||
        Height <= 0;


    public override string ToString()
    {
        return
            $"X={Left}, Y={Top}, " +
            $"Width={Width}, Height={Height}";
    }
}