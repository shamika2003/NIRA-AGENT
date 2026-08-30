/*
 * filename: SegaSocialMeaning.cs
 */

namespace SegaAgent.Character.Appraisal;


// =============================================================
// SOCIAL MEANING
//
// This describes what ONE interaction appears to communicate.
//
// It is NOT:
//
// - Sega's mood
// - Sega's relationship
// - Sega's response
//
// Example:
//
// User says something rude.
//
// This may produce:
//
// Hostility = 0.80
// Respect = -0.65
//
// The character engine later decides how much that actually
// affects Sega.
//
// A close relationship may absorb it differently from a new
// or already damaged relationship.
// =============================================================

public readonly record struct SegaSocialMeaning(
    double Respect,
    double Warmth,
    double Trust,
    double Appreciation,
    double Affection,
    double Playfulness,
    double Hostility,
    double Dismissal,
    double Repair,
    double Concern,
    double Engagement,
    double Pressure)
{
    // =========================================================
    // NEUTRAL
    // =========================================================

    public static SegaSocialMeaning Neutral =>
        new(
            Respect: 0.0,
            Warmth: 0.0,
            Trust: 0.0,
            Appreciation: 0.0,
            Affection: 0.0,
            Playfulness: 0.0,
            Hostility: 0.0,
            Dismissal: 0.0,
            Repair: 0.0,
            Concern: 0.0,
            Engagement: 0.0,
            Pressure: 0.0);


    // =========================================================
    // NORMALIZE
    //
    // Signed dimensions:
    //
    // Respect
    // Warmth
    // Trust
    //
    // -1.0 = strongly negative
    //  0.0 = neutral / absent
    // +1.0 = strongly positive
    //
    // Other dimensions represent strength only:
    //
    // 0.0 -> absent
    // 1.0 -> very strong
    // =========================================================

    public SegaSocialMeaning Normalize()
    {
        return this with
        {
            Respect =
                ClampSigned(
                    Respect),

            Warmth =
                ClampSigned(
                    Warmth),

            Trust =
                ClampSigned(
                    Trust),

            Appreciation =
                Clamp01(
                    Appreciation),

            Affection =
                Clamp01(
                    Affection),

            Playfulness =
                Clamp01(
                    Playfulness),

            Hostility =
                Clamp01(
                    Hostility),

            Dismissal =
                Clamp01(
                    Dismissal),

            Repair =
                Clamp01(
                    Repair),

            Concern =
                Clamp01(
                    Concern),

            Engagement =
                Clamp01(
                    Engagement),

            Pressure =
                Clamp01(
                    Pressure)
        };
    }


    // =========================================================
    // CLAMP SIGNED
    // =========================================================

    private static double ClampSigned(
        double value)
    {
        return Math.Clamp(
            value,
            -1.0,
            1.0);
    }


    // =========================================================
    // CLAMP 0 - 1
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