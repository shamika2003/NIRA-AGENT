/*
 * filename: SegaSituationState.cs
 */

namespace SegaAgent.Character.State;


// =============================================================
// INTERACTION MODE
// =============================================================

public enum SegaInteractionMode
{
    Casual,

    FocusedWork,

    Serious,

    Sensitive
}


// =============================================================
// SITUATION STATE
// =============================================================

public readonly record struct SegaSituationState(
    SegaInteractionMode Mode,
    double Intensity)
{
    public static SegaSituationState Default =>
        new(
            SegaInteractionMode.Casual,
            0.20);


    public SegaSituationState Normalize()
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