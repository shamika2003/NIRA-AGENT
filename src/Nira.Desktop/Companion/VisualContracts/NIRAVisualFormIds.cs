/*
 * filename: NIRAVisualFormIds.cs
 */

namespace NIRAAgent.Embodiment;

public static class NIRAVisualFormIds
{
    // =========================================================
    // CURRENT NIRA FORM
    // =========================================================

    public const string Orb =
        "NIRA.orb";


    // =========================================================
    // FUTURE GENERATED FORM ROUTES
    //
    // These remain reserved for the future visual/tool system.
    //
    // No provider currently implements them.
    //
    // The particle registry therefore safely falls back to Orb.
    // =========================================================

    public const string Procedural =
        "NIRA.procedural";


    public const string Generated =
        "NIRA.generated";
}
