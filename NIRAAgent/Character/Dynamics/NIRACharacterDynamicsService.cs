/*
 * filename: NIRACharacterDynamicsService.cs
 */

using Microsoft.Extensions.Hosting;

using NIRAAgent.Character.Appraisal;
using NIRAAgent.Character.Interaction;
using NIRAAgent.Character.State;
using System.Diagnostics;

namespace NIRAAgent.Character.Dynamics;

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


        DateTimeOffset now =
            DateTimeOffset.UtcNow;


        DateTimeOffset stored =
            _state
                .Current
                .UpdatedAt;


        _lastUpdateUtc =
            stored == default
                ? now
                : stored > now
                    ? now
                    : stored;
    }


    public void Apply(
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

            NIRACharacterSnapshot before =
                _state.Current;


            _state.UpdateCharacter(
                current =>
                {
                    NIRACharacterSnapshot decayed =
                        ApplyDecay(
                            current,
                            elapsed);


                    return ApplyInteraction(
                        decayed,
                        interaction,
                        normalized);
                });

            NIRACharacterSnapshot after =
                _state.Current;


            Debug.WriteLine(
                $"[CharacterDynamics] " +
                $"Version {before.Version}->{after.Version} | " +
                $"Irritation " +
                $"{before.Mood.Irritation:F3}->" +
                $"{after.Mood.Irritation:F3} | " +
                $"Amusement " +
                $"{before.Mood.Amusement:F3}->" +
                $"{after.Mood.Amusement:F3} | " +
                $"Affection " +
                $"{before.Mood.Affection:F3}->" +
                $"{after.Mood.Affection:F3} | " +
                $"Warmth " +
                $"{before.Relationship.Warmth:F3}->" +
                $"{after.Relationship.Warmth:F3} | " +
                $"Trust " +
                $"{before.Relationship.Trust:F3}->" +
                $"{after.Relationship.Trust:F3} | " +
                $"Friction " +
                $"{before.Relationship.Friction:F3}->" +
                $"{after.Relationship.Friction:F3} | " +
                $"Situation " +
                $"{before.Situation.Mode}/" +
                $"{before.Situation.Intensity:F2}->" +
                $"{after.Situation.Mode}/" +
                $"{after.Situation.Intensity:F2}");

            _lastUpdateUtc =
                now;
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


        if (userInteraction)
        {
            relationship =
                ApplyRelationship(
                    relationship,
                    meaning,
                    recurrence,
                    certainty);


            mood =
                ApplyMood(
                    mood,
                    meaning,
                    pressure,
                    recurrence,
                    appraisal.SituationMode,
                    certainty);
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
            double certainty)
    {
        double familiarityDelta =
            0.0015 +
            meaning.Engagement *
            0.0035 +
            recurrence *
            0.0008;


        double trustDelta =
            certainty *
            (
                meaning.Trust *
                    0.012
                +
                meaning.Repair *
                    0.004
                -
                meaning.Hostility *
                    0.012
                -
                meaning.Dismissal *
                    0.008
            );


        double warmthDelta =
            certainty *
            (
                meaning.Warmth *
                    0.014
                +
                meaning.Appreciation *
                    0.005
                +
                meaning.Affection *
                    0.007
                +
                meaning.Repair *
                    0.004
                -
                meaning.Hostility *
                    0.012
                -
                meaning.Dismissal *
                    0.010
            );


        double respectDelta =
            certainty *
            (
                meaning.Respect *
                    0.014
                +
                meaning.Appreciation *
                    0.003
                +
                meaning.Repair *
                    0.002
                -
                meaning.Hostility *
                    0.009
                -
                meaning.Dismissal *
                    0.008
                -
                meaning.Pressure *
                    0.004
            );


        double attachmentPositive =
            (
                meaning.Affection *
                    0.005
                +
                meaning.Appreciation *
                    0.002
                +
                Math.Max(
                    0.0,
                    meaning.Warmth) *
                    0.002
            )
            *
            (
                0.35 +
                current.Trust *
                0.65
            );


        double attachmentNegative =
            meaning.Hostility *
                0.004
            +
            meaning.Dismissal *
                0.003;


        double attachmentDelta =
            certainty *
            (
                attachmentPositive -
                attachmentNegative
            );


        double opennessDelta =
            certainty *
            (
                Math.Max(
                    0.0,
                    meaning.Warmth) *
                    0.005
                +
                Math.Max(
                    0.0,
                    meaning.Trust) *
                    0.005
                +
                meaning.Affection *
                    0.003
                +
                meaning.Repair *
                    0.006
                -
                meaning.Hostility *
                    0.007
                -
                meaning.Dismissal *
                    0.007
            );


        double playfulnessDelta =
            certainty *
            (
                meaning.Playfulness *
                    0.009
                +
                meaning.Affection *
                    0.002
                -
                meaning.Hostility *
                    0.004
                -
                meaning.Dismissal *
                    0.003
            );


        double frictionDelta =
            certainty *
            (
                meaning.Hostility *
                    0.018
                +
                meaning.Dismissal *
                    0.014
                +
                meaning.Pressure *
                    (
                        0.010 +
                        recurrence *
                        0.006
                    )
                -
                meaning.Repair *
                    0.022
                -
                meaning.Appreciation *
                    0.003
                -
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
        double certainty)
    {
        double focusScale =
            mode ==
                NIRAInteractionMode.FocusedWork
                ? 0.68
                : mode ==
                    NIRAInteractionMode.Serious
                    ? 0.80
                    : 1.0;


        double irritationDelta =
            certainty *
            focusScale *
            (
                meaning.Hostility *
                    0.22
                +
                meaning.Dismissal *
                    0.18
                +
                pressure *
                    0.16
                +
                recurrence *
                (
                    meaning.Hostility *
                        0.08
                    +
                    meaning.Pressure *
                        0.10
                )
                -
                meaning.Repair *
                    0.28
                -
                meaning.Affection *
                    0.04
            );


        double amusementDelta =
            certainty *
            (
                meaning.Playfulness *
                    0.20
                +
                recurrence *
                    meaning.Playfulness *
                    0.12
                -
                meaning.Hostility *
                    0.08
            );


        double affectionDelta =
            certainty *
            (
                meaning.Affection *
                    0.18
                +
                Math.Max(
                    0.0,
                    meaning.Warmth) *
                    0.08
                +
                meaning.Appreciation *
                    0.05
                +
                meaning.Repair *
                    0.04
                -
                meaning.Hostility *
                    0.10
                -
                meaning.Dismissal *
                    0.08
            );


        double curiosityDelta =
            certainty *
            (
                meaning.Engagement *
                    0.055
                +
                (
                    1.0 -
                    recurrence
                )
                *
                    0.018
                -
                recurrence *
                    0.025
                -
                meaning.Dismissal *
                    0.025
            );


        double concernDelta =
            certainty *
            (
                meaning.Concern *
                    0.22
                -
                meaning.Repair *
                    0.04
            );


        double positiveValence =
            Math.Max(
                0.0,
                meaning.Warmth) *
                0.10
            +
            meaning.Appreciation *
                0.08
            +
            meaning.Affection *
                0.10
            +
            meaning.Playfulness *
                0.055
            +
            meaning.Repair *
                0.055;


        double negativeValence =
            meaning.Hostility *
                0.14
            +
            meaning.Dismissal *
                0.12
            +
            pressure *
                0.07;


        double valenceDelta =
            certainty *
            (
                positiveValence -
                negativeValence
            );


        double energyDelta =
            certainty *
            (
                meaning.Engagement *
                    0.045
                +
                meaning.Playfulness *
                    0.035
                +
                meaning.Hostility *
                    0.025
                -
                meaning.Dismissal *
                    0.025
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
        return Math.Clamp(
            value +
            delta,
            0.0,
            1.0);
    }


    private static double AddSigned(
        double value,
        double delta)
    {
        return Math.Clamp(
            value +
            delta,
            -1.0,
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
