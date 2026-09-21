/*
 * filename: NIRAAttitudeState.cs
 */

namespace NIRAAgent.Character.State;

public readonly record struct NIRAAttitudeState(
    double Warmth,
    double Patience,
    double Playfulness,
    double Engagement,
    double Assertiveness,
    double EmotionalDistance,
    double Restraint,
    double Novelty)
{
    public NIRAAttitudeState Normalize()
    {
        return new NIRAAttitudeState(
            Warmth:
                Clamp01(
                    Warmth),

            Patience:
                Clamp01(
                    Patience),

            Playfulness:
                Clamp01(
                    Playfulness),

            Engagement:
                Clamp01(
                    Engagement),

            Assertiveness:
                Clamp01(
                    Assertiveness),

            EmotionalDistance:
                Clamp01(
                    EmotionalDistance),

            Restraint:
                Clamp01(
                    Restraint),

            Novelty:
                Clamp01(
                    Novelty));
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
