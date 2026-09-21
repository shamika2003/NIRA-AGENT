/*
 * filename: ParticleDensityProfile.cs
 */

namespace NIRAAgent.UI.Companion.Particles;

// =============================================================
// PARTICLE DENSITY PROFILE
//
// The live engine still owns the total particle count. This profile
// only assigns stable size-band proportions to those particles.
// Later we can introduce alternate density profiles without changing
// the renderer contract or the form-provider architecture.
// =============================================================

public sealed record ParticleDensityProfile(
    double TinyRatio,
    double NormalRatio,
    double LargeRatio)
{
    public static ParticleDensityProfile Balanced { get; } =
        new(
            TinyRatio: 0.40,
            NormalRatio: 0.45,
            LargeRatio: 0.15);

    public ParticleSizeBand ResolveBand(
        int stableSeed)
    {
        int bucket =
            (int)(
                (uint)stableSeed %
                10_000U);

        double unit =
            bucket /
            10_000.0;

        double tinyEnd =
            Math.Clamp(
                TinyRatio,
                0.0,
                1.0);

        double normalEnd =
            Math.Clamp(
                tinyEnd +
                NormalRatio,
                tinyEnd,
                1.0);

        if (unit < tinyEnd)
        {
            return ParticleSizeBand.Tiny;
        }

        if (unit < normalEnd)
        {
            return ParticleSizeBand.Normal;
        }

        return ParticleSizeBand.Large;
    }
}

