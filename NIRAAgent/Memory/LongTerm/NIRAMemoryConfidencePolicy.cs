/*
 * filename: NIRAMemoryConfidencePolicy.cs
 */

namespace NIRAAgent.Memory.LongTerm;


// =============================================================
// MEMORY CONFIDENCE POLICY
//
// Stored confidence represents the evidence NIRA had when the
// durable memory was formed/reinforced.
//
// Retrieval confidence is allowed to age conservatively for
// weaker derived sources without rewriting the authoritative
// stored proposition or its original confidence.
//
// Explicit user statements and imported authoritative records do
// not decay merely because time passed.
// =============================================================

public static class NIRAMemoryConfidencePolicy
{
    public static double CalculateEffectiveConfidence(
        NIRAMemoryRecord memory,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(
            memory);


        DateTimeOffset reference =
            now ?? DateTimeOffset.UtcNow;


        TimeSpan age =
            reference -
            memory.UpdatedAt;


        if (age <
            TimeSpan.Zero)
        {
            age =
                TimeSpan.Zero;
        }


        return CalculateEffectiveConfidence(
            memory,
            age);
    }


    public static double CalculateEffectiveConfidence(
        NIRAMemoryRecord memory,
        TimeSpan age)
    {
        ArgumentNullException.ThrowIfNull(
            memory);


        double stored =
            Math.Clamp(
                memory.Confidence,
                0.0,
                1.0);


        if (
            memory.Provenance.SourceType ==
                NIRAMemorySourceType.UserExplicit
            ||
            memory.Provenance.SourceType ==
                NIRAMemorySourceType.Imported)
        {
            return stored;
        }


        if (age <
            TimeSpan.Zero)
        {
            age =
                TimeSpan.Zero;
        }


        (TimeSpan grace,
         TimeSpan halfLife,
         double floorFactor) =
            memory.Provenance.SourceType switch
            {
                NIRAMemorySourceType.SharedExperience =>
                    (
                        TimeSpan.FromDays(
                            365),
                        TimeSpan.FromDays(
                            1460),
                        0.75
                    ),

                NIRAMemorySourceType.SystemDerived =>
                    (
                        TimeSpan.FromDays(
                            180),
                        TimeSpan.FromDays(
                            730),
                        0.65
                    ),

                NIRAMemorySourceType.NIRAInference =>
                    (
                        TimeSpan.FromDays(
                            120),
                        TimeSpan.FromDays(
                            540),
                        0.55
                    ),

                _ =>
                    (
                        TimeSpan.FromDays(
                            90),
                        TimeSpan.FromDays(
                            365),
                        0.50
                    )
            };


        if (age <=
            grace)
        {
            return stored;
        }


        TimeSpan decayAge =
            age -
            grace;


        double decay =
            Math.Exp(
                -Math.Log(
                    2.0)
                *
                decayAge.TotalSeconds
                /
                Math.Max(
                    1.0,
                    halfLife.TotalSeconds));


        double floor =
            stored *
            floorFactor;


        double aged =
            floor +
            (
                stored -
                floor
            )
            *
            decay;


        // Repeated independent reinforcement protects a memory
        // from fading as aggressively, but never increases it
        // beyond the stored confidence.
        double reinforcementProtection =
            Math.Min(
                1.0,
                Math.Log(
                    1.0 +
                    Math.Max(
                        0,
                        memory.ReinforcementCount))
                /
                4.0);


        double protectedConfidence =
            aged +
            (
                stored -
                aged
            )
            *
            reinforcementProtection
            *
            0.35;


        return Math.Clamp(
            protectedConfidence,
            0.0,
            stored);
    }
}

