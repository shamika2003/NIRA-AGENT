/*
 * filename: OrbParticleFormProvider.cs
 */

using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Media;

using NIRAAgent.Embodiment;

namespace NIRAAgent.UI.Companion.Particles;

public sealed class OrbParticleFormProvider
    : IParticleFormProvider
{
    // =========================================================
    // FORM
    // =========================================================

    public string FormId =>
        NIRAVisualFormIds.Orb;


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
    // LIVING VOLUME
    //
    // The blob is not a shell.
    //
    // Its apparent boundary is created only by probability:
    //
    // center -> dense
    // middle -> medium
    // outside -> sparse
    // a few particles -> drifting beyond the main body
    // =========================================================

    private const double AmbientRadiusFloor =
        0.025;

    private const double AmbientRadiusRange =
        0.995;

    private const double AmbientRadiusExponent =
        1.34;


    // =========================================================
    // NIRA PARTICLE MASK
    //
    // Existing particle sizes are preserved.
    //
    // Surface = tiny
    // Core    = medium
    // Accent  = large/highlight
    //
    // The word is created by moving a subset of those existing
    // particles into a real font-shaped mask.
    //
    // Nothing is rendered as text. The font geometry is used only
    // as an invisible target mask for normal NIRA particles.
    // =========================================================

    private const double TextSurfaceParticipation =
        0.36;

    private const double TextCoreParticipation =
        0.86;

    private const double TextAccentParticipation =
        0.88;


    // Tiny text particles still breathe and move, but stay strongly
    // attracted so the word remains readable.
    private const float TextSurfaceBlend =
        0.92f;

    private const float TextCoreBlend =
        0.965f;

    private const float TextAccentBlend =
        0.992f;


    // Word size inside the existing orb.
    //
    // This does NOT change NIRA's body/window size.
    private const double TextWidth =
        1.48;

    private const double TextHeight =
        0.56;


    // =========================================================
    // TEXT GEOMETRY
    //
    // Segoe UI Bold gives a normal clean bold sans-serif shape.
    //
    // This geometry is NEVER drawn.
    //
    // It is only queried while generating stable particle targets.
    // =========================================================

    private static readonly Geometry TextGeometry =
        BuildTextGeometry();

    private static readonly Rect TextGeometryBounds =
        TextGeometry.Bounds;

    private static readonly Pen TextEdgePen =
        BuildTextEdgePen();


    // =========================================================
    // CACHED TEXT LAYOUT
    //
    // Font hit-testing is done only when particle count changes.
    //
    // The 60 Hz animation path reuses these normalized target
    // positions, so the living motion stays cheap.
    // =========================================================

    private TextParticleSample[]
        _textLayout =
            Array.Empty<TextParticleSample>();


    // =========================================================
    // BUILD
    // =========================================================

    public void BuildTargets(
        ParticleTarget[] targets,
        NIRAVisualIntent intent,
        double time)
    {
        ArgumentNullException.ThrowIfNull(
            targets);


        NIRAVisualIntent normalized =
            intent.Normalize();


        int count =
            targets.Length;


        if (count ==
            0)
        {
            return;
        }


        EnsureTextLayout(
            count);


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
            // TRUE VOLUME DISTRIBUTION
            //
            // Previous versions biased too many particles toward
            // the outer radius, which could make NIRA read as a
            // circular shell.
            //
            // A stronger exponent produces a denser interior and
            // progressively fewer particles near the outside.
            // =================================================

            double radialSeed =
                Fract(
                    index *
                    0.7548776662466927);


            double radius =
                AmbientRadiusFloor +
                Math.Pow(
                    radialSeed,
                    AmbientRadiusExponent)
                *
                AmbientRadiusRange;


            // =================================================
            // ORGANIC SHAPE NOISE
            //
            // This avoids a mathematically perfect circular edge.
            // =================================================

            double shapeNoise =
                0.965 +
                Math.Sin(
                    baseAngle *
                        3.17
                    +
                    y *
                        2.31
                    +
                    time *
                        0.055)
                *
                0.032
                +
                Math.Sin(
                    baseAngle *
                        1.43
                    -
                    y *
                        5.10
                    +
                    time *
                        0.031)
                *
                0.018;


            radius *=
                shapeNoise;


            // =================================================
            // PARTICLE PHASE
            // =================================================

            double phase =
                index *
                0.137;


            // =================================================
            // INTERNAL CIRCULATION
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
                    0.006 +
                    normalized.Energy *
                        0.014
                )
                *
                (
                    1.0 -
                    normalized.Focus *
                        0.55
                );


            // =================================================
            // TENSION
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
                    0.003 +
                    normalized.Tension *
                        0.008
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
                0.006
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
                    0.02,
                    1.16);


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


            // =================================================
            // SPARSE OUTLIERS
            //
            // Only a small fraction of tiny Halo particles drift
            // beyond the main volume.
            //
            // This creates an organic fade instead of an outer ring.
            // =================================================

            if (role ==
                ParticleRole.Halo)
            {
                double outlierSeed =
                    Hash01(
                        index,
                        901);


                if (outlierSeed >
                    0.68)
                {
                    double extra =
                        (
                            outlierSeed -
                            0.68
                        )
                        /
                        0.32
                        *
                        0.18;


                    position *=
                        (float)(
                            1.0 +
                            extra);
                }
            }


            // =================================================
            // NIRA PARTICLE ORGANIZATION
            //
            // Tiny + medium particles form the interior density.
            //
            // Large Accent particles are rare and concentrate near
            // glyph boundaries.
            //
            // The word exists in the front-middle Z layer.
            // =================================================

            TextParticleSample textSample =
                _textLayout[index];


            double identityStrength =
                string.Equals(
                    normalized.ExpressionId,
                    NIRAVisualExpressionIds.IdentityMark,
                    StringComparison.OrdinalIgnoreCase)
                    ? SmoothStep01(
                        normalized.ExpressionStrength)
                    : 0.0;


            if (
                textSample.IsText
                &&
                identityStrength >
                    0.001)
            {
                Vector3 textPosition =
                    BuildTextPosition(
                        index,
                        role,
                        textSample,
                        normalized,
                        time,
                        globalPulse);


                float textBlend =
                    role switch
                    {
                        ParticleRole.Accent =>
                            TextAccentBlend,

                        ParticleRole.Core =>
                            TextCoreBlend,

                        _ =>
                            TextSurfaceBlend
                    };


                textBlend *=
                    (float)identityStrength;


                position =
                    Vector3.Lerp(
                        position,
                        textPosition,
                        textBlend);
            }


            // =================================================
            // MATERIAL
            //
            // Particle core sizes remain unchanged from the current
            // NIRA blob.
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
                        0.58f,

                    _ =>
                        0.80f
                };


            float baseBrightness =
                role switch
                {
                    ParticleRole.Accent =>
                        0.94f,

                    ParticleRole.Core =>
                        0.76f,

                    ParticleRole.Halo =>
                        0.30f,

                    _ =>
                        0.62f
                };


            // Text is defined by density first, not by turning every
            // text particle white.
            if (
                textSample.IsText
                &&
                identityStrength >
                    0.001)
            {
                float textBrightnessBoost =
                    role switch
                    {
                        ParticleRole.Accent =>
                            textSample.IsEdge
                                ? 0.12f
                                : 0.06f,

                        ParticleRole.Core =>
                            textSample.IsEdge
                                ? 0.10f
                                : 0.07f,

                        _ =>
                            textSample.IsEdge
                                ? 0.07f
                                : 0.045f
                    };


                baseBrightness +=
                    textBrightnessBoost
                    *
                    (float)identityStrength;
            }


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
                    2.1;


            float size =
                (float)(
                    baseSize *
                    sizeGain);


            float brightness =
                (float)Math.Clamp(
                    baseBrightness *
                    presenceGain *
                    pulseGlow,
                    0.04,
                    1.12);


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
    // TEXT LAYOUT
    // =========================================================

    private void EnsureTextLayout(
        int count)
    {
        if (_textLayout.Length ==
            count)
        {
            return;
        }


        TextParticleSample[] layout =
            new TextParticleSample[
                count];


        for (
            int index = 0;
            index < count;
            index++)
        {
            ParticleRole role =
                ResolveRole(
                    index);


            if (!ShouldJoinText(
                    index,
                    role))
            {
                continue;
            }


            bool preferEdge =
                ShouldPreferTextEdge(
                    index,
                    role);


            if (!TrySampleTextPoint(
                    index,
                    preferEdge,
                    out Vector2 normalizedPoint,
                    out bool sampledEdge))
            {
                continue;
            }


            layout[index] =
                new TextParticleSample(
                    true,
                    normalizedPoint,
                    sampledEdge);
        }


        _textLayout =
            layout;
    }


    // =========================================================
    // TEXT PARTICIPATION
    // =========================================================

    private static bool ShouldJoinText(
        int index,
        ParticleRole role)
    {
        double seed =
            Hash01(
                index,
                117);


        return role switch
        {
            ParticleRole.Accent =>
                seed <
                    TextAccentParticipation,

            ParticleRole.Core =>
                seed <
                    TextCoreParticipation,

            ParticleRole.Surface =>
                seed <
                    TextSurfaceParticipation,

            _ =>
                false
        };
    }


    // =========================================================
    // TEXT EDGE PREFERENCE
    //
    // Large particles mainly live near letter boundaries.
    //
    // Core/tiny particles mostly fill the interior.
    // =========================================================

    private static bool ShouldPreferTextEdge(
        int index,
        ParticleRole role)
    {
        double seed =
            Hash01(
                index,
                211);


        return role switch
        {
            ParticleRole.Accent =>
                true,

            ParticleRole.Core =>
                seed <
                    0.30,

            ParticleRole.Surface =>
                seed <
                    0.08,

            _ =>
                false
        };
    }


    // =========================================================
    // SAMPLE FONT MASK
    // =========================================================

    private static bool TrySampleTextPoint(
        int index,
        bool preferEdge,
        out Vector2 normalizedPoint,
        out bool sampledEdge)
    {
        const int MaxAttempts =
            144;


        for (
            int attempt = 0;
            attempt < MaxAttempts;
            attempt++)
        {
            double xSeed =
                Hash01(
                    index,
                    1000 +
                    attempt *
                        2);


            double ySeed =
                Hash01(
                    index,
                    1001 +
                    attempt *
                        2);


            Point candidate =
                new(
                    TextGeometryBounds.Left +
                        xSeed *
                            TextGeometryBounds.Width,

                    TextGeometryBounds.Top +
                        ySeed *
                            TextGeometryBounds.Height);


            bool inside =
                TextGeometry.FillContains(
                    candidate);


            if (!inside)
            {
                continue;
            }


            bool onEdge =
                TextGeometry.StrokeContains(
                    TextEdgePen,
                    candidate);


            if (
                preferEdge
                &&
                !onEdge)
            {
                continue;
            }


            double normalizedX =
                (
                    (
                        candidate.X -
                        TextGeometryBounds.Left
                    )
                    /
                    TextGeometryBounds.Width
                    -
                    0.5
                )
                *
                TextWidth;


            double normalizedY =
                (
                    (
                        candidate.Y -
                        TextGeometryBounds.Top
                    )
                    /
                    TextGeometryBounds.Height
                    -
                    0.5
                )
                *
                TextHeight;


            normalizedPoint =
                new Vector2(
                    (float)normalizedX,
                    (float)normalizedY);


            sampledEdge =
                onEdge;


            return true;
        }


        // Edge rejection is intentionally strict. If a rare Accent
        // particle failed to find an edge sample, retry as interior
        // rather than dropping the particle from the word.
        if (preferEdge)
        {
            return TrySampleTextPoint(
                index +
                    100000,
                false,
                out normalizedPoint,
                out sampledEdge);
        }


        normalizedPoint =
            Vector2.Zero;


        sampledEdge =
            false;


        return false;
    }


    // =========================================================
    // TEXT POSITION
    //
    // The cached X/Y point stays stable enough to keep NIRA clear.
    //
    // Z and microscopic drift remain alive every frame.
    // =========================================================

    private static Vector3 BuildTextPosition(
        int index,
        ParticleRole role,
        TextParticleSample sample,
        NIRAVisualIntent intent,
        double time,
        double globalPulse)
    {
        double x =
            sample.LocalPoint.X;


        double y =
            sample.LocalPoint.Y;


        double depthSeed =
            Hash01(
                index,
                401)
            *
            2.0
            -
            1.0;


        double zCenter =
            role switch
            {
                ParticleRole.Accent =>
                    0.18,

                ParticleRole.Core =>
                    0.135,

                _ =>
                    0.095
            };


        double zRange =
            role switch
            {
                ParticleRole.Accent =>
                    0.024,

                ParticleRole.Core =>
                    0.048,

                _ =>
                    0.075
            };


        double z =
            zCenter +
            depthSeed *
                zRange;


        // ---------------------------------------------------------
        // CALM LIVING DRIFT
        // ---------------------------------------------------------

        double driftAmount =
            role switch
            {
                ParticleRole.Accent =>
                    0.0025,

                ParticleRole.Core =>
                    0.0045,

                _ =>
                    0.0065
            };


        driftAmount *=
            (
                0.70 +
                intent.Energy *
                    0.45
            )
            *
            (
                1.0 -
                intent.Focus *
                    0.52
            );


        double phase =
            index *
                0.193
            +
            time *
            (
                0.28 +
                intent.Flow *
                    0.34
            );


        x +=
            Math.Sin(
                phase)
            *
            driftAmount;


        y +=
            Math.Cos(
                phase *
                    0.83)
            *
            driftAmount *
                0.72;


        z +=
            Math.Sin(
                phase *
                    0.61)
            *
            driftAmount *
                1.30;


        // Whole organism breathes together.
        double textPulse =
            1.0 +
            globalPulse *
                0.44;


        x *=
            textPulse;


        y *=
            textPulse;


        return
            new Vector3(
                (float)x,
                (float)y,
                (float)z);
    }


    // =========================================================
    // FONT GEOMETRY
    // =========================================================

    private static Geometry BuildTextGeometry()
    {
        Typeface typeface =
            new(
                new FontFamily(
                    "Segoe UI"),

                FontStyles.Normal,

                FontWeights.Bold,

                FontStretches.Normal);


        FormattedText formatted =
            new(
                "NIRA",

                CultureInfo.InvariantCulture,

                FlowDirection.LeftToRight,

                typeface,

                100.0,

                Brushes.White,

                1.0);


        Geometry geometry =
            formatted.BuildGeometry(
                new Point(
                    0.0,
                    0.0));


        if (geometry.CanFreeze)
        {
            geometry.Freeze();
        }


        return geometry;
    }


    private static Pen BuildTextEdgePen()
    {
        Pen pen =
            new(
                Brushes.White,
                5.5);


        if (pen.CanFreeze)
        {
            pen.Freeze();
        }


        return pen;
    }


    // =========================================================
    // ROLE
    //
    // Large particles are intentionally rare.
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
            4)
        {
            return
                ParticleRole.Accent;
        }


        if (value <
            22)
        {
            return
                ParticleRole.Core;
        }


        if (value >=
            90)
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
                0.72,

            ParticleRole.Halo =>
                1.18,

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
            // Accent is rare enough that a near-white core reads as
            // an occasional highlight instead of dominating NIRA.
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
            76)
        {
            return
                ParticlePalette.Cyan;
        }


        if (value <
            96)
        {
            return
                ParticlePalette.Blue;
        }


        return
            ParticlePalette.Violet;
    }


    // =========================================================
    // SMOOTH STEP
    // =========================================================

    private static double SmoothStep01(
        double value)
    {
        value =
            Math.Clamp(
                value,
                0.0,
                1.0);


        return
            value *
            value *
            (
                3.0 -
                2.0 *
                value
            );
    }


    // =========================================================
    // HASH
    // =========================================================

    private static double Hash01(
        int index,
        int salt)
    {
        unchecked
        {
            uint value =
                (uint)(
                    index +
                    1)
                *
                0x9E3779B9u
                +
                (uint)(
                    salt +
                    1)
                *
                0x85EBCA6Bu;


            value ^=
                value >>
                16;


            value *=
                0x7FEB352Du;


            value ^=
                value >>
                15;


            value *=
                0x846CA68Bu;


            value ^=
                value >>
                16;


            return
                (
                    value &
                    0x00FFFFFFu
                )
                /
                16777216.0;
        }
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


    // =========================================================
    // TEXT SAMPLE
    // =========================================================

    private readonly record struct TextParticleSample(
        bool IsText,
        Vector2 LocalPoint,
        bool IsEdge);
}

