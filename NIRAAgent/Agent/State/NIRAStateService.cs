/*
 * filename: NIRAStateService.cs
 */

namespace NIRAAgent.Agent.State;


// =========================================================
// MIND STATE
// =========================================================

public enum NIRAMindState
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
// NIRA no longer runs away from the mouse.
//
// Moving remains because future tools / presentation logic may
// intentionally reposition NIRA.
//
// Dragging remains for direct user movement.
// =========================================================

public enum NIRABodyState
{
    Resting,

    Moving,

    Dragging
}


// =========================================================
// STATE SNAPSHOT
// =========================================================

public readonly record struct NIRAStateSnapshot(
    NIRAMindState Mind,
    NIRABodyState Body
);


// =========================================================
// STATE SERVICE
// =========================================================

public sealed class NIRAStateService
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

    public event Action<NIRAStateSnapshot>?
        StateChanged;


    // =====================================================
    // CURRENT
    // =====================================================

    public NIRAStateSnapshot Current
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
        NIRAStateSnapshot before;

        NIRAStateSnapshot after;


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

    private NIRAStateSnapshot BuildSnapshot()
    {
        return new NIRAStateSnapshot(
            ResolveMindState(),
            ResolveBodyState());
    }


    // =====================================================
    // MIND
    // =====================================================

    private NIRAMindState ResolveMindState()
    {
        if (_speaking)
        {
            return
                NIRAMindState.Speaking;
        }


        if (_thinking)
        {
            return
                NIRAMindState.Thinking;
        }


        if (_listening)
        {
            return
                NIRAMindState.Listening;
        }


        return
            NIRAMindState.Idle;
    }


    // =====================================================
    // BODY
    // =====================================================

    private NIRABodyState ResolveBodyState()
    {
        if (_dragging)
        {
            return
                NIRABodyState.Dragging;
        }


        if (_moving)
        {
            return
                NIRABodyState.Moving;
        }


        return
            NIRABodyState.Resting;
    }
}
