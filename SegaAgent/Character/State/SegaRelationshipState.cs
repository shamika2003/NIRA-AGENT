/*
 * filename: SegaRelationshipState.cs
 */

namespace SegaAgent.Character.State;

public readonly record struct SegaRelationshipState(
    double Familiarity,
    double Trust,
    double Warmth,
    double Respect,
    double Attachment,
    double Openness,
    double Playfulness,
    double Friction)
{
    // =========================================================
    // DEFAULT
    //
    // Sega begins as a capable personal companion.
    //
    // She does not begin emotionally close to the user,
    // hostile to the user, or artificially attached.
    //
    // Relationship development happens later through
    // interaction.
    // =========================================================

    public static SegaRelationshipState Default =>
        new(
            Familiarity: 0.10,
            Trust: 0.50,
            Warmth: 0.35,
            Respect: 0.60,
            Attachment: 0.05,
            Openness: 0.25,
            Playfulness: 0.25,
            Friction: 0.00);


    // =========================================================
    // NORMALIZE
    // =========================================================

    public SegaRelationshipState Normalize()
    {
        return this with
        {
            Familiarity =
                Clamp01(
                    Familiarity),

            Trust =
                Clamp01(
                    Trust),

            Warmth =
                Clamp01(
                    Warmth),

            Respect =
                Clamp01(
                    Respect),

            Attachment =
                Clamp01(
                    Attachment),

            Openness =
                Clamp01(
                    Openness),

            Playfulness =
                Clamp01(
                    Playfulness),

            Friction =
                Clamp01(
                    Friction)
        };
    }


    // =========================================================
    // CLAMP
    // =========================================================

    private static double Clamp01(
        double value)
    {
        return Math.Clamp(
            value,
            0.0,
            1.0);
    }
}