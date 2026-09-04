/*
 * filename: OrbParticleFormProvider.cs
 */

using System.Numerics;

using SegaAgent.Embodiment;

namespace SegaAgent.UI.Companion.Particles;

public sealed class OrbParticleFormProvider
    : IParticleFormProvider
{
    // =========================================================
    // FORM
    // =========================================================

    public string FormId =>
        SegaVisualFormIds.Orb;


    // =========================================================
    // GOLDEN ANGLE
    // =========================================================

    private const double GoldenAngle =
        Math.PI *
        (
            3.0 -
            2.2360679774997896964
        );


    // =========================================================
    // BUILD
    //
    // Sega's orb is intentionally never static.
    //
    // The form combines:
    //
    // stable volumetric distribution
    // slow breathing
    // internal circulation
    // energy-driven life
    // tension-driven surface strain
    // focus-driven control
    // presence-driven brightness
    //
    // None of these dimensions represent a fixed emotion.
    // =========================================================

    public void BuildTargets(
        ParticleTarget[] targets,
        SegaVisualIntent intent,
        double time)
    {
        ArgumentNullException.ThrowIfNull(
            targets);


        SegaVisualIntent normalized =
            intent.Normalize();


        int count =
            targets.Length;


        if (count ==
            0)
        {
            return;
        }


        double globalPulse =
            Math.Sin(
                time *
                (
                    0.68 +
                    normalized.Pulse *
                        1.18
                ))
            *
            (
                0.006 +
                normalized.Pulse *
                    0.018
            );


        double horizontalFocusScale =
            1.0 -
            normalized.Focus *
                0.025;


        double verticalFocusScale =
            1.0 +
            normalized.Focus *
                0.035;


        for (
            int index = 0;
            index < count;
            index++)
        {
            double normalizedIndex =
                (
                    index +
                    0.5
                )
                /
                count;


            double y =
                1.0 -
                normalizedIndex *
                    2.0;


            double horizontal =
                Math.Sqrt(
                    Math.Max(
                        0.0,
                        1.0 -
                        y *
                        y));


            double baseAngle =
                index *
                GoldenAngle;


            ParticleRole role =
                ResolveRole(
                    index);


            double roleMotion =
                ResolveMotionInfluence(
                    role);


            // =================================================
            // VOLUMETRIC DISTRIBUTION
            // =================================================

            double radialSeed =
                Fract(
                    index *
                    0.7548776662466927);


            double radius =
                0.18 +
                Math.Pow(
                    radialSeed,
                    0.42)
                *
                0.82;


            // =================================================
            // PARTICLE PHASE
            // =================================================

            double phase =
                index *
                0.137;


            // =================================================
            // INTERNAL CIRCULATION
            //
            // Flow rotates different depth layers at slightly
            // different rates so the orb feels internally alive
            // rather than like a rigid spinning shell.
            // =================================================

            double flowAngle =
                time *
                (
                    0.025 +
                    normalized.Flow *
                        0.16
                )
                *
                (
                    0.35 +
                    radialSeed *
                        0.65
                )
                +
                Math.Sin(
                    time *
                        0.31
                    +
                    phase *
                        0.37)
                *
                normalized.Flow *
                    0.045;


            double angle =
                baseAngle +
                flowAngle;


            // =================================================
            // LIVING MICRO MOTION
            //
            // Focus suppresses random-looking motion while still
            // preserving subtle life.
            // =================================================

            double life =
                Math.Sin(
                    time *
                    (
                        0.52 +
                        normalized.Energy *
                            1.05
                    )
                    +
                    phase)
                *
                (
                    0.008 +
                    normalized.Energy *
                        0.017
                )
                *
                (
                    1.0 -
                    normalized.Focus *
                        0.55
                );


            // =================================================
            // TENSION
            //
            // Tension adds controlled surface strain. Core
            // particles move less than surface/halo particles so
            // Sega stays structurally coherent.
            // =================================================

            double tensionRipple =
                Math.Sin(
                    baseAngle *
                        3.0
                    +
                    time *
                    (
                        1.15 +
                        normalized.Tension *
                            2.10
                    )
                    +
                    phase *
                        0.43)
                *
                normalized.Tension
                *
                (
                    0.004 +
                    normalized.Tension *
                        0.010
                )
                *
                roleMotion
                *
                (
                    1.0 -
                    normalized.Focus *
                        0.42
                );


            // =================================================
            // FLOW WAVE
            // =================================================

            double flowWave =
                Math.Sin(
                    time *
                    (
                        0.38 +
                        normalized.Flow *
                            0.82
                    )
                    +
                    phase *
                        1.71)
                *
                normalized.Flow
                *
                0.007
                *
                roleMotion;


            radius =
                radius *
                (
                    1.0 +
                    globalPulse
                )
                +
                life
                +
                tensionRipple
                +
                flowWave;


            radius =
                Math.Clamp(
                    radius,
                    0.10,
                    1.20);


            float x =
                (float)(
                    Math.Cos(
                        angle)
                    *
                    horizontal
                    *
                    radius
                    *
                    horizontalFocusScale);


            float z =
                (float)(
                    Math.Sin(
                        angle)
                    *
                    horizontal
                    *
                    radius
                    *
                    horizontalFocusScale);


            float finalY =
                (float)(
                    y
                    *
                    radius
                    *
                    verticalFocusScale);


            Vector3 position =
                new(
                    x,
                    finalY,
                    z);


            if (role ==
                ParticleRole.Halo)
            {
                position *=
                    (float)(
                        1.07 +
                        normalized.Presence *
                            0.045);
            }


            // =================================================
            // MATERIAL
            // =================================================

            ParticlePalette palette =
                ResolvePalette(
                    index,
                    role);


            float baseSize =
                role switch
                {
                    ParticleRole.Accent =>
                        1.20f,

                    ParticleRole.Core =>
                        0.92f,

                    ParticleRole.Halo =>
                        0.48f,

                    _ =>
                        0.70f
                };


            float baseBrightness =
                role switch
                {
                    ParticleRole.Accent =>
                        1.00f,

                    ParticleRole.Core =>
                        0.82f,

                    ParticleRole.Halo =>
                        0.26f,

                    _ =>
                        0.58f
                };


            double sizeGain =
                0.94 +
                normalized.Energy *
                    0.06
                +
                normalized.Presence *
                    0.04;


            double presenceGain =
                0.58 +
                normalized.Presence *
                    0.58;


            double pulseGlow =
                1.0 +
                globalPulse *
                    2.4;


            float size =
                (float)(
                    baseSize *
                    sizeGain);


            float brightness =
                (float)Math.Clamp(
                    baseBrightness *
                    presenceGain *
                    pulseGlow,
                    0.05,
                    1.15);


            targets[index] =
                new ParticleTarget(
                    position,
                    size,
                    brightness,
                    role,
                    palette);
        }
    }


    // =========================================================
    // ROLE
    // =========================================================

    private static ParticleRole ResolveRole(
        int index)
    {
        int value =
            Math.Abs(
                index *
                37)
            %
            100;


        if (value <
            8)
        {
            return
                ParticleRole.Accent;
        }


        if (value <
            24)
        {
            return
                ParticleRole.Core;
        }


        if (value >=
            88)
        {
            return
                ParticleRole.Halo;
        }


        return
            ParticleRole.Surface;
    }


    // =========================================================
    // MOTION INFLUENCE
    // =========================================================

    private static double ResolveMotionInfluence(
        ParticleRole role)
    {
        return role switch
        {
            ParticleRole.Core =>
                0.34,

            ParticleRole.Accent =>
                0.76,

            ParticleRole.Halo =>
                1.22,

            _ =>
                1.00
        };
    }


    // =========================================================
    // PALETTE
    // =========================================================

    private static ParticlePalette ResolvePalette(
        int index,
        ParticleRole role)
    {
        if (role ==
            ParticleRole.Accent)
        {
            return
                ParticlePalette.White;
        }


        int value =
            Math.Abs(
                index *
                53)
            %
            100;


        if (value <
            72)
        {
            return
                ParticlePalette.Cyan;
        }


        if (value <
            94)
        {
            return
                ParticlePalette.Blue;
        }


        return
            ParticlePalette.Violet;
    }


    // =========================================================
    // FRACT
    // =========================================================

    private static double Fract(
        double value)
    {
        return
            value -
            Math.Floor(
                value);
    }
}
