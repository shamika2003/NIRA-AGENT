/*
 * filename: SegaVisualFormIds.cs
 */

namespace SegaAgent.Embodiment;

public static class SegaVisualFormIds
{
    // =========================================================
    // CURRENT SEGA FORM
    // =========================================================

    public const string Orb =
        "sega.orb";


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
        "sega.procedural";


    public const string Generated =
        "sega.generated";
}