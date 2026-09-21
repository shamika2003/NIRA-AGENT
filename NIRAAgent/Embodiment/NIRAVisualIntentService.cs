/*
 * filename: NIRAVisualIntentService.cs
 */

using System.Threading;

using NIRAAgent.Agent.State;
using NIRAAgent.Character.State;

namespace NIRAAgent.Embodiment;

public sealed class NIRAVisualIntentService
    : IDisposable
{
    // =========================================================
    // STATE SOURCES
    // =========================================================

    private readonly NIRAStateService
        _state;


    private readonly NIRACharacterStateService
        _characterState;


    private readonly NIRAAttitudeService
        _attitude;


    // =========================================================
    // SYNCHRONIZATION
    // =========================================================

    private readonly object
        _sync =
            new();


    // =========================================================
    // AUTOMATIC INTENT
    // =========================================================

    private NIRAVisualIntent
        _automaticIntent;


    // =========================================================
    // EXPLICIT OVERRIDE
    //
    // This remains the permanent entry point for future agent,
    // user and tool-directed visual forms.
    //
    // Automatic NIRA embodiment resumes when the override is
    // cleared.
    // =========================================================

    private NIRAVisualIntent?
        _overrideIntent;


    // =========================================================
    // TRANSIENT PARTICLE EXPRESSION
    //
    // Automatic character state continues to own NIRA's normal
    // posture. This is a short-lived layer over that posture.
    // =========================================================

    private readonly System.Threading.Timer
        _expressionTimer;


    private TransientExpression?
        _transientExpression;


    private double
        _transientExpressionStrength;


    private bool
        _disposed;


    private static readonly TimeSpan ExpressionTickInterval =
        TimeSpan.FromMilliseconds(
            50);


    // =========================================================
    // EVENT
    // =========================================================

    public event Action<NIRAVisualIntent>?
        IntentChanged;


    // =========================================================
    // CURRENT
    // =========================================================

    public NIRAVisualIntent Current
    {
        get
        {
            lock (_sync)
            {
                return ResolveCurrent();
            }
        }
    }


    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public NIRAVisualIntentService(
        NIRAStateService state,
        NIRACharacterStateService characterState,
        NIRAAttitudeService attitude)
    {
        _state =
            state
            ?? throw new ArgumentNullException(
                nameof(state));


        _characterState =
            characterState
            ?? throw new ArgumentNullException(
                nameof(characterState));


        _attitude =
            attitude
            ?? throw new ArgumentNullException(
                nameof(attitude));


        _automaticIntent =
            BuildAutomaticIntent(
                _state.Current,
                _characterState.Current);


        _expressionTimer =
            new System.Threading.Timer(
                OnExpressionTimer,
                null,
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);


        _state.StateChanged +=
            OnNIRAStateChanged;


        _characterState.StateChanged +=
            OnCharacterStateChanged;
    }


    // =========================================================
    // SET EXPLICIT INTENT
    // =========================================================

    public void SetIntent(
        NIRAVisualIntent intent)
    {
        ArgumentNullException.ThrowIfNull(
            intent);


        NIRAVisualIntent normalized =
            intent.Normalize();


        NIRAVisualIntent before;

        NIRAVisualIntent after;


        lock (_sync)
        {
            before =
                ResolveCurrent();


            _overrideIntent =
                normalized;


            after =
                ResolveCurrent();
        }


        PublishIfChanged(
            before,
            after);
    }


    // =========================================================
    // CLEAR OVERRIDE
    // =========================================================

    public void ClearIntentOverride()
    {
        NIRAVisualIntent before;

        NIRAVisualIntent after;


        lock (_sync)
        {
            if (_overrideIntent ==
                null)
            {
                return;
            }


            before =
                ResolveCurrent();


            _overrideIntent =
                null;


            after =
                ResolveCurrent();
        }


        PublishIfChanged(
            before,
            after);
    }


    // =========================================================
    // TRANSIENT EXPRESSION API
    //
    // This is generic on purpose.
    //
    // Today the orb provider understands IdentityMark.
    // Future procedural expressions can use this same route without
    // replacing the character/mood/body architecture.
    // =========================================================

    public void TriggerExpression(
        string expressionId,
        double strength = 1.0,
        TimeSpan? delay = null,
        TimeSpan? fadeIn = null,
        TimeSpan? hold = null,
        TimeSpan? fadeOut = null)
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);


        string normalizedId =
            string.IsNullOrWhiteSpace(
                expressionId)
                ? NIRAVisualExpressionIds.None
                : expressionId.Trim();


        if (
            normalizedId ==
                NIRAVisualExpressionIds.None
            ||
            strength <=
                0.001)
        {
            ClearTransientExpression();

            return;
        }


        TransientExpression expression =
            new(
                normalizedId,
                Clamp01(
                    strength),
                DateTimeOffset.UtcNow,
                NormalizeDuration(
                    delay ??
                        TimeSpan.Zero,
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(
                        8)),
                NormalizeDuration(
                    fadeIn ??
                        TimeSpan.FromMilliseconds(
                            260),
                    TimeSpan.FromMilliseconds(
                        40),
                    TimeSpan.FromSeconds(
                        5)),
                NormalizeDuration(
                    hold ??
                        TimeSpan.FromMilliseconds(
                            380),
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(
                        8)),
                NormalizeDuration(
                    fadeOut ??
                        TimeSpan.FromMilliseconds(
                            920),
                    TimeSpan.FromMilliseconds(
                        40),
                    TimeSpan.FromSeconds(
                        6)));


        NIRAVisualIntent before;

        NIRAVisualIntent after;


        lock (_sync)
        {
            before =
                ResolveCurrent();


            _transientExpression =
                expression;


            _transientExpressionStrength =
                0.0;


            after =
                ResolveCurrent();
        }


        try
        {
            _expressionTimer.Change(
                TimeSpan.Zero,
                ExpressionTickInterval);
        }
        catch (ObjectDisposedException)
        {
        }


        PublishIfChanged(
            before,
            after);
    }


    public void TriggerIdentityReveal(
        double strength = 1.0,
        TimeSpan? delay = null,
        TimeSpan? fadeIn = null,
        TimeSpan? hold = null,
        TimeSpan? fadeOut = null)
    {
        TriggerExpression(
            NIRAVisualExpressionIds.IdentityMark,
            strength,
            delay,
            fadeIn,
            hold,
            fadeOut);
    }


    public void ClearTransientExpression()
    {
        NIRAVisualIntent before;

        NIRAVisualIntent after;


        lock (_sync)
        {
            before =
                ResolveCurrent();


            _transientExpression =
                null;


            _transientExpressionStrength =
                0.0;


            after =
                ResolveCurrent();
        }


        try
        {
            _expressionTimer.Change(
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
        }


        PublishIfChanged(
            before,
            after);
    }


    // =========================================================
    // TRANSIENT EXPRESSION CLOCK
    // =========================================================

    private void OnExpressionTimer(
        object? state)
    {
        NIRAVisualIntent before;

        NIRAVisualIntent after;

        bool stopTimer =
            false;


        lock (_sync)
        {
            if (
                _disposed
                ||
                _transientExpression ==
                    null)
            {
                return;
            }


            before =
                ResolveCurrent();


            DateTimeOffset now =
                DateTimeOffset.UtcNow;


            _transientExpressionStrength =
                ResolveExpressionStrength(
                    _transientExpression,
                    now);


            if (IsExpressionFinished(
                    _transientExpression,
                    now))
            {
                _transientExpression =
                    null;


                _transientExpressionStrength =
                    0.0;


                stopTimer =
                    true;
            }


            after =
                ResolveCurrent();
        }


        if (stopTimer)
        {
            try
            {
                _expressionTimer.Change(
                    Timeout.InfiniteTimeSpan,
                    Timeout.InfiniteTimeSpan);
            }
            catch (ObjectDisposedException)
            {
            }
        }


        PublishIfChanged(
            before,
            after);
    }


    private static double ResolveExpressionStrength(
        TransientExpression expression,
        DateTimeOffset now)
    {
        double elapsed =
            (
                now -
                expression.StartedAt
            )
            .TotalSeconds;


        if (elapsed <
            expression.Delay.TotalSeconds)
        {
            return 0.0;
        }


        elapsed -=
            expression.Delay.TotalSeconds;


        double fadeInSeconds =
            expression.FadeIn.TotalSeconds;


        if (elapsed <
            fadeInSeconds)
        {
            return
                expression.Strength
                *
                SmoothStep01(
                    elapsed /
                    Math.Max(
                        0.001,
                        fadeInSeconds));
        }


        elapsed -=
            fadeInSeconds;


        if (elapsed <
            expression.Hold.TotalSeconds)
        {
            return
                expression.Strength;
        }


        elapsed -=
            expression.Hold.TotalSeconds;


        double fadeOutSeconds =
            expression.FadeOut.TotalSeconds;


        if (elapsed <
            fadeOutSeconds)
        {
            return
                expression.Strength
                *
                (
                    1.0 -
                    SmoothStep01(
                        elapsed /
                        Math.Max(
                            0.001,
                            fadeOutSeconds))
                );
        }


        return 0.0;
    }


    private static bool IsExpressionFinished(
        TransientExpression expression,
        DateTimeOffset now)
    {
        TimeSpan total =
            expression.Delay
            +
            expression.FadeIn
            +
            expression.Hold
            +
            expression.FadeOut;


        return
            now -
            expression.StartedAt
            >=
            total;
    }


    // =========================================================
    // MIND / BODY STATE CHANGE
    // =========================================================

    private void OnNIRAStateChanged(
        NIRAStateSnapshot snapshot)
    {
        RebuildAutomaticIntent(
            snapshot,
            _characterState.Current);
    }


    // =========================================================
    // CHARACTER STATE CHANGE
    // =========================================================

    private void OnCharacterStateChanged(
        NIRACharacterSnapshot snapshot)
    {
        RebuildAutomaticIntent(
            _state.Current,
            snapshot);
    }


    // =========================================================
    // REBUILD AUTOMATIC INTENT
    // =========================================================

    private void RebuildAutomaticIntent(
        NIRAStateSnapshot state,
        NIRACharacterSnapshot character)
    {
        NIRAVisualIntent before;

        NIRAVisualIntent after;


        lock (_sync)
        {
            before =
                ResolveCurrent();


            _automaticIntent =
                BuildAutomaticIntent(
                    state,
                    character);


            after =
                ResolveCurrent();
        }


        PublishIfChanged(
            before,
            after);
    }


    // =========================================================
    // AUTOMATIC ORB EXPRESSION
    //
    // NIRA has one canonical particle body: the orb.
    //
    // There are no emotion presets here.
    //
    // Mind/body state establishes the immediate physical posture.
    // Persistent character state then continuously changes the
    // same physical dimensions.
    // =========================================================

    private NIRAVisualIntent BuildAutomaticIntent(
        NIRAStateSnapshot state,
        NIRACharacterSnapshot character)
    {
        NIRAVisualIntent baseline =
            ResolveMindBaseline(
                state.Mind);


        NIRARelationshipState relationship =
            character.Relationship;


        NIRAMoodState mood =
            character.Mood;


        NIRASituationState situation =
            character.Situation;


        NIRAAttitudeState attitude =
            _attitude.Evaluate(
                character,
                null);


        // =====================================================
        // CHARACTER ENERGY
        // =====================================================

        double characterEnergy =
            Clamp01(
                mood.Energy *
                    0.48
                +
                attitude.Engagement *
                    0.24
                +
                mood.Amusement *
                    0.16
                +
                mood.Curiosity *
                    0.12);


        // =====================================================
        // TENSION
        //
        // Tension can mean irritation, concern, social friction
        // or simply intense controlled attention.
        // =====================================================

        double characterTension =
            Clamp01(
                mood.Irritation *
                    0.52
                +
                relationship.Friction *
                    0.26
                +
                mood.Concern *
                    0.16
                +
                (
                    1.0 -
                    attitude.Patience
                ) *
                    0.10
                +
                attitude.Assertiveness *
                    0.06);


        // =====================================================
        // FOCUS
        // =====================================================

        double situationFocus =
            ResolveSituationFocus(
                situation);


        // =====================================================
        // FLOW
        //
        // Curiosity, amusement and playfulness create more
        // internal circulation. Restraint keeps it controlled.
        // =====================================================

        double characterFlow =
            Clamp01(
                (
                    mood.Curiosity *
                        0.34
                    +
                    mood.Amusement *
                        0.24
                    +
                    attitude.Playfulness *
                        0.22
                    +
                    mood.Energy *
                        0.20
                )
                *
                (
                    1.0 -
                    attitude.Restraint *
                        0.36
                ));


        // =====================================================
        // PULSE
        //
        // Pulse is intentionally not equivalent to happiness.
        // Affection, amusement, concern and warmth can all make
        // NIRA feel more physically present and alive.
        // =====================================================

        double characterPulse =
            Clamp01(
                0.10
                +
                mood.Affection *
                    0.22
                +
                mood.Amusement *
                    0.16
                +
                mood.Concern *
                    0.12
                +
                attitude.Warmth *
                    0.12
                +
                mood.Energy *
                    0.10);


        // =====================================================
        // PRESENCE
        // =====================================================

        double characterPresence =
            Clamp01(
                0.32
                +
                attitude.Engagement *
                    0.22
                +
                attitude.Warmth *
                    0.18
                +
                (
                    1.0 -
                    attitude.EmotionalDistance
                ) *
                    0.14
                +
                relationship.Attachment *
                    0.08
                +
                mood.Concern *
                    0.06);


        // =====================================================
        // COHESION
        //
        // Focus and restraint produce deliberate control.
        // Playfulness and tension are allowed to loosen the edge
        // slightly, but never destroy NIRA's identity.
        // =====================================================

        double characterCohesion =
            Clamp01(
                0.62
                +
                situationFocus *
                    0.28
                +
                attitude.Restraint *
                    0.12
                -
                attitude.Playfulness *
                    0.06
                -
                characterTension *
                    0.04);


        double energy =
            Lerp(
                baseline.Energy,
                characterEnergy,
                0.24);


        double tension =
            Lerp(
                baseline.Tension,
                characterTension,
                0.74);


        double focusTarget =
            Math.Max(
                baseline.Focus,
                situationFocus);


        double focus =
            Lerp(
                baseline.Focus,
                focusTarget,
                0.78);


        double flow =
            Lerp(
                baseline.Flow,
                characterFlow,
                0.40);


        double pulseTarget =
            Math.Max(
                baseline.Pulse,
                characterPulse);


        double pulse =
            Lerp(
                baseline.Pulse,
                pulseTarget,
                0.64);


        double presence =
            Lerp(
                baseline.Presence,
                characterPresence,
                0.20);


        double cohesionTarget =
            Math.Max(
                baseline.Cohesion -
                    0.08,
                characterCohesion);


        double cohesion =
            Lerp(
                baseline.Cohesion,
                cohesionTarget,
                0.38);


        // =====================================================
        // SCALE
        //
        // Scale only moves subtly during automatic embodiment.
        // Large transformations remain the responsibility of
        // explicit future visual intents.
        // =====================================================

        double characterScale =
            1.0
            +
            (
                energy -
                0.50
            ) *
                0.08
            +
            attitude.Playfulness *
                0.025
            -
            tension *
                0.025
            -
            attitude.Restraint *
                0.015;


        double scale =
            Math.Clamp(
                Lerp(
                    baseline.Scale,
                    characterScale,
                    0.52),
                0.94,
                1.10);


        NIRAVisualIntent result =
            new()
            {
                FormId =
                    NIRAVisualFormIds.Orb,

                Energy =
                    energy,

                Cohesion =
                    cohesion,

                Presence =
                    presence,

                Scale =
                    scale,

                Tension =
                    tension,

                Flow =
                    flow,

                Pulse =
                    pulse,

                Focus =
                    focus,

                Source =
                    NIRAVisualIntentSource.Automatic
            };


        return ApplyBodyState(
                result,
                state.Body)
            .Normalize();
    }


    // =========================================================
    // MIND BASELINE
    // =========================================================

    private static NIRAVisualIntent ResolveMindBaseline(
        NIRAMindState mind)
    {
        return mind switch
        {
            // Quiet but clearly alive.
            NIRAMindState.Idle =>
                new NIRAVisualIntent
                {
                    FormId =
                        NIRAVisualFormIds.Orb,

                    Energy =
                        0.22,

                    Cohesion =
                        0.82,

                    Presence =
                        0.66,

                    Scale =
                        0.98,

                    Tension =
                        0.05,

                    Flow =
                        0.20,

                    Pulse =
                        0.14,

                    Focus =
                        0.22,

                    Source =
                        NIRAVisualIntentSource.Automatic
                },


            // Opens slightly and becomes attentive.
            NIRAMindState.Listening =>
                new NIRAVisualIntent
                {
                    FormId =
                        NIRAVisualFormIds.Orb,

                    Energy =
                        0.42,

                    Cohesion =
                        0.84,

                    Presence =
                        0.82,

                    Scale =
                        1.03,

                    Tension =
                        0.06,

                    Flow =
                        0.32,

                    Pulse =
                        0.30,

                    Focus =
                        0.56,

                    Source =
                        NIRAVisualIntentSource.Automatic
                },


            // Pulls inward, becomes controlled and concentrated.
            NIRAMindState.Thinking =>
                new NIRAVisualIntent
                {
                    FormId =
                        NIRAVisualFormIds.Orb,

                    Energy =
                        0.60,

                    Cohesion =
                        0.94,

                    Presence =
                        0.90,

                    Scale =
                        0.96,

                    Tension =
                        0.10,

                    Flow =
                        0.70,

                    Pulse =
                        0.20,

                    Focus =
                        0.92,

                    Source =
                        NIRAVisualIntentSource.Automatic
                },


            // Most expressive state: visible breathing / voice pulse.
            NIRAMindState.Speaking =>
                new NIRAVisualIntent
                {
                    FormId =
                        NIRAVisualFormIds.Orb,

                    Energy =
                        0.80,

                    Cohesion =
                        0.82,

                    Presence =
                        1.00,

                    Scale =
                        1.07,

                    Tension =
                        0.08,

                    Flow =
                        0.74,

                    Pulse =
                        0.88,

                    Focus =
                        0.50,

                    Source =
                        NIRAVisualIntentSource.Automatic
                },


            _ =>
                NIRAVisualIntent.RestingOrb
        };
    }


    // =========================================================
    // SITUATION FOCUS
    // =========================================================

    private static double ResolveSituationFocus(
        NIRASituationState situation)
    {
        double intensity =
            Math.Clamp(
                situation.Intensity,
                0.0,
                1.0);


        return situation.Mode switch
        {
            NIRAInteractionMode.FocusedWork =>
                Clamp01(
                    0.66 +
                    intensity *
                        0.34),


            NIRAInteractionMode.Serious =>
                Clamp01(
                    0.72 +
                    intensity *
                        0.28),


            NIRAInteractionMode.Sensitive =>
                Clamp01(
                    0.46 +
                    intensity *
                        0.24),


            _ =>
                Clamp01(
                    0.18 +
                    intensity *
                        0.16)
        };
    }


    // =========================================================
    // BODY STATE
    // =========================================================

    private static NIRAVisualIntent ApplyBodyState(
        NIRAVisualIntent intent,
        NIRABodyState body)
    {
        return body switch
        {
            // Deliberate travel: visibly more flow and energy.
            NIRABodyState.Moving =>
                intent with
                {
                    Energy =
                        intent.Energy +
                        0.12,

                    Cohesion =
                        intent.Cohesion +
                        0.03,

                    Flow =
                        intent.Flow +
                        0.16,

                    Pulse =
                        intent.Pulse +
                        0.08,

                    Focus =
                        intent.Focus +
                        0.08
                },


            // Direct user manipulation: body tightens and follows the drag.
            NIRABodyState.Dragging =>
                intent with
                {
                    Energy =
                        intent.Energy +
                        0.08,

                    Cohesion =
                        intent.Cohesion +
                        0.15,

                    Tension =
                        intent.Tension +
                        0.06,

                    Flow =
                        Math.Max(
                            0.0,
                            intent.Flow -
                            0.08),

                    Pulse =
                        intent.Pulse +
                        0.04,

                    Focus =
                        intent.Focus +
                        0.18
                },


            _ =>
                intent
        };
    }


    // =========================================================
    // RESOLVE
    // =========================================================

    private NIRAVisualIntent ResolveCurrent()
    {
        NIRAVisualIntent current =
            _overrideIntent
            ??
            _automaticIntent;


        if (
            _transientExpression ==
                null
            ||
            _transientExpressionStrength <=
                0.001)
        {
            return current;
        }


        string existingExpressionId =
            current.ExpressionId
            ??
            NIRAVisualExpressionIds.None;


        if (
            !string.IsNullOrWhiteSpace(
                existingExpressionId)
            &&
            !string.Equals(
                existingExpressionId,
                _transientExpression.ExpressionId,
                StringComparison.OrdinalIgnoreCase)
            &&
            current.ExpressionStrength >
                0.001)
        {
            return current;
        }


        return current with
        {
            ExpressionId =
                _transientExpression.ExpressionId,

            ExpressionStrength =
                Math.Max(
                    current.ExpressionStrength,
                    _transientExpressionStrength)
        };
    }


    // =========================================================
    // PUBLISH
    // =========================================================

    private void PublishIfChanged(
        NIRAVisualIntent before,
        NIRAVisualIntent after)
    {
        if (before ==
            after)
        {
            return;
        }


        IntentChanged?.Invoke(
            after);
    }


    // =========================================================
    // MATH
    // =========================================================

    private static double Clamp01(
        double value)
    {
        return Math.Clamp(
            value,
            0.0,
            1.0);
    }


    private static double Lerp(
        double from,
        double to,
        double amount)
    {
        return from +
            (
                to -
                from
            )
            *
            Math.Clamp(
                amount,
                0.0,
                1.0);
    }


    // =========================================================
    // EXPRESSION MATH
    // =========================================================

    private static TimeSpan NormalizeDuration(
        TimeSpan value,
        TimeSpan minimum,
        TimeSpan maximum)
    {
        if (value <
            minimum)
        {
            return minimum;
        }


        if (value >
            maximum)
        {
            return maximum;
        }


        return value;
    }


    private static double SmoothStep01(
        double value)
    {
        value =
            Math.Clamp(
                value,
                0.0,
                1.0);


        return
            value *
            value *
            (
                3.0 -
                2.0 *
                value
            );
    }


    private sealed record TransientExpression(
        string ExpressionId,
        double Strength,
        DateTimeOffset StartedAt,
        TimeSpan Delay,
        TimeSpan FadeIn,
        TimeSpan Hold,
        TimeSpan FadeOut);


    // =========================================================
    // DISPOSE
    // =========================================================

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }


            _disposed =
                true;


            _transientExpression =
                null;


            _transientExpressionStrength =
                0.0;
        }


        _expressionTimer.Dispose();


        _state.StateChanged -=
            OnNIRAStateChanged;


        _characterState.StateChanged -=
            OnCharacterStateChanged;
    }
}
