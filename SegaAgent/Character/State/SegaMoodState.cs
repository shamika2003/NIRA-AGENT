/*
 * filename: SegaMoodState.cs
 */

namespace SegaAgent.Character.State;

public readonly record struct SegaMoodState(
    double Valence,
    double Energy,
    double Irritation,
    double Amusement,
    double Curiosity,
    double Affection,
    double Concern)
{
    // =========================================================
    // DEFAULT
    // =========================================================

    public static SegaMoodState Default =>
        new(
            Valence: 0.15,
            Energy: 0.45,
            Irritation: 0.00,
            Amusement: 0.20,
            Curiosity: 0.50,
            Affection: 0.10,
            Concern: 0.00);


    // =========================================================
    // NORMALIZE
    // =========================================================

    public SegaMoodState Normalize()
    {
        return this with
        {
            Valence =
                Math.Clamp(
                    Valence,
                    -1.0,
                    1.0),

            Energy =
                Clamp01(
                    Energy),

            Irritation =
                Clamp01(
                    Irritation),

            Amusement =
                Clamp01(
                    Amusement),

            Curiosity =
                Clamp01(
                    Curiosity),

            Affection =
                Clamp01(
                    Affection),

            Concern =
                Clamp01(
                    Concern)
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