/*
 * filename: NIRAVisualExpressionIds.cs
 */

namespace NIRAAgent.Embodiment;

public static class NIRAVisualExpressionIds
{
    // No transient formation. NIRA remains her normal living orb.
    public const string None =
        "";


    // Current implemented particle expression:
    // the particles briefly resolve the word NIRA.
    public const string IdentityMark =
        "NIRA.identity-mark";


    // Reserved generic route for future richer particle expressions.
    //
    // This deliberately does not hard-code a face/body shape.
    // A future procedural provider can decide what NIRA wants to form.
    public const string Procedural =
        "NIRA.expression.procedural";
}

