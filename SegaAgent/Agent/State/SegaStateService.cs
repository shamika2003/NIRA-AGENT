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
// =========================================================

public enum SegaBodyState
{
    Resting,

    Moving,

    Avoiding,

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
    private readonly object _sync =
        new();


    // =====================================================
    // INTERNAL MIND FLAGS
    // =====================================================

    private bool _listening;

    private bool _thinking;

    private bool _speaking;


    // =====================================================
    // INTERNAL BODY FLAGS
    // =====================================================

    private bool _moving;

    private bool _avoiding;

    private bool _dragging;


    // =====================================================
    // EVENT
    // =====================================================

    public event Action<SegaStateSnapshot>?
        StateChanged;


    // =====================================================
    // CURRENT STATE
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
    // AVOIDING
    // =====================================================

    public void SetAvoiding(
        bool avoiding)
    {
        Update(() =>
        {
            _avoiding =
                avoiding;
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
            _moving = false;

            _avoiding = false;

            _dragging = false;
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


        if (before == after)
        {
            return;
        }


        StateChanged?.Invoke(
            after);
    }


    // =====================================================
    // BUILD SNAPSHOT
    // =====================================================

    private SegaStateSnapshot BuildSnapshot()
    {
        return new SegaStateSnapshot(
            ResolveMindState(),
            ResolveBodyState());
    }


    // =====================================================
    // RESOLVE MIND
    // =====================================================

    private SegaMindState ResolveMindState()
    {
        /*
         * Priority matters.
         *
         * Sega may still be generating text while speech
         * has already started.
         *
         * In that case:
         *
         * Speaking wins visually.
         *
         * When speaking finishes, if thinking is still true,
         * the state automatically becomes Thinking again.
         */

        if (_speaking)
        {
            return SegaMindState.Speaking;
        }


        if (_thinking)
        {
            return SegaMindState.Thinking;
        }


        if (_listening)
        {
            return SegaMindState.Listening;
        }


        return SegaMindState.Idle;
    }


    // =====================================================
    // RESOLVE BODY
    // =====================================================

    private SegaBodyState ResolveBodyState()
    {
        if (_dragging)
        {
            return SegaBodyState.Dragging;
        }


        if (_avoiding)
        {
            return SegaBodyState.Avoiding;
        }


        if (_moving)
        {
            return SegaBodyState.Moving;
        }


        return SegaBodyState.Resting;
    }
}