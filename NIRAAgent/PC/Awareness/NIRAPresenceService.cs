/*
 * filename: NIRAPresenceService.cs
 */

using System.Diagnostics;

namespace NIRAAgent.PC.Awareness;


// =============================================================
// NIRA PRESENCE SNAPSHOT
//
// Raw facts reported by NIRA's desktop body.
//
// It does NOT:
//
// decide where NIRA should move
// decide whether NIRA is obstructing something
// decide whether NIRA should hide
// call the AI
// =============================================================

public sealed record NIRAPresenceSnapshot
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
    // Meaningful visible / interactive NIRA body inside the
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

    public static NIRAPresenceSnapshot Unavailable =>
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
// NIRA PRESENCE SERVICE
//
// Thread-safe bridge between NIRA's actual WPF body and the
// shared PC world-state system.
//
// CompanionWindow reports facts here.
// PcWorldStateService reads them.
// =============================================================

public sealed class NIRAPresenceService
{
    private readonly object
        _sync =
            new();


    private NIRAPresenceSnapshot
        _current =
            NIRAPresenceSnapshot.Unavailable;


    public event Action<NIRAPresenceSnapshot>?
        PresenceChanged;


    public NIRAPresenceSnapshot Current
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


        NIRAPresenceSnapshot before;

        NIRAPresenceSnapshot after;


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
                new NIRAPresenceSnapshot
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
        NIRAPresenceSnapshot after;


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
        NIRAPresenceSnapshot after;


        lock (_sync)
        {
            if (!_current.IsAvailable)
            {
                return;
            }


            after =
                new NIRAPresenceSnapshot
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
        NIRAPresenceSnapshot snapshot)
    {
        Action<NIRAPresenceSnapshot>?
            handlers =
                PresenceChanged;


        if (handlers ==
            null)
        {
            return;
        }


        foreach (
            Action<NIRAPresenceSnapshot> handler
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
                    $"[NIRAPresence] " +
                    $"OBSERVER ERROR: {ex}");
            }
        }
    }
}

