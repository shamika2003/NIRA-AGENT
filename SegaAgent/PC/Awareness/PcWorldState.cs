/*
 * filename: PcWorldState.cs
 */

namespace SegaAgent.PC.Awareness;


// =============================================================
// PC WORLD STATE
//
// Sega's authoritative local snapshot of the desktop world.
//
// It contains observations only.
//
// It does NOT:
//
// make decisions
// perform actions
// call the AI
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
    } =
        new();


    // =========================================================
    // MOUSE
    // =========================================================

    public PcMouseState Mouse
    {
        get;
        init;
    } =
        new();


    // =========================================================
    // FOREGROUND WINDOW
    // =========================================================

    public PcForegroundWindowState
        ForegroundWindow
    {
        get;
        init;
    } =
        new();


    // =========================================================
    // ACTIVE DISPLAY
    // =========================================================

    public PcDisplayState Display
    {
        get;
        init;
    } =
        new();


    // =========================================================
    // SEGA
    //
    // Sega is a first-class physical entity inside the same
    // world model as the user, mouse, windows and monitors.
    // =========================================================

    public PcSegaPresenceState Sega
    {
        get;
        init;
    } =
        new();
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
    } =
        string.Empty;


    // =========================================================
    // WINDOW
    // =========================================================

    public string Title
    {
        get;
        init;
    } =
        string.Empty;


    public string ClassName
    {
        get;
        init;
    } =
        string.Empty;


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
        Handle !=
        IntPtr.Zero;
}


// =============================================================
// DISPLAY STATE
// =============================================================

public sealed class PcDisplayState
{
    public PcRectangle MonitorBounds
    {
        get;
        init;
    }


    public PcRectangle WorkArea
    {
        get;
        init;
    }


    public bool IsPrimary
    {
        get;
        init;
    }
}


// =============================================================
// SEGA PHYSICAL WORLD STATE
//
// This is not Sega's personality or emotional state.
//
// It answers physical questions such as:
//
// Where am I?
// Am I visible?
// Is the cursor over me?
// Am I covering the foreground application?
// Am I on the same monitor as the active application?
// =============================================================

public sealed class PcSegaPresenceState
{
    // =========================================================
    // AVAILABILITY
    // =========================================================

    public bool IsAvailable
    {
        get;
        init;
    }


    // =========================================================
    // INTERNAL NATIVE HANDLE
    // =========================================================

    public IntPtr WindowHandle
    {
        get;
        init;
    }


    // =========================================================
    // GEOMETRY
    // =========================================================

    public PcRectangle WindowBounds
    {
        get;
        init;
    }


    public PcRectangle BodyBounds
    {
        get;
        init;
    }


    // =========================================================
    // VISIBILITY
    // =========================================================

    public bool IsVisible
    {
        get;
        init;
    }


    public bool IsFaded
    {
        get;
        init;
    }


    // =========================================================
    // SEGA'S DISPLAY
    // =========================================================

    public PcDisplayState Display
    {
        get;
        init;
    } =
        new();


    // =========================================================
    // MOUSE RELATIONSHIP
    // =========================================================

    public bool IsMouseOverBody
    {
        get;
        init;
    }


    public double MouseDistanceFromBodyCenter
    {
        get;
        init;
    }


    // =========================================================
    // FOREGROUND RELATIONSHIP
    // =========================================================

    public bool SharesMonitorWithForeground
    {
        get;
        init;
    }


    public bool OverlapsForegroundWindow
    {
        get;
        init;
    }


    // =========================================================
    // PORTION OF SEGA'S BODY OVER FOREGROUND WINDOW
    //
    // 0.0 = none
    // 1.0 = all of Sega's body bounds overlap it
    // =========================================================

    public double ForegroundOverlapRatio
    {
        get;
        init;
    }


    public bool OverlapsFullscreenContent
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
    // =========================================================
    // SIZE
    // =========================================================

    public int Width =>
        Math.Max(
            0,
            Right -
            Left);


    public int Height =>
        Math.Max(
            0,
            Bottom -
            Top);


    public long Area =>
        (long)Width *
        Height;


    public bool IsEmpty =>
        Width <=
            0
        ||
        Height <=
            0;


    // =========================================================
    // CENTER
    // =========================================================

    public double CenterX =>
        Left +
        Width /
        2.0;


    public double CenterY =>
        Top +
        Height /
        2.0;


    // =========================================================
    // CONTAINS POINT
    // =========================================================

    public bool Contains(
        int x,
        int y)
    {
        if (IsEmpty)
        {
            return false;
        }


        return
            x >=
                Left
            &&
            x <
                Right
            &&
            y >=
                Top
            &&
            y <
                Bottom;
    }


    // =========================================================
    // INTERSECTION
    // =========================================================

    public bool Intersects(
        PcRectangle other)
    {
        if (IsEmpty ||
            other.IsEmpty)
        {
            return false;
        }


        return
            Left <
                other.Right
            &&
            Right >
                other.Left
            &&
            Top <
                other.Bottom
            &&
            Bottom >
                other.Top;
    }


    public PcRectangle Intersection(
        PcRectangle other)
    {
        if (!Intersects(
                other))
        {
            return default;
        }


        return new PcRectangle(
            Math.Max(
                Left,
                other.Left),

            Math.Max(
                Top,
                other.Top),

            Math.Min(
                Right,
                other.Right),

            Math.Min(
                Bottom,
                other.Bottom));
    }


    public long IntersectionArea(
        PcRectangle other)
    {
        return Intersection(
                other)
            .Area;
    }


    // =========================================================
    // STRING
    // =========================================================

    public override string ToString()
    {
        return
            $"X={Left}, Y={Top}, " +
            $"Width={Width}, Height={Height}";
    }
}
