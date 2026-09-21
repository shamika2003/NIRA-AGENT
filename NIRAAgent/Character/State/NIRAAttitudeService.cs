/*
 * filename: NIRAAttitudeService.cs
 */

using NIRAAgent.Character.Interaction;

namespace NIRAAgent.Character.State;

public sealed class NIRAAttitudeService
{
    public NIRAAttitudeState Evaluate(
        NIRACharacterSnapshot character,
        NIRAInteractionContext? interaction)
    {
        NIRARelationshipState relationship =
            character.Relationship;


        NIRAMoodState mood =
            character.Mood;


        NIRASituationState situation =
            character.Situation;


        double recurrence =
            interaction?
                .SemanticRecurrence
            ?? 0.0;


        double novelty =
            1.0 -
            recurrence;


        // =====================================================
        // WARMTH
        // =====================================================

        double warmth =
            relationship.Warmth *
                0.55
            +
            mood.Affection *
                0.30
            +
            Math.Max(
                0.0,
                mood.Valence) *
                0.15
            -
            relationship.Friction *
                0.30
            -
            mood.Irritation *
                0.20;


        // =====================================================
        // PATIENCE
        //
        // Repetition does not automatically mean anger.
        //
        // It does reduce novelty and can consume patience when
        // NIRA is already irritated or there is friction.
        // =====================================================

        double patience =
            0.82
            -
            mood.Irritation *
                0.52
            -
            relationship.Friction *
                0.36
            -
            recurrence *
                (
                    0.08
                    +
                    mood.Irritation *
                        0.22
                    +
                    relationship.Friction *
                        0.15
                );


        // =====================================================
        // PLAYFULNESS
        // =====================================================

        double playfulness =
            relationship.Playfulness *
                0.48
            +
            mood.Amusement *
                0.42
            +
            relationship.Warmth *
                0.10
            -
            relationship.Friction *
                0.22;


        // =====================================================
        // ENGAGEMENT
        //
        // Novel interactions increase engagement.
        //
        // Recurrence doesn't force disengagement, but repeated
        // low-novelty interaction naturally provides less new
        // stimulation.
        // =====================================================

        double engagement =
            0.22
            +
            mood.Curiosity *
                0.42
            +
            novelty *
                0.26
            +
            relationship.Attachment *
                0.10
            -
            mood.Irritation *
                0.08;


        // =====================================================
        // ASSERTIVENESS
        // =====================================================

        double assertiveness =
            0.58
            +
            relationship.Respect *
                0.12
            +
            mood.Irritation *
                0.20
            +
            relationship.Friction *
                0.10;


        // =====================================================
        // EMOTIONAL DISTANCE
        // =====================================================

        double closeness =
            relationship.Warmth *
                0.28
            +
            relationship.Trust *
                0.20
            +
            relationship.Attachment *
                0.24
            +
            relationship.Openness *
                0.14
            +
            relationship.Familiarity *
                0.14;


        double emotionalDistance =
            1.0 -
            closeness
            +
            relationship.Friction *
                0.30;


        // =====================================================
        // RESTRAINT
        //
        // Serious/focused situations reduce unnecessary
        // emotional performance.
        // =====================================================

        double restraint =
            situation.Mode switch
            {
                NIRAInteractionMode.FocusedWork =>
                    0.62 +
                    situation.Intensity *
                        0.28,

                NIRAInteractionMode.Serious =>
                    0.72 +
                    situation.Intensity *
                        0.24,

                NIRAInteractionMode.Sensitive =>
                    0.36,

                _ =>
                    0.20
            };


        return new NIRAAttitudeState(
            Warmth:
                warmth,

            Patience:
                patience,

            Playfulness:
                playfulness,

            Engagement:
                engagement,

            Assertiveness:
                assertiveness,

            EmotionalDistance:
                emotionalDistance,

            Restraint:
                restraint,

            Novelty:
                novelty)
            .Normalize();
    }
}
