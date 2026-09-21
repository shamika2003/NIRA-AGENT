/*
 * filename: NIRAVoiceExpression.cs
 */

namespace NIRAAgent.Voice;

public readonly record struct NIRAVoiceExpression(
    double Valence,
    double Arousal,
    double Warmth,
    double Tension,
    double Confidence,
    double Playfulness,
    double Tenderness,
    double Irritation,
    double Concern,
    double Restraint,
    double Surprise,
    double Pace)
{
    public static NIRAVoiceExpression Neutral =>
        new(
            Valence: 0.0,
            Arousal: 0.45,
            Warmth: 0.45,
            Tension: 0.20,
            Confidence: 0.70,
            Playfulness: 0.20,
            Tenderness: 0.15,
            Irritation: 0.00,
            Concern: 0.00,
            Restraint: 0.55,
            Surprise: 0.00,
            Pace: 1.00);


    public NIRAVoiceExpression Normalize()
    {
        return this with
        {
            Valence =
                Math.Clamp(
                    Valence,
                    -1.0,
                    1.0),

            Arousal =
                Clamp01(
                    Arousal),

            Warmth =
                Clamp01(
                    Warmth),

            Tension =
                Clamp01(
                    Tension),

            Confidence =
                Clamp01(
                    Confidence),

            Playfulness =
                Clamp01(
                    Playfulness),

            Tenderness =
                Clamp01(
                    Tenderness),

            Irritation =
                Clamp01(
                    Irritation),

            Concern =
                Clamp01(
                    Concern),

            Restraint =
                Clamp01(
                    Restraint),

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
