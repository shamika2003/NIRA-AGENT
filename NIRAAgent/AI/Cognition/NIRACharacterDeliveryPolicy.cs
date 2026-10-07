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
// Normal first-call terminal Natural replies are still emitted directly. This assessment
// is also the narrow deterministic gate for a post-commit realization when the CURRENT
// interaction materially changed NIRA's authoritative delivery state. That keeps ordinary
// replies at one model call while preventing a pre-commit draft from hiding a real social
// transition such as hostility, repair, affection, concern or a material mood/attitude shift.
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

        // Situation mode/intensity are already proposed by cognition from THIS
        // current event, so a Casual<->FocusedWork transition alone must not turn
        // ordinary work into a two-model-call path. Post-commit realization is reserved
        // for delivery state cognition could not authoritatively know until after commit
        // (mood/relationship/derived attitude) or for strong structured social acts.
        maximumDelta =
            Math.Max(
                maximumDelta,
                Math.Abs(
                    after.Situation.Intensity -
                    before.Situation.Intensity));

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