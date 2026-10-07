/*
 * filename: NIRACharacterDynamicsService.cs
 */

using Microsoft.Extensions.Hosting;

using NIRAAgent.Character.Appraisal;
using NIRAAgent.Character.Interaction;
using NIRAAgent.Character.State;
using System.Diagnostics;

namespace NIRAAgent.Character.Dynamics;

// Authoritative state transition for one committed social interaction.
//
// BeforeDecay is the stored state at the start of the turn.
// BeforeInteraction is that same state after ordinary runtime decay has been
// applied but BEFORE the current user's social act mutates it.
// After is the committed state after the current interaction.
//
// Keeping these phases separate prevents passive time decay from being mistaken
// for a reaction to the current message by presentation routing or episode logic.
public readonly record struct NIRACharacterTransition(
    NIRACharacterSnapshot BeforeDecay,
    NIRACharacterSnapshot BeforeInteraction,
    NIRACharacterSnapshot After)
{
    // Compatibility/readability alias: any code asking for the social "before"
    // state should compare against the pre-interaction snapshot, never pre-decay.
    public NIRACharacterSnapshot Before =>
        BeforeInteraction;
}


public sealed class NIRACharacterDynamicsService
    : BackgroundService
{
    private static readonly TimeSpan
        DecayCheckInterval =
            TimeSpan.FromMinutes(
                5);


    private readonly NIRACharacterStateService
        _state;


    private readonly object _sync =
        new();


    private DateTimeOffset _lastUpdateUtc;


    public NIRACharacterDynamicsService(
        NIRACharacterStateService state)
    {
        _state =
            state
            ?? throw new ArgumentNullException(
                nameof(state));


        // Application downtime is not lived emotional time.
        //
        // The snapshot loaded by NIRACharacterStateService is the exact character
        // state NIRA had when it was last committed. Starting the process must not
        // silently cool anger, erase concern, reduce affection, or repair friction
        // merely because the program was closed for a while.
        //
        // Runtime decay starts from NOW. Once NIRA is alive again, ordinary elapsed
        // time can naturally move transient mood toward its relationship-shaped
        // baseline.
        _lastUpdateUtc =
            DateTimeOffset.UtcNow;


        Debug.WriteLine(
            $"[CharacterContinuity] RUNTIME CLOCK STARTED | " +
            $"LoadedVersion={_state.Current.Version} | " +
            $"StoredAt={_state.Current.UpdatedAt:O} | " +
            "OfflineDecay=False");
    }


    public NIRACharacterTransition Apply(
        NIRAInteractionContext interaction,
        NIRAInteractionAppraisal appraisal)
    {
        ArgumentNullException.ThrowIfNull(
            interaction);


        ArgumentNullException.ThrowIfNull(
            appraisal);


        NIRAInteractionAppraisal normalized =
            appraisal.Normalize();


        DateTimeOffset now =
            DateTimeOffset.UtcNow;


        lock (_sync)
        {
            TimeSpan elapsed =
                ResolveElapsed(
                    now);


            NIRACharacterSnapshot beforeDecay =
                _state.Current;


            NIRACharacterSnapshot beforeInteraction =
                beforeDecay;


            _state.UpdateCharacter(
                current =>
                {
                    beforeInteraction =
                        ApplyDecay(
                            current,
                            elapsed)
                        .Normalize();


                    return ApplyInteraction(
                        beforeInteraction,
                        interaction,
                        normalized);
                });


            NIRACharacterSnapshot after =
                _state.Current;


            double logCertainty =
                Math.Clamp(
                    normalized.Confidence *
                    (
                        1.0 -
                        normalized.Ambiguity *
                            0.75
                    ),
                    0.0,
                    1.0);


            double logPositiveImpact =
                ResolvePositiveImpactScale(
                    normalized.Meaning,
                    logCertainty);


            double logNegativeImpact =
                ResolveNegativeImpactScale(
                    normalized.Meaning,
                    logCertainty,
                    interaction.SemanticRecurrence);


            Debug.WriteLine(
                $"[CharacterDynamics] " +
                $"Version {beforeDecay.Version}->{after.Version} | " +
                $"Elapsed={elapsed.TotalSeconds:F1}s | " +
                $"Impact +{logPositiveImpact:F3}/-{logNegativeImpact:F3} | " +
                $"Irritation " +
                $"{beforeInteraction.Mood.Irritation:F3}->" +
                $"{after.Mood.Irritation:F3} | " +
                $"Amusement " +
                $"{beforeInteraction.Mood.Amusement:F3}->" +
                $"{after.Mood.Amusement:F3} | " +
                $"Affection " +
                $"{beforeInteraction.Mood.Affection:F3}->" +
                $"{after.Mood.Affection:F3} | " +
                $"Warmth " +
                $"{beforeInteraction.Relationship.Warmth:F3}->" +
                $"{after.Relationship.Warmth:F3} | " +
                $"Trust " +
                $"{beforeInteraction.Relationship.Trust:F3}->" +
                $"{after.Relationship.Trust:F3} | " +
                $"Friction " +
                $"{beforeInteraction.Relationship.Friction:F3}->" +
                $"{after.Relationship.Friction:F3} | " +
                $"Situation " +
                $"{beforeInteraction.Situation.Mode}/" +
                $"{beforeInteraction.Situation.Intensity:F2}->" +
                $"{after.Situation.Mode}/" +
                $"{after.Situation.Intensity:F2}");


            _lastUpdateUtc =
                now;


            return new NIRACharacterTransition(
                beforeDecay,
                beforeInteraction,
                after);
        }
    }


    // =========================================================
    // APPLY LIVED INTERNAL EXPERIENCE
    //
    // Relationship state never changes here. This path exists so
    // meaningful work NIRA actually lives through can influence
    // mood/situation without pretending every primitive operation
    // is emotionally important. The model proposes a bounded
    // appraisal; this service owns the persistent mutation.
    // =========================================================

    public void ApplyExperience(
        NIRACharacterExperienceAppraisal appraisal)
    {
        ArgumentNullException.ThrowIfNull(
            appraisal);

        NIRACharacterExperienceAppraisal normalized =
            appraisal.Normalize();

        if (normalized.Significance <= 0.0
            || normalized.Confidence <= 0.0)
        {
            return;
        }

        DateTimeOffset now =
            DateTimeOffset.UtcNow;

        lock (_sync)
        {
            TimeSpan elapsed =
                ResolveElapsed(
                    now);

            NIRACharacterSnapshot before =
                _state.Current;

            _state.UpdateCharacter(
                current =>
                {
                    NIRACharacterSnapshot decayed =
                        ApplyDecay(
                            current,
                            elapsed);

                    return ApplyLivedExperience(
                        decayed,
                        normalized);
                });

            NIRACharacterSnapshot after =
                _state.Current;

            Debug.WriteLine(
                $"[CharacterExperience] " +
                $"Version {before.Version}->{after.Version} | " +
                $"Significance={normalized.Significance:F2} | " +
                $"Confidence={normalized.Confidence:F2} | " +
                $"Valence {before.Mood.Valence:F3}->{after.Mood.Valence:F3} | " +
                $"Irritation {before.Mood.Irritation:F3}->{after.Mood.Irritation:F3} | " +
                $"Concern {before.Mood.Concern:F3}->{after.Mood.Concern:F3} | " +
                $"Situation {before.Situation.Mode}/{before.Situation.Intensity:F2}->" +
                $"{after.Situation.Mode}/{after.Situation.Intensity:F2} | " +
                $"Reason='{TrimLog(normalized.Reason)}'");

            _lastUpdateUtc =
                now;
        }
    }


    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        DecayToNow();


        using PeriodicTimer timer =
            new(
                DecayCheckInterval);


        try
        {
            while (
                await timer.WaitForNextTickAsync(
                    stoppingToken))
            {
                DecayToNow();
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken
                .IsCancellationRequested)
        {
        }
    }


    private void DecayToNow()
    {
        DateTimeOffset now =
            DateTimeOffset.UtcNow;


        lock (_sync)
        {
            TimeSpan elapsed =
                ResolveElapsed(
                    now);


            if (elapsed <=
                TimeSpan.Zero)
            {
                return;
            }


            _state.UpdateCharacter(
                current =>
                    ApplyDecay(
                        current,
                        elapsed));


            _lastUpdateUtc =
                now;
        }
    }


    private TimeSpan ResolveElapsed(
        DateTimeOffset now)
    {
        if (now <=
            _lastUpdateUtc)
        {
            return TimeSpan.Zero;
        }


        return now -
            _lastUpdateUtc;
    }


    private static NIRACharacterSnapshot
        ApplyInteraction(
            NIRACharacterSnapshot current,
            NIRAInteractionContext interaction,
            NIRAInteractionAppraisal appraisal)
    {
        /*
         * Long-term relationship changes only come from
         * real user interaction.
         */

        bool userInteraction =
            interaction.Event.Source ==
                Character.History
                    .NIRASocialEventSource.User
            &&
            interaction.Event.Kind ==
                Character.History
                    .NIRASocialEventKind.UserMessage;


        NIRARelationshipState relationship =
            current.Relationship;


        NIRAMoodState mood =
            current.Mood;


        NIRASituationState situation =
            current.Situation;


        double certainty =
            Math.Clamp(
                appraisal.Confidence *
                (
                    1.0 -
                    appraisal.Ambiguity *
                    0.75
                ),
                0.0,
                1.0);


        NIRASocialMeaning meaning =
            appraisal.Meaning;


        double recurrence =
            interaction.SemanticRecurrence;


        double pressure =
            Math.Clamp(
                meaning.Pressure *
                (
                    1.0 +
                    recurrence *
                    0.75
                ),
                0.0,
                1.0);


        // Social state is deliberately asymmetric:
        //
        // - ordinary pleasant tone should barely move durable closeness or affection;
        // - an unusually meaningful positive interaction can still matter a lot;
        // - criticism/hostility may affect irritation immediately without destroying
        //   a strong relationship in one turn;
        // - repeated negative pressure can accumulate.
        //
        // The scales are derived from the structured appraisal, never from fixed
        // user-text phrases.
        double positiveImpact =
            ResolvePositiveImpactScale(
                meaning,
                certainty);


        double negativeImpact =
            ResolveNegativeImpactScale(
                meaning,
                certainty,
                recurrence);


        double concernImpact =
            ResolveConcernImpactScale(
                meaning,
                certainty);


        if (userInteraction)
        {
            relationship =
                ApplyRelationship(
                    relationship,
                    meaning,
                    recurrence,
                    positiveImpact,
                    negativeImpact);


            mood =
                ApplyMood(
                    mood,
                    meaning,
                    pressure,
                    recurrence,
                    appraisal.SituationMode,
                    positiveImpact,
                    negativeImpact,
                    concernImpact);
        }


        situation =
            ApplySituation(
                situation,
                appraisal,
                certainty);


        return current with
        {
            Relationship =
                relationship,

            Mood =
                mood,

            Situation =
                situation
        };
    }


    private static NIRACharacterSnapshot
        ApplyLivedExperience(
        NIRACharacterSnapshot current,
        NIRACharacterExperienceAppraisal appraisal)
    {
        double scale =
            Math.Clamp(
                appraisal.Significance
                *
                (
                    0.30
                    +
                    appraisal.Confidence
                    *
                    0.70
                ),
                0.0,
                1.0);

        if (scale <= 0.0)
        {
            return current;
        }

        NIRAMoodState mood =
            current.Mood with
            {
                Valence =
                    AddSigned(
                        current.Mood.Valence,
                        appraisal.ValenceImpact * 0.14 * scale),

                Energy =
                    Add01(
                        current.Mood.Energy,
                        appraisal.EnergyImpact * 0.10 * scale),

                Irritation =
                    Add01(
                        current.Mood.Irritation,
                        appraisal.IrritationImpact * 0.16 * scale),

                Amusement =
                    Add01(
                        current.Mood.Amusement,
                        appraisal.AmusementImpact * 0.10 * scale),

                Curiosity =
                    Add01(
                        current.Mood.Curiosity,
                        appraisal.CuriosityImpact * 0.10 * scale),

                Concern =
                    Add01(
                        current.Mood.Concern,
                        appraisal.ConcernImpact * 0.14 * scale)
            };

        double situationInfluence =
            Math.Clamp(
                0.12 + scale * 0.55,
                0.0,
                0.70);

        NIRASituationState situation =
            new(
                appraisal.SituationMode,
                Lerp(
                    current.Situation.Intensity,
                    appraisal.SituationIntensity,
                    situationInfluence));

        return current with
        {
            Mood =
                mood.Normalize(),

            Situation =
                situation.Normalize()
        };
    }


    private static NIRARelationshipState
        ApplyRelationship(
            NIRARelationshipState current,
            NIRASocialMeaning meaning,
            double recurrence,
            double positiveImpact,
            double negativeImpact)
    {
        // Familiarity is continuity, not affection. It rises very slowly from
        // simply spending real interaction time together and cannot saturate the
        // relationship in a short chat.
        double familiarityDelta =
            0.00030
            +
            meaning.Engagement *
                0.00055
            +
            (
                1.0 -
                recurrence
            )
            *
                0.00015;


        double trustDelta =
            positiveImpact *
            (
                Math.Max(
                    0.0,
                    meaning.Trust) *
                    0.0040
                +
                meaning.Repair *
                    0.0025
            )
            -
            negativeImpact *
            (
                Math.Max(
                    0.0,
                    -meaning.Trust) *
                    0.0060
                +
                meaning.Hostility *
                    0.0055
                +
                meaning.Dismissal *
                    0.0045
            );


        double warmthDelta =
            positiveImpact *
            (
                Math.Max(
                    0.0,
                    meaning.Warmth) *
                    0.0050
                +
                meaning.Appreciation *
                    0.0025
                +
                meaning.Affection *
                    0.0035
                +
                meaning.Repair *
                    0.0020
            )
            -
            negativeImpact *
            (
                Math.Max(
                    0.0,
                    -meaning.Warmth) *
                    0.0070
                +
                meaning.Hostility *
                    0.0060
                +
                meaning.Dismissal *
                    0.0050
            );


        double respectDelta =
            positiveImpact *
            (
                Math.Max(
                    0.0,
                    meaning.Respect) *
                    0.0045
                +
                meaning.Appreciation *
                    0.0020
                +
                meaning.Repair *
                    0.0015
            )
            -
            negativeImpact *
            (
                Math.Max(
                    0.0,
                    -meaning.Respect) *
                    0.0075
                +
                meaning.Hostility *
                    0.0050
                +
                meaning.Dismissal *
                    0.0050
                +
                meaning.Pressure *
                    0.0025
            );


        double attachmentPositive =
            positiveImpact *
            (
                meaning.Affection *
                    0.0030
                +
                meaning.Appreciation *
                    0.0012
                +
                Math.Max(
                    0.0,
                    meaning.Warmth) *
                    0.0008
            )
            *
            (
                0.30 +
                current.Trust *
                    0.70
            );


        double attachmentNegative =
            negativeImpact *
            (
                meaning.Hostility *
                    0.0035
                +
                meaning.Dismissal *
                    0.0030
            );


        double attachmentDelta =
            attachmentPositive -
            attachmentNegative;


        double opennessDelta =
            positiveImpact *
            (
                Math.Max(
                    0.0,
                    meaning.Warmth) *
                    0.0022
                +
                Math.Max(
                    0.0,
                    meaning.Trust) *
                    0.0025
                +
                meaning.Affection *
                    0.0018
                +
                meaning.Repair *
                    0.0030
            )
            -
            negativeImpact *
            (
                meaning.Hostility *
                    0.0045
                +
                meaning.Dismissal *
                    0.0045
                +
                Math.Max(
                    0.0,
                    -meaning.Trust) *
                    0.0030
            );


        double playfulnessDelta =
            positiveImpact *
            (
                meaning.Playfulness *
                    0.0045
                +
                meaning.Affection *
                    0.0010
            )
            -
            negativeImpact *
            (
                meaning.Hostility *
                    0.0030
                +
                meaning.Dismissal *
                    0.0025
            );


        // Friction is durable enough to survive a turn and influence future
        // patience, but one bad line still cannot erase an established bond.
        double frictionDelta =
            negativeImpact *
            (
                meaning.Hostility *
                    0.018
                +
                meaning.Dismissal *
                    0.014
                +
                meaning.Pressure *
                    (
                        0.008 +
                        recurrence *
                            0.008
                    )
                +
                Math.Max(
                    0.0,
                    -meaning.Respect) *
                    0.008
            )
            -
            positiveImpact *
            (
                meaning.Repair *
                    0.015
                +
                meaning.Appreciation *
                    0.002
                +
                meaning.Affection *
                    0.002
            );


        return new NIRARelationshipState(
            Familiarity:
                Add01(
                    current.Familiarity,
                    familiarityDelta),

            Trust:
                Add01(
                    current.Trust,
                    trustDelta),

            Warmth:
                Add01(
                    current.Warmth,
                    warmthDelta),

            Respect:
                Add01(
                    current.Respect,
                    respectDelta),

            Attachment:
                Add01(
                    current.Attachment,
                    attachmentDelta),

            Openness:
                Add01(
                    current.Openness,
                    opennessDelta),

            Playfulness:
                Add01(
                    current.Playfulness,
                    playfulnessDelta),

            Friction:
                Add01(
                    current.Friction,
                    frictionDelta));
    }


    private static NIRAMoodState ApplyMood(
        NIRAMoodState current,
        NIRASocialMeaning meaning,
        double pressure,
        double recurrence,
        NIRAInteractionMode mode,
        double positiveImpact,
        double negativeImpact,
        double concernImpact)
    {
        double focusScale =
            mode ==
                NIRAInteractionMode.FocusedWork
                ? 0.72
                : mode ==
                    NIRAInteractionMode.Serious
                    ? 0.84
                    : 1.0;


        double irritationDelta =
            focusScale *
            (
                negativeImpact *
                (
                    meaning.Hostility *
                        0.24
                    +
                    meaning.Dismissal *
                        0.20
                    +
                    pressure *
                        0.14
                    +
                    Math.Max(
                        0.0,
                        -meaning.Respect) *
                        0.10
                    +
                    recurrence *
                    (
                        meaning.Hostility *
                            0.08
                        +
                        meaning.Pressure *
                            0.08
                    )
                )
                -
                positiveImpact *
                (
                    meaning.Repair *
                        0.30
                    +
                    meaning.Affection *
                        0.04
                    +
                    meaning.Appreciation *
                        0.02
                )
            );


        double amusementDelta =
            positiveImpact *
            (
                meaning.Playfulness *
                    0.10
                +
                meaning.Affection *
                    0.015
            )
            -
            negativeImpact *
            (
                meaning.Hostility *
                    0.060
                +
                meaning.Dismissal *
                    0.030
            );


        // Affection is intentionally difficult to saturate. Generic warmth is
        // almost irrelevant; explicit/high-salience affection or appreciation
        // can still create a noticeable jump.
        double affectionDelta =
            positiveImpact *
            (
                meaning.Affection *
                    0.10
                +
                meaning.Appreciation *
                    0.040
                +
                Math.Max(
                    0.0,
                    meaning.Warmth) *
                    0.012
                +
                meaning.Repair *
                    0.025
            )
            -
            negativeImpact *
            (
                meaning.Hostility *
                    0.070
                +
                meaning.Dismissal *
                    0.060
                +
                Math.Max(
                    0.0,
                    -meaning.Warmth) *
                    0.050
            );


        double curiosityDelta =
            (
                meaning.Engagement *
                    0.015
                +
                (
                    1.0 -
                    recurrence
                )
                *
                    0.004
            )
            *
            (
                0.30 +
                Math.Max(
                    positiveImpact,
                    negativeImpact) *
                    0.70
            )
            -
            recurrence *
                0.006
            -
            negativeImpact *
                meaning.Dismissal *
                0.018;


        double concernDelta =
            concernImpact *
                meaning.Concern *
                0.22
            -
            positiveImpact *
                meaning.Repair *
                0.050;


        double positiveValence =
            positiveImpact *
            (
                Math.Max(
                    0.0,
                    meaning.Warmth) *
                    0.030
                +
                meaning.Appreciation *
                    0.050
                +
                meaning.Affection *
                    0.060
                +
                meaning.Playfulness *
                    0.025
                +
                meaning.Repair *
                    0.035
            );


        double negativeValence =
            negativeImpact *
            (
                meaning.Hostility *
                    0.100
                +
                meaning.Dismissal *
                    0.090
                +
                pressure *
                    0.060
                +
                Math.Max(
                    0.0,
                    -meaning.Warmth) *
                    0.040
            );


        double valenceDelta =
            positiveValence -
            negativeValence;


        double energyDelta =
            positiveImpact *
            (
                meaning.Engagement *
                    0.020
                +
                meaning.Playfulness *
                    0.025
                +
                meaning.Appreciation *
                    0.010
            )
            +
            negativeImpact *
            (
                meaning.Hostility *
                    0.020
                +
                pressure *
                    0.015
                -
                meaning.Dismissal *
                    0.015
            );


        return new NIRAMoodState(
            Valence:
                AddSigned(
                    current.Valence,
                    valenceDelta),

            Energy:
                Add01(
                    current.Energy,
                    energyDelta),

            Irritation:
                Add01(
                    current.Irritation,
                    irritationDelta),

            Amusement:
                Add01(
                    current.Amusement,
                    amusementDelta),

            Curiosity:
                Add01(
                    current.Curiosity,
                    curiosityDelta),

            Affection:
                Add01(
                    current.Affection,
                    affectionDelta),

            Concern:
                Add01(
                    current.Concern,
                    concernDelta));
    }


    // =========================================================
    // SOCIAL IMPACT SCALING
    //
    // Normal friendliness should shape tone without speed-running emotional
    // saturation. Strong explicit social events can still matter immediately.
    // These functions operate only on the model's structured appraisal and do
    // not inspect or classify fixed user phrases.
    // =========================================================

    private static double ResolvePositiveImpactScale(
        NIRASocialMeaning meaning,
        double certainty)
    {
        double primary =
            Math.Max(
                Math.Max(
                    meaning.Appreciation,
                    meaning.Affection),
                Math.Max(
                    meaning.Repair,
                    meaning.Concern *
                        0.65));


        double tonal =
            Math.Max(
                Math.Max(
                    Math.Max(
                        0.0,
                        meaning.Warmth),
                    Math.Max(
                        0.0,
                        meaning.Trust)),
                Math.Max(
                    Math.Max(
                        0.0,
                        meaning.Respect),
                    meaning.Playfulness));


        double salience =
            Math.Clamp(
                Math.Max(
                    primary,
                    tonal *
                        0.28),
                0.0,
                1.0);


        if (salience <=
            0.0001)
        {
            return 0.0;
        }


        // Squaring suppresses ordinary low/medium social tone while preserving
        // a path for genuinely strong moments to matter.
        return Math.Clamp(
            certainty *
            (
                0.04 +
                0.96 *
                    salience *
                    salience
            ),
            0.0,
            1.0);
    }


    private static double ResolveNegativeImpactScale(
        NIRASocialMeaning meaning,
        double certainty,
        double recurrence)
    {
        double primary =
            Math.Max(
                Math.Max(
                    meaning.Hostility,
                    meaning.Dismissal),
                Math.Max(
                    meaning.Pressure,
                    Math.Max(
                        Math.Max(
                            0.0,
                            -meaning.Respect),
                        Math.Max(
                            Math.Max(
                                0.0,
                                -meaning.Warmth),
                            Math.Max(
                                0.0,
                                -meaning.Trust)))));


        if (primary <=
            0.0001)
        {
            return 0.0;
        }


        // Negative social pressure is allowed to register sooner than ordinary
        // positive tone. Repetition only amplifies an already-negative appraisal.
        double recurrenceGain =
            1.0 +
            recurrence *
                0.20 *
                Math.Max(
                    Math.Max(
                        meaning.Hostility,
                        meaning.Dismissal),
                    meaning.Pressure);


        return Math.Clamp(
            certainty *
            (
                0.28 +
                0.72 *
                    primary
            )
            *
            recurrenceGain,
            0.0,
            1.0);
    }


    private static double ResolveConcernImpactScale(
        NIRASocialMeaning meaning,
        double certainty)
    {
        double concern =
            Math.Clamp(
                meaning.Concern,
                0.0,
                1.0);


        if (concern <=
            0.0001)
        {
            return 0.0;
        }


        return Math.Clamp(
            certainty *
            (
                0.10 +
                0.90 *
                    concern *
                    concern
            ),
            0.0,
            1.0);
    }


    private static NIRASituationState
        ApplySituation(
            NIRASituationState current,
            NIRAInteractionAppraisal appraisal,
            double certainty)
    {
        double influence =
            Math.Clamp(
                0.30 +
                certainty *
                0.60,
                0.0,
                0.90);


        double intensity =
            Lerp(
                current.Intensity,
                appraisal.SituationIntensity,
                influence);


        return new NIRASituationState(
            appraisal.SituationMode,
            intensity);
    }


    private static NIRACharacterSnapshot ApplyDecay(
        NIRACharacterSnapshot current,
        TimeSpan elapsed)
    {
        if (elapsed <=
            TimeSpan.Zero)
        {
            return current;
        }


        NIRARelationshipState relationship =
            current.Relationship;


        relationship =
            relationship with
            {
                Friction =
                    DecayToward(
                        relationship.Friction,
                        0.0,
                        elapsed,
                        TimeSpan.FromHours(
                            18))
            };


        double targetAffection =
            Math.Clamp(
                0.05 +
                relationship.Warmth *
                relationship.Attachment *
                0.45,
                0.0,
                1.0);


        double targetAmusement =
            relationship.Playfulness *
            0.15;


        double targetValence =
            Math.Clamp(
                (
                    relationship.Warmth -
                    relationship.Friction
                )
                *
                0.28,
                -1.0,
                1.0);


        NIRAMoodState mood =
            current.Mood with
            {
                Valence =
                    DecayToward(
                        current.Mood.Valence,
                        targetValence,
                        elapsed,
                        TimeSpan.FromMinutes(
                            35)),

                Energy =
                    DecayToward(
                        current.Mood.Energy,
                        0.45,
                        elapsed,
                        TimeSpan.FromMinutes(
                            40)),

                Irritation =
                    DecayToward(
                        current.Mood.Irritation,
                        relationship.Friction *
                            0.24,
                        elapsed,
                        TimeSpan.FromMinutes(
                            40)),

                Amusement =
                    DecayToward(
                        current.Mood.Amusement,
                        targetAmusement,
                        elapsed,
                        TimeSpan.FromMinutes(
                            16)),

                Curiosity =
                    DecayToward(
                        current.Mood.Curiosity,
                        0.50,
                        elapsed,
                        TimeSpan.FromMinutes(
                            45)),

                Affection =
                    DecayToward(
                        current.Mood.Affection,
                        targetAffection,
                        elapsed,
                        TimeSpan.FromMinutes(
                            100)),

                Concern =
                    DecayToward(
                        current.Mood.Concern,
                        0.0,
                        elapsed,
                        TimeSpan.FromMinutes(
                            30))
            };


        double situationIntensity =
            DecayToward(
                current.Situation.Intensity,
                0.0,
                elapsed,
                TimeSpan.FromMinutes(
                    25));


        NIRASituationState situation =
            situationIntensity <
                0.12
                ? new NIRASituationState(
                    NIRAInteractionMode.Casual,
                    0.10)
                : current.Situation with
                {
                    Intensity =
                        situationIntensity
                };


        return current with
        {
            Relationship =
                relationship.Normalize(),

            Mood =
                mood.Normalize(),

            Situation =
                situation.Normalize()
        };
    }


    private static double DecayToward(
        double current,
        double target,
        TimeSpan elapsed,
        TimeSpan halfLife)
    {
        if (halfLife <=
            TimeSpan.Zero)
        {
            return target;
        }


        double factor =
            Math.Exp(
                -Math.Log(
                    2.0)
                *
                elapsed.TotalSeconds /
                halfLife.TotalSeconds);


        return target +
            (
                current -
                target
            )
            *
            factor;
    }


    private static double Add01(
        double value,
        double delta)
    {
        // Positive experiences have diminishing impact near saturation;
        // negative experiences can still move the state away from the ceiling.
        // No hard cap such as 0.8 or canned mood reset is required.
        value = Math.Clamp(value, 0.0, 1.0);
        return delta >= 0.0
            ? value + (1.0 - value) * (1.0 - Math.Exp(-delta))
            : value * Math.Exp(delta);
    }


    private static double AddSigned(
        double value,
        double delta)
    {
        value = Math.Clamp(value, -1.0, 1.0);
        return delta >= 0.0
            ? value + (1.0 - value) * (1.0 - Math.Exp(-delta))
            : value - (1.0 + value) * (1.0 - Math.Exp(delta));
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

    private static string TrimLog(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        const int maximumLength = 180;
        string clean = value.Trim();

        return clean.Length <= maximumLength
            ? clean
            : clean[..maximumLength] + "...";
    }

}
