/*
 * filename: SegaVocalIntent.cs
 */

namespace SegaAgent.Voice;

public readonly record struct SegaVocalIntent(
    double Warmth,
    double Energy,
    double Tension,
    double Playfulness,
    double Confidence,
    double Tenderness,
    double Surprise,
    double Pace)
{
    public static SegaVocalIntent Default =>
        new(
            Warmth: 0.45,
            Energy: 0.45,
            Tension: 0.20,
            Playfulness: 0.20,
            Confidence: 0.70,
            Tenderness: 0.15,
            Surprise: 0.00,
            Pace: 1.00);


    public SegaVocalIntent Normalize()
    {
        return this with
        {
            Warmth =
                Clamp01(
                    Warmth),

            Energy =
                Clamp01(
                    Energy),

            Tension =
                Clamp01(
                    Tension),

            Playfulness =
                Clamp01(
                    Playfulness),

            Confidence =
                Clamp01(
                    Confidence),

            Tenderness =
                Clamp01(
                    Tenderness),

            Surprise =
                Clamp01(
                    Surprise),

            Pace =
                Math.Clamp(
                    Pace,
                    0.75,
                    1.25)
        };
    }


    private static double Clamp01(
        double value)
    {
        return Math.Clamp(
            value,
            0.0,
            1.0);
    }
}