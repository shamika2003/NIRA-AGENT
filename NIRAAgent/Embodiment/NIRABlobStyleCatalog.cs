/*
 * filename: NIRABlobStyleCatalog.cs
 */

namespace NIRAAgent.Embodiment;

public static class NIRABlobStyleCatalog
{
    private static readonly IReadOnlyList<NIRABlobStyleOption> _all =
        new[]
        {
            new NIRABlobStyleOption(
                NIRABlobStyleId.EclipseGlass,
                "ECLIPSE GLASS",
                "Premium / Main UI",
                "Best default fit for NIRA main dashboard. Glass shell, premium glow, strong Elvara vibe.",
                NIRABlobPresetId.Core),

            new NIRABlobStyleOption(
                NIRABlobStyleId.HaloBloom,
                "HALO BLOOM",
                "Soft / Light Theme",
                "Brighter white halo and softer aura. Good for settings or light surfaces.",
                NIRABlobPresetId.Warm),

            new NIRABlobStyleOption(
                NIRABlobStyleId.OrbitFlow,
                "ORBIT FLOW",
                "Listening / Motion",
                "Multiple orbit ribbons and motion lines. Feels alive and responsive.",
                NIRABlobPresetId.Flow),

            new NIRABlobStyleOption(
                NIRABlobStyleId.LatticeCore,
                "LATTICE CORE",
                "Thinking / Precision",
                "Clean technical intelligence look with nodes and network lines.",
                NIRABlobPresetId.Focus),

            new NIRABlobStyleOption(
                NIRABlobStyleId.NebulaPulse,
                "NEBULA PULSE",
                "Expressive / Speaking",
                "Dense energetic atmosphere with stronger pulse and inner activity.",
                NIRABlobPresetId.Speak),

            new NIRABlobStyleOption(
                NIRABlobStyleId.QuietMinimal,
                "QUIET MINIMAL",
                "Calm / Resting",
                "Minimal luxury look with thin ring language and low-noise rendering.",
                NIRABlobPresetId.Calm)
        };

    public static IReadOnlyList<NIRABlobStyleOption> All =>
        _all;

    public static NIRABlobStyleOption Get(
        NIRABlobStyleId id)
    {
        foreach (NIRABlobStyleOption option in _all)
        {
            if (option.Id == id)
            {
                return option;
            }
        }

        return _all[0];
    }
}

