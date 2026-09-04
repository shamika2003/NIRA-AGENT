/*
 * filename: SegaVisualIntentService.cs
 */

using SegaAgent.Agent.State;
using SegaAgent.Character.State;

namespace SegaAgent.Embodiment;

public sealed class SegaVisualIntentService
    : IDisposable
{
    // =========================================================
    // STATE SOURCES
    // =========================================================

    private readonly SegaStateService
        _state;


    private readonly SegaCharacterStateService
        _characterState;


    private readonly SegaAttitudeService
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

    private SegaVisualIntent
        _automaticIntent;


    // =========================================================
    // EXPLICIT OVERRIDE
    //
    // This remains the permanent entry point for future agent,
    // user and tool-directed visual forms.
    //
    // Automatic Sega embodiment resumes when the override is
    // cleared.
    // =========================================================

    private SegaVisualIntent?
        _overrideIntent;


    // =========================================================
    // EVENT
    // =========================================================

    public event Action<SegaVisualIntent>?
        IntentChanged;


    // =========================================================
    // CURRENT
    // =========================================================

    public SegaVisualIntent Current
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

    public SegaVisualIntentService(
        SegaStateService state,
        SegaCharacterStateService characterState,
        SegaAttitudeService attitude)
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


        _state.StateChanged +=
            OnSegaStateChanged;


        _characterState.StateChanged +=
            OnCharacterStateChanged;
    }


    // =========================================================
    // SET EXPLICIT INTENT
    // =========================================================

    public void SetIntent(
        SegaVisualIntent intent)
    {
        ArgumentNullException.ThrowIfNull(
            intent);


        SegaVisualIntent normalized =
            intent.Normalize();


        SegaVisualIntent before;

        SegaVisualIntent after;


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
        SegaVisualIntent before;

        SegaVisualIntent after;


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
    // MIND / BODY STATE CHANGE
    // =========================================================

    private void OnSegaStateChanged(
        SegaStateSnapshot snapshot)
    {
        RebuildAutomaticIntent(
            snapshot,
            _characterState.Current);
    }


    // =========================================================
    // CHARACTER STATE CHANGE
    // =========================================================

    private void OnCharacterStateChanged(
        SegaCharacterSnapshot snapshot)
    {
        RebuildAutomaticIntent(
            _state.Current,
            snapshot);
    }


    // =========================================================
    // REBUILD AUTOMATIC INTENT
    // =========================================================

    private void RebuildAutomaticIntent(
        SegaStateSnapshot state,
        SegaCharacterSnapshot character)
    {
        SegaVisualIntent before;

        SegaVisualIntent after;


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
    // Sega has one canonical particle body: the orb.
    //
    // There are no emotion presets here.
    //
    // Mind/body state establishes the immediate physical posture.
    // Persistent character state then continuously changes the
    // same physical dimensions.
    // =========================================================

    private SegaVisualIntent BuildAutomaticIntent(
        SegaStateSnapshot state,
        SegaCharacterSnapshot character)
    {
        SegaVisualIntent baseline =
            ResolveMindBaseline(
                state.Mind);


        SegaRelationshipState relationship =
            character.Relationship;


        SegaMoodState mood =
            character.Mood;


        SegaSituationState situation =
            character.Situation;


        SegaAttitudeState attitude =
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
        // Sega feel more physically present and alive.
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
        // slightly, but never destroy Sega's identity.
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
                0.34);


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
                0.52);


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
                0.30);


        double cohesionTarget =
            Math.Max(
                baseline.Cohesion -
                    0.08,
                characterCohesion);


        double cohesion =
            Lerp(
                baseline.Cohesion,
                cohesionTarget,
                0.52);


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


        SegaVisualIntent result =
            new()
            {
                FormId =
                    SegaVisualFormIds.Orb,

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
                    SegaVisualIntentSource.Automatic
            };


        return ApplyBodyState(
                result,
                state.Body)
            .Normalize();
    }


    // =========================================================
    // MIND BASELINE
    // =========================================================

    private static SegaVisualIntent ResolveMindBaseline(
        SegaMindState mind)
    {
        return mind switch
        {
            SegaMindState.Listening =>
                new SegaVisualIntent
                {
                    FormId =
                        SegaVisualFormIds.Orb,

                    Energy =
                        0.40,

                    Cohesion =
                        0.84,

                    Presence =
                        0.74,

                    Scale =
                        1.02,

                    Tension =
                        0.08,

                    Flow =
                        0.36,

                    Pulse =
                        0.24,

                    Focus =
                        0.42,

                    Source =
                        SegaVisualIntentSource.Automatic
                },


            SegaMindState.Thinking =>
                new SegaVisualIntent
                {
                    FormId =
                        SegaVisualFormIds.Orb,

                    Energy =
                        0.58,

                    Cohesion =
                        0.90,

                    Presence =
                        0.88,

                    Scale =
                        0.99,

                    Tension =
                        0.12,

                    Flow =
                        0.58,

                    Pulse =
                        0.24,

                    Focus =
                        0.80,

                    Source =
                        SegaVisualIntentSource.Automatic
                },


            SegaMindState.Speaking =>
                new SegaVisualIntent
                {
                    FormId =
                        SegaVisualFormIds.Orb,

                    Energy =
                        0.72,

                    Cohesion =
                        0.84,

                    Presence =
                        1.00,

                    Scale =
                        1.06,

                    Tension =
                        0.10,

                    Flow =
                        0.62,

                    Pulse =
                        0.68,

                    Focus =
                        0.48,

                    Source =
                        SegaVisualIntentSource.Automatic
                },


            _ =>
                SegaVisualIntent.RestingOrb
        };
    }


    // =========================================================
    // SITUATION FOCUS
    // =========================================================

    private static double ResolveSituationFocus(
        SegaSituationState situation)
    {
        double intensity =
            Math.Clamp(
                situation.Intensity,
                0.0,
                1.0);


        return situation.Mode switch
        {
            SegaInteractionMode.FocusedWork =>
                Clamp01(
                    0.66 +
                    intensity *
                        0.34),


            SegaInteractionMode.Serious =>
                Clamp01(
                    0.72 +
                    intensity *
                        0.28),


            SegaInteractionMode.Sensitive =>
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

    private static SegaVisualIntent ApplyBodyState(
        SegaVisualIntent intent,
        SegaBodyState body)
    {
        return body switch
        {
            SegaBodyState.Moving =>
                intent with
                {
                    Energy =
                        intent.Energy +
                        0.08,

                    Cohesion =
                        intent.Cohesion +
                        0.05,

                    Flow =
                        intent.Flow +
                        0.08,

                    Focus =
                        intent.Focus +
                        0.10
                },


            SegaBodyState.Dragging =>
                intent with
                {
                    Energy =
                        intent.Energy +
                        0.06,

                    Cohesion =
                        intent.Cohesion +
                        0.12,

                    Tension =
                        intent.Tension +
                        0.04,

                    Focus =
                        intent.Focus +
                        0.14
                },


            _ =>
                intent
        };
    }


    // =========================================================
    // RESOLVE
    // =========================================================

    private SegaVisualIntent ResolveCurrent()
    {
        return
            _overrideIntent
            ??
            _automaticIntent;
    }


    // =========================================================
    // PUBLISH
    // =========================================================

    private void PublishIfChanged(
        SegaVisualIntent before,
        SegaVisualIntent after)
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
    // DISPOSE
    // =========================================================

    public void Dispose()
    {
        _state.StateChanged -=
            OnSegaStateChanged;


        _characterState.StateChanged -=
            OnCharacterStateChanged;
    }
}
