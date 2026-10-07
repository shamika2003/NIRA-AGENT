/*
 * filename: NIRACharacterDeliveryPolicy.cs
 */

using NIRAAgent.Character.Appraisal;
using NIRAAgent.Character.Dynamics;
using NIRAAgent.Character.Interaction;
using NIRAAgent.Character.State;

namespace NIRAAgent.AI.Cognition;

// Deterministic terminal presentation policy.
//
// This never inspects user text and never owns character state. It compares the
// authoritative PRE-INTERACTION and POST-INTERACTION snapshots produced by
// NIRACharacterDynamicsService, so passive runtime decay can never masquerade as
// a reaction to the current user message.
// Response-realization routing is intentionally NOT based on these deltas. A first-call
// terminal Natural reply is emitted directly; multi-model-call runs get one final response
// build. This assessment remains diagnostic evidence for logs/tests showing whether the
// current social act materially changed NIRA's authoritative delivery state.
internal static class NIRACharacterDeliveryPolicy
{
    public static NIRACharacterDeliveryAssessment Assess(
        NIRACharacterTransition transition,
        NIRAInteractionContext? interaction,
        NIRAInteractionAppraisal? appliedAppraisal = null)
    {
        NIRACharacterSnapshot before =
            transition.BeforeInteraction.Normalize();

        NIRACharacterSnapshot after =
            transition.After.Normalize();

        List<string> reasons = new();
        double maximumDelta = 0.0;

        void Check(
            string name,
            double beforeValue,
            double afterValue,
            double threshold)
        {
            double delta =
                Math.Abs(
                    afterValue -
                    beforeValue);

            maximumDelta =
                Math.Max(
                    maximumDelta,
                    delta);

            if (delta >= threshold)
            {
                reasons.Add(
                    $"{name}:{delta:F3}");
            }
        }

        // A situation-mode change is categorical, not merely numeric. A response
        // drafted in Casual mode should not be treated as final after the interaction
        // moved NIRA into Serious, Sensitive or FocusedWork (or vice versa).
        if (before.Situation.Mode !=
            after.Situation.Mode)
        {
            reasons.Add(
                $"SituationMode:{before.Situation.Mode}->{after.Situation.Mode}");
        }

        Check(
            "SituationIntensity",
            before.Situation.Intensity,
            after.Situation.Intensity,
            0.16);

        // Immediate mood shifts can materially change wording even when the durable
        // relationship barely moves. Irritation/concern/affection use a lower bar than
        // amusement because they more strongly alter social stance and boundaries.
        Check(
            "Irritation",
            before.Mood.Irritation,
            after.Mood.Irritation,
            0.05);

        Check(
            "Concern",
            before.Mood.Concern,
            after.Mood.Concern,
            0.07);

        Check(
            "Affection",
            before.Mood.Affection,
            after.Mood.Affection,
            0.075);

        Check(
            "Amusement",
            before.Mood.Amusement,
            after.Mood.Amusement,
            0.10);

        Check(
            "Valence",
            before.Mood.Valence,
            after.Mood.Valence,
            0.10);

        // Relationship state intentionally evolves slowly, so materially meaningful
        // durable changes have smaller absolute magnitudes than mood changes.
        Check(
            "Friction",
            before.Relationship.Friction,
            after.Relationship.Friction,
            0.0075);

        Check(
            "Warmth",
            before.Relationship.Warmth,
            after.Relationship.Warmth,
            0.010);

        Check(
            "Trust",
            before.Relationship.Trust,
            after.Relationship.Trust,
            0.010);

        Check(
            "Respect",
            before.Relationship.Respect,
            after.Relationship.Respect,
            0.010);

        // Attitude is derived from authoritative character state. Re-evaluating the
        // same interaction against before/after snapshots lets the policy detect a
        // material change in delivery dimensions without duplicating attitude formulas.
        NIRAAttitudeService attitudeService =
            new();

        NIRAAttitudeState beforeAttitude =
            attitudeService.Evaluate(
                before,
                interaction);

        NIRAAttitudeState afterAttitude =
            attitudeService.Evaluate(
                after,
                interaction);

        Check(
            "AttitudeWarmth",
            beforeAttitude.Warmth,
            afterAttitude.Warmth,
            0.07);

        Check(
            "Patience",
            beforeAttitude.Patience,
            afterAttitude.Patience,
            0.07);

        Check(
            "Playfulness",
            beforeAttitude.Playfulness,
            afterAttitude.Playfulness,
            0.08);

        Check(
            "Assertiveness",
            beforeAttitude.Assertiveness,
            afterAttitude.Assertiveness,
            0.055);

        Check(
            "EmotionalDistance",
            beforeAttitude.EmotionalDistance,
            afterAttitude.EmotionalDistance,
            0.07);

        Check(
            "Restraint",
            beforeAttitude.Restraint,
            afterAttitude.Restraint,
            0.08);

        // A strong current social act can warrant fresh final wording even when
        // saturation/diminishing-return math intentionally keeps the persistent state
        // delta small. This still uses only structured, source-grounded appraisal;
        // it never inspects user text.
        if (appliedAppraisal !=
            null)
        {
            NIRAInteractionAppraisal social =
                appliedAppraisal.Normalize();


            NIRASocialMeaning meaning =
                social.Meaning;


            void CheckSocialHigh(
                string name,
                double value,
                double threshold)
            {
                if (value >=
                    threshold)
                {
                    reasons.Add(
                        $"{name}:{value:F2}");
                }
            }


            void CheckSocialLow(
                string name,
                double value,
                double threshold)
            {
                if (value <=
                    threshold)
                {
                    reasons.Add(
                        $"{name}:{value:F2}");
                }
            }


            CheckSocialHigh(
                "CurrentHostility",
                meaning.Hostility,
                0.25);

            CheckSocialHigh(
                "CurrentDismissal",
                meaning.Dismissal,
                0.25);

            CheckSocialHigh(
                "CurrentPressure",
                meaning.Pressure,
                0.55);

            CheckSocialLow(
                "CurrentRespect",
                meaning.Respect,
                -0.30);

            CheckSocialLow(
                "CurrentWarmth",
                meaning.Warmth,
                -0.35);

            CheckSocialHigh(
                "CurrentRepair",
                meaning.Repair,
                0.70);

            CheckSocialHigh(
                "CurrentAffection",
                meaning.Affection,
                0.80);

            CheckSocialHigh(
                "CurrentAppreciation",
                meaning.Appreciation,
                0.90);

            CheckSocialHigh(
                "CurrentConcern",
                meaning.Concern,
                0.80);
        }


        if (reasons.Count == 0)
        {
            return new NIRACharacterDeliveryAssessment(
                RequiresRealization: false,
                Reason: "StableCharacterState",
                MaximumDelta: maximumDelta);
        }

        return new NIRACharacterDeliveryAssessment(
            RequiresRealization: true,
            Reason:
                string.Join(
                    ",",
                    reasons.Take(5)),
            MaximumDelta: maximumDelta);
    }
}


internal readonly record struct NIRACharacterDeliveryAssessment(
    bool RequiresRealization,
    string Reason,
    double MaximumDelta);
