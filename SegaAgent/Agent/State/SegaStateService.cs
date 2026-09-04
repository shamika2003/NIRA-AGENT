/*
 * filename: SegaStateService.cs
 */

namespace SegaAgent.Agent.State;


// =========================================================
// MIND STATE
// =========================================================

public enum SegaMindState
{
    Idle,

    Listening,

    Thinking,

    Speaking
}


// =========================================================
// BODY STATE
//
// Avoiding has been removed.
//
// Sega no longer runs away from the mouse.
//
// Moving remains because future tools / presentation logic may
// intentionally reposition Sega.
//
// Dragging remains for direct user movement.
// =========================================================

public enum SegaBodyState
{
    Resting,

    Moving,

    Dragging
}


// =========================================================
// STATE SNAPSHOT
// =========================================================

public readonly record struct SegaStateSnapshot(
    SegaMindState Mind,
    SegaBodyState Body
);


// =========================================================
// STATE SERVICE
// =========================================================

public sealed class SegaStateService
{
    private readonly object
        _sync =
            new();


    // =====================================================
    // MIND FLAGS
    // =====================================================

    private bool
        _listening;


    private bool
        _thinking;


    private bool
        _speaking;


    // =====================================================
    // BODY FLAGS
    // =====================================================

    private bool
        _moving;


    private bool
        _dragging;


    // =====================================================
    // EVENT
    // =====================================================

    public event Action<SegaStateSnapshot>?
        StateChanged;


    // =====================================================
    // CURRENT
    // =====================================================

    public SegaStateSnapshot Current
    {
        get
        {
            lock (_sync)
            {
                return BuildSnapshot();
            }
        }
    }


    // =====================================================
    // LISTENING
    // =====================================================

    public void SetListening(
        bool listening)
    {
        Update(() =>
        {
            _listening =
                listening;
        });
    }


    // =====================================================
    // THINKING
    // =====================================================

    public void SetThinking(
        bool thinking)
    {
        Update(() =>
        {
            _thinking =
                thinking;
        });
    }


    // =====================================================
    // SPEAKING
    // =====================================================

    public void SetSpeaking(
        bool speaking)
    {
        Update(() =>
        {
            _speaking =
                speaking;
        });
    }


    // =====================================================
    // MOVING
    // =====================================================

    public void SetMoving(
        bool moving)
    {
        Update(() =>
        {
            _moving =
                moving;
        });
    }


    // =====================================================
    // DRAGGING
    // =====================================================

    public void SetDragging(
        bool dragging)
    {
        Update(() =>
        {
            _dragging =
                dragging;
        });
    }


    // =====================================================
    // RESET BODY
    // =====================================================

    public void ResetBody()
    {
        Update(() =>
        {
            _moving =
                false;


            _dragging =
                false;
        });
    }


    // =====================================================
    // UPDATE
    // =====================================================

    private void Update(
        Action mutation)
    {
        SegaStateSnapshot before;

        SegaStateSnapshot after;


        lock (_sync)
        {
            before =
                BuildSnapshot();


            mutation();


            after =
                BuildSnapshot();
        }


        if (before ==
            after)
        {
            return;
        }


        StateChanged?.Invoke(
            after);
    }


    // =====================================================
    // SNAPSHOT
    // =====================================================

    private SegaStateSnapshot BuildSnapshot()
    {
        return new SegaStateSnapshot(
            ResolveMindState(),
            ResolveBodyState());
    }


    // =====================================================
    // MIND
    // =====================================================

    private SegaMindState ResolveMindState()
    {
        if (_speaking)
        {
            return
                SegaMindState.Speaking;
        }


        if (_thinking)
        {
            return
                SegaMindState.Thinking;
        }


        if (_listening)
        {
            return
                SegaMindState.Listening;
        }


        return
            SegaMindState.Idle;
    }


    // =====================================================
    // BODY
    // =====================================================

    private SegaBodyState ResolveBodyState()
    {
        if (_dragging)
        {
            return
                SegaBodyState.Dragging;
        }


        if (_moving)
        {
            return
                SegaBodyState.Moving;
        }


        return
            SegaBodyState.Resting;
    }
}