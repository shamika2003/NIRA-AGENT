/*
 * filename: SegaPresenceService.cs
 */

using System.Diagnostics;

namespace SegaAgent.PC.Awareness;


// =============================================================
// SEGA PRESENCE SNAPSHOT
//
// Raw facts reported by Sega's desktop body.
//
// It does NOT:
//
// decide where Sega should move
// decide whether Sega is obstructing something
// decide whether Sega should hide
// call the AI
// =============================================================

public sealed record SegaPresenceSnapshot
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
    // NATIVE WINDOW
    // =========================================================

    public IntPtr WindowHandle
    {
        get;
        init;
    }


    // =========================================================
    // WINDOW BOUNDS
    //
    // Physical screen coordinates.
    // =========================================================

    public PcRectangle WindowBounds
    {
        get;
        init;
    }


    // =========================================================
    // BODY BOUNDS
    //
    // Meaningful visible / interactive Sega body inside the
    // transparent companion window.
    // =========================================================

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
    // VERSION
    // =========================================================

    public long Version
    {
        get;
        init;
    }


    public DateTimeOffset UpdatedAt
    {
        get;
        init;
    }


    // =========================================================
    // INITIAL
    // =========================================================

    public static SegaPresenceSnapshot Unavailable =>
        new()
        {
            IsAvailable =
                false,

            Version =
                0,

            UpdatedAt =
                DateTimeOffset.UtcNow
        };
}


// =============================================================
// SEGA PRESENCE SERVICE
//
// Thread-safe bridge between Sega's actual WPF body and the
// shared PC world-state system.
//
// CompanionWindow reports facts here.
// PcWorldStateService reads them.
// =============================================================

public sealed class SegaPresenceService
{
    private readonly object
        _sync =
            new();


    private SegaPresenceSnapshot
        _current =
            SegaPresenceSnapshot.Unavailable;


    public event Action<SegaPresenceSnapshot>?
        PresenceChanged;


    public SegaPresenceSnapshot Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }


    // =========================================================
    // REPORT BODY
    // =========================================================

    public void Report(
        IntPtr windowHandle,
        PcRectangle windowBounds,
        PcRectangle bodyBounds,
        bool isVisible,
        bool isFaded)
    {
        if (windowHandle ==
            IntPtr.Zero)
        {
            return;
        }


        if (windowBounds.IsEmpty ||
            bodyBounds.IsEmpty)
        {
            return;
        }


        SegaPresenceSnapshot before;

        SegaPresenceSnapshot after;


        lock (_sync)
        {
            before =
                _current;


            if (
                before.IsAvailable
                &&
                before.WindowHandle ==
                    windowHandle
                &&
                before.WindowBounds ==
                    windowBounds
                &&
                before.BodyBounds ==
                    bodyBounds
                &&
                before.IsVisible ==
                    isVisible
                &&
                before.IsFaded ==
                    isFaded)
            {
                return;
            }


            after =
                new SegaPresenceSnapshot
                {
                    IsAvailable =
                        true,

                    WindowHandle =
                        windowHandle,

                    WindowBounds =
                        windowBounds,

                    BodyBounds =
                        bodyBounds,

                    IsVisible =
                        isVisible,

                    IsFaded =
                        isFaded,

                    Version =
                        Math.Max(
                            1,
                            before.Version +
                            1),

                    UpdatedAt =
                        DateTimeOffset.UtcNow
                };


            _current =
                after;
        }


        Publish(
            after);
    }


    // =========================================================
    // VISUAL STATE
    // =========================================================

    public void SetVisualState(
        bool isVisible,
        bool isFaded)
    {
        SegaPresenceSnapshot after;


        lock (_sync)
        {
            if (!_current.IsAvailable)
            {
                return;
            }


            if (
                _current.IsVisible ==
                    isVisible
                &&
                _current.IsFaded ==
                    isFaded)
            {
                return;
            }


            after =
                _current with
                {
                    IsVisible =
                        isVisible,

                    IsFaded =
                        isFaded,

                    Version =
                        _current.Version +
                        1,

                    UpdatedAt =
                        DateTimeOffset.UtcNow
                };


            _current =
                after;
        }


        Publish(
            after);
    }


    // =========================================================
    // UNAVAILABLE
    // =========================================================

    public void MarkUnavailable()
    {
        SegaPresenceSnapshot after;


        lock (_sync)
        {
            if (!_current.IsAvailable)
            {
                return;
            }


            after =
                new SegaPresenceSnapshot
                {
                    IsAvailable =
                        false,

                    Version =
                        _current.Version +
                        1,

                    UpdatedAt =
                        DateTimeOffset.UtcNow
                };


            _current =
                after;
        }


        Publish(
            after);
    }


    // =========================================================
    // PUBLISH
    // =========================================================

    private void Publish(
        SegaPresenceSnapshot snapshot)
    {
        Action<SegaPresenceSnapshot>?
            handlers =
                PresenceChanged;


        if (handlers ==
            null)
        {
            return;
        }


        foreach (
            Action<SegaPresenceSnapshot> handler
            in handlers.GetInvocationList())
        {
            try
            {
                handler(
                    snapshot);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[SegaPresence] " +
                    $"OBSERVER ERROR: {ex}");
            }
        }
    }
}
