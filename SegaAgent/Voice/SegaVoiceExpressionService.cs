/*
 * filename: SegaVoiceExpressionService.cs
 */

using System.Diagnostics;

using SegaAgent.Character.Interaction;
using SegaAgent.Character.State;

namespace SegaAgent.Voice;

public sealed class SegaVoiceExpressionService
{
    private readonly SegaCharacterStateService
        _characterState;


    private readonly SegaAttitudeService
        _attitude;


    public SegaVoiceExpressionService(
        SegaCharacterStateService characterState,
        SegaAttitudeService attitude)
    {
        _characterState =
            characterState
            ?? throw new ArgumentNullException(
                nameof(characterState));


        _attitude =
            attitude
            ?? throw new ArgumentNullException(
                nameof(attitude));
    }


    public SegaVoiceExpression Resolve(
        SegaVocalIntent rawIntent,
        SegaInteractionContext? interaction)
    {
        SegaVocalIntent intent =
            rawIntent.Normalize();


        SegaCharacterSnapshot character =
            _characterState.Current;


        SegaRelationshipState relationship =
            character.Relationship;


        SegaMoodState mood =
            character.Mood;


        SegaAttitudeState attitude =
            _attitude.Evaluate(
                character,
                interaction);


        // =====================================================
        // WARMTH
        // =====================================================

        double warmth =
            attitude.Warmth *
                0.50
            +
            mood.Affection *
                0.20
            +
            intent.Warmth *
                0.30;


        // =====================================================
        // TENSION
        // =====================================================

        double tension =
            mood.Irritation *
                0.40
            +
            relationship.Friction *
                0.18
            +
            mood.Concern *
                0.12
            +
            intent.Tension *
                0.30;


        // =====================================================
        // CONFIDENCE
        // =====================================================

        double confidence =
            attitude.Assertiveness *
                0.55
            +
            relationship.Respect *
                0.15
            +
            intent.Confidence *
                0.30;


        // =====================================================
        // PLAYFULNESS
        // =====================================================

        double playfulness =
            attitude.Playfulness *
                0.50
            +
            mood.Amusement *
                0.20
            +
            intent.Playfulness *
                0.30;


        // =====================================================
        // TENDERNESS
        // =====================================================

        double tenderness =
            mood.Affection *
                0.35
            +
            attitude.Warmth *
                0.25
            +
            mood.Concern *
                0.10
            +
            intent.Tenderness *
                0.30;


        // =====================================================
        // IRRITATION / CONCERN / RESTRAINT
        // =====================================================

        double irritation =
            mood.Irritation *
                0.82
            +
            relationship.Friction *
                0.18;


        double concern =
            mood.Concern;


        double restraint =
            attitude.Restraint;


        double surprise =
            intent.Surprise;


        // =====================================================
        // VALENCE
        // =====================================================

        double valence =
            mood.Valence *
                0.72
            +
            (
                warmth -
                0.5
            ) *
                0.22
            +
            (
                playfulness -
                0.5
            ) *
                0.10
            -
            tension *
                0.08;


        // =====================================================
        // AROUSAL
        // =====================================================

        double arousal =
            mood.Energy *
                0.52
            +
            mood.Irritation *
                0.16
            +
            mood.Amusement *
                0.10
            +
            mood.Concern *
                0.06
            +
            intent.Energy *
                0.16;


        // =====================================================
        // PACE
        // =====================================================

        double pace =
            1.0
            +
            (
                arousal -
                0.5
            ) *
                0.16
            +
            (
                intent.Pace -
                1.0
            ) *
                0.50
            -
            restraint *
                0.05
            -
            tenderness *
                0.03;


        SegaVoiceExpression expression =
            new SegaVoiceExpression(
                Valence:
                    valence,

                Arousal:
                    arousal,

                Warmth:
                    warmth,

                Tension:
                    tension,

                Confidence:
                    confidence,

                Playfulness:
                    playfulness,

                Tenderness:
                    tenderness,

                Irritation:
                    irritation,

                Concern:
                    concern,

                Restraint:
                    restraint,

                Surprise:
                    surprise,

                Pace:
                    pace)
            .Normalize();


        Debug.WriteLine(
            $"[VoiceExpression] " +
            $"Valence={expression.Valence:F2} | " +
            $"Arousal={expression.Arousal:F2} | " +
            $"Warmth={expression.Warmth:F2} | " +
            $"Tension={expression.Tension:F2} | " +
            $"Confidence={expression.Confidence:F2} | " +
            $"Playfulness={expression.Playfulness:F2} | " +
            $"Tenderness={expression.Tenderness:F2} | " +
            $"Irritation={expression.Irritation:F2} | " +
            $"Concern={expression.Concern:F2} | " +
            $"Restraint={expression.Restraint:F2} | " +
            $"Surprise={expression.Surprise:F2} | " +
            $"Pace={expression.Pace:F2}");


        return expression;
    }
}