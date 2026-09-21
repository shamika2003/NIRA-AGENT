/*
 * filename: NIRASituationState.cs
 */

namespace NIRAAgent.Character.State;


// =============================================================
// INTERACTION MODE
// =============================================================

public enum NIRAInteractionMode
{
    Casual,

    FocusedWork,

    Serious,

    Sensitive
}


// =============================================================
// SITUATION STATE
// =============================================================

public readonly record struct NIRASituationState(
    NIRAInteractionMode Mode,
    double Intensity)
{
    public static NIRASituationState Default =>
        new(
            NIRAInteractionMode.Casual,
            0.20);


    public NIRASituationState Normalize()
    {
        return this with
        {
            Intensity =
                Math.Clamp(
                    Intensity,
                    0.0,
                    1.0)
        };
    }
}
