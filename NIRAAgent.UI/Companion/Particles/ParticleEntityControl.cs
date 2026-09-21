/*
 * filename: ParticleEntityControl.cs
 */

using System.Numerics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

using NIRAAgent.Embodiment;

namespace NIRAAgent.UI.Companion.Particles;

public sealed class ParticleEntityControl
    : FrameworkElement
{
    // =========================================================
    // PERFORMANCE / DENSITY
    // =========================================================

    private const int ParticleCount =
        1200;

    private const double TargetFrameSeconds =
        1.0 /
        60.0;

    private static readonly ParticleDensityProfile Density =
        ParticleDensityProfile.Balanced;


    // =========================================================
    // VISUAL
    // =========================================================

    private readonly DrawingVisual _visual =
        new();


    // =========================================================
    // PARTICLE SYSTEM
    //
    // We keep NIRA's existing form registry + morph engine. This is
    // important: future shapes remain providers, not renderer hacks.
    // =========================================================

    private readonly ParticleFormRegistry _forms =
        new();

    private readonly ParticleMorphEngine _engine;

    private readonly ParticleRenderState[] _renderBuffer =
        new ParticleRenderState[
            ParticleCount];


    // =========================================================
    // ANIMATION
    // =========================================================

    private bool _rendering;

    private double _lastTime;


    // =========================================================
    // INTENT
    // =========================================================

    private NIRAVisualIntent _intent =
        NIRAVisualIntent.RestingOrb;


    // =========================================================
    // COLORS
    // =========================================================

    private static readonly Color ElectricCyan =
        Color.FromRgb(
            120,
            219,
            255);

    private static readonly Color ElectricBlue =
        Color.FromRgb(
            77,
            143,
            255);

    private static readonly Color ElectricViolet =
        Color.FromRgb(
            178,
            110,
            255);

    private static readonly Color HotWhite =
        Color.FromRgb(
            232,
            244,
            255);


    // =========================================================
    // THREE-SIZE MATERIAL RAMPS
    //
    // Tiny = background/fill brightness.
    // Normal = structural support.
    // Large = noticeable style-defining points.
    //
    // No BlurEffect is used. The glow comes from low-alpha radial
    // material and compact local halos only.
    // =========================================================

    private static readonly Brush[] CyanTinyBrushes =
        CreateBrushRamp(
            ElectricCyan,
            new byte[] { 22, 60, 128, 190 });

    private static readonly Brush[] CyanNormalBrushes =
        CreateBrushRamp(
            ElectricCyan,
            new byte[] { 46, 116, 206, 255 });

    private static readonly Brush[] CyanLargeBrushes =
        CreateBrushRamp(
            ElectricCyan,
            new byte[] { 64, 150, 236, 255 });

    private static readonly Brush[] BlueTinyBrushes =
        CreateBrushRamp(
            ElectricBlue,
            new byte[] { 20, 56, 122, 186 });

    private static readonly Brush[] BlueNormalBrushes =
        CreateBrushRamp(
            ElectricBlue,
            new byte[] { 44, 110, 198, 255 });

    private static readonly Brush[] BlueLargeBrushes =
        CreateBrushRamp(
            ElectricBlue,
            new byte[] { 62, 144, 230, 255 });

    private static readonly Brush[] VioletTinyBrushes =
        CreateBrushRamp(
            ElectricViolet,
            new byte[] { 20, 54, 116, 180 });

    private static readonly Brush[] VioletNormalBrushes =
        CreateBrushRamp(
            ElectricViolet,
            new byte[] { 42, 106, 190, 252 });

    private static readonly Brush[] VioletLargeBrushes =
        CreateBrushRamp(
            ElectricViolet,
            new byte[] { 58, 138, 224, 255 });

    private static readonly Brush[] WhiteTinyBrushes =
        CreateBrushRamp(
            HotWhite,
            new byte[] { 18, 48, 104, 168 });

    private static readonly Brush[] WhiteNormalBrushes =
        CreateBrushRamp(
            HotWhite,
            new byte[] { 38, 96, 180, 244 });

    private static readonly Brush[] WhiteLargeBrushes =
        CreateBrushRamp(
            HotWhite,
            new byte[] { 54, 126, 214, 255 });


    // =========================================================
    // BACKGROUND AURA
    // =========================================================

    private static readonly Brush CyanAuraBrush =
        CreateAuraBrush(
            ElectricCyan);

    private static readonly Brush VioletAuraBrush =
        CreateAuraBrush(
            ElectricViolet);


    // =========================================================
    // VISUAL TREE
    // =========================================================

    protected override int VisualChildrenCount =>
        1;

    protected override Visual GetVisualChild(
        int index)
    {
        if (index != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index));
        }

        return _visual;
    }


    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public ParticleEntityControl()
    {
        AddVisualChild(
            _visual);

        IsHitTestVisible =
            true;

        _engine =
            new ParticleMorphEngine(
                ParticleCount,
                _forms);

        _engine.SetIntent(
            _intent);

        Loaded +=
            OnLoaded;

        Unloaded +=
            OnUnloaded;
    }


    // =========================================================
    // INTENT
    // =========================================================

    public void SetIntent(
        NIRAVisualIntent intent)
    {
        ArgumentNullException.ThrowIfNull(
            intent);

        _intent =
            intent.Normalize();

        _engine.SetIntent(
            _intent);
    }


    // =========================================================
    // STARTUP MATERIALIZATION
    // =========================================================

    public void PrepareStartupEntrance()
    {
        _engine.PrepareStartupEntrance();
    }

    public void StartStartupEntrance()
    {
        _engine.StartStartupEntrance();
    }


    // =========================================================
    // FORM REGISTRATION
    // =========================================================

    public void RegisterFormProvider(
        IParticleFormProvider provider)
    {
        _forms.Register(
            provider);

        _engine.SetIntent(
            _intent);
    }


    // =========================================================
    // HIT TEST
    // =========================================================

    protected override HitTestResult? HitTestCore(
        PointHitTestParameters hitTestParameters)
    {
        double centerX =
            ActualWidth *
            0.5;

        double centerY =
            ActualHeight *
            0.5;

        double dx =
            hitTestParameters.HitPoint.X -
            centerX;

        double dy =
            hitTestParameters.HitPoint.Y -
            centerY;

        double radius =
            Math.Min(
                ActualWidth,
                ActualHeight) *
            0.38;

        if (
            dx * dx +
            dy * dy >
            radius * radius)
        {
            return null;
        }

        return new PointHitTestResult(
            this,
            hitTestParameters.HitPoint);
    }


    // =========================================================
    // LIFECYCLE
    // =========================================================

    private void OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        if (_rendering)
        {
            return;
        }

        _rendering =
            true;

        _lastTime =
            GetSeconds();

        CompositionTarget.Rendering +=
            OnRendering;

        RenderParticles();
    }

    private void OnUnloaded(
        object sender,
        RoutedEventArgs e)
    {
        if (!_rendering)
        {
            return;
        }

        _rendering =
            false;

        CompositionTarget.Rendering -=
            OnRendering;
    }


    // =========================================================
    // FRAME
    // =========================================================

    private void OnRendering(
        object? sender,
        EventArgs e)
    {
        double now =
            GetSeconds();

        double elapsed =
            now -
            _lastTime;

        if (
            elapsed <
            TargetFrameSeconds *
            0.80)
        {
            return;
        }

        _lastTime =
            now;

        elapsed =
            Math.Clamp(
                elapsed,
                0.001,
                0.05);

        _engine.Update(
            elapsed);

        RenderParticles();
    }


    // =========================================================
    // RENDER
    // =========================================================

    private void RenderParticles()
    {
        if (
            ActualWidth <= 0 ||
            ActualHeight <= 0)
        {
            return;
        }

        for (
            int index = 0;
            index < _engine.Count;
            index++)
        {
            _renderBuffer[index] =
                _engine.Get(
                    index);
        }

        Array.Sort(
            _renderBuffer,
            CompareDepth);

        using DrawingContext dc =
            _visual.RenderOpen();

        double centerX =
            ActualWidth *
            0.5;

        double centerY =
            ActualHeight *
            0.5;

        double baseRadius =
            Math.Min(
                ActualWidth,
                ActualHeight) *
            0.40 *
            _intent.Scale;

        DrawAmbientAura(
            dc,
            centerX,
            centerY,
            baseRadius);

        for (
            int index = 0;
            index < _renderBuffer.Length;
            index++)
        {
            DrawParticle(
                dc,
                _renderBuffer[index],
                centerX,
                centerY,
                baseRadius);
        }
    }


    // =========================================================
    // SUBTLE REAR GLOW
    //
    // This replaces the bright central wash from the prototype.
    // The particles remain the body; the aura only supports them.
    // =========================================================

    private void DrawAmbientAura(
        DrawingContext dc,
        double centerX,
        double centerY,
        double baseRadius)
    {
        double presence =
            Math.Clamp(
                _intent.Presence,
                0.0,
                1.0);

        double energy =
            Math.Clamp(
                _intent.Energy,
                0.0,
                1.0);

        double pulse =
            Math.Clamp(
                _intent.Pulse,
                0.0,
                1.0);

        double now =
            GetSeconds();

        double breathe =
            1.0 +
            Math.Sin(
                now *
                (0.34 +
                 pulse *
                 0.46)) *
            (0.012 +
             pulse *
             0.010);

        double opacity =
            0.12 +
            presence *
            0.13 +
            energy *
            0.06;

        double offsetX =
            Math.Sin(
                now *
                0.13) *
            baseRadius *
            0.025;

        double offsetY =
            Math.Cos(
                now *
                0.11) *
            baseRadius *
            0.018;

        dc.PushOpacity(
            opacity);

        dc.DrawEllipse(
            CyanAuraBrush,
            null,
            new Point(
                centerX +
                offsetX,
                centerY +
                offsetY),
            baseRadius *
                1.08 *
                breathe,
            baseRadius *
                1.08 *
                breathe);

        dc.Pop();

        dc.PushOpacity(
            opacity *
            0.52);

        dc.DrawEllipse(
            VioletAuraBrush,
            null,
            new Point(
                centerX -
                offsetX *
                0.7,
                centerY -
                offsetY *
                0.7),
            baseRadius *
                0.90 *
                breathe,
            baseRadius *
                0.90 *
                breathe);

        dc.Pop();
    }


    // =========================================================
    // DEPTH COMPARISON
    // =========================================================

    private static int CompareDepth(
        ParticleRenderState left,
        ParticleRenderState right)
    {
        return left.Position.Z.CompareTo(
            right.Position.Z);
    }


    // =========================================================
    // DRAW PARTICLE
    // =========================================================

    private static void DrawParticle(
        DrawingContext dc,
        ParticleRenderState particle,
        double centerX,
        double centerY,
        double baseRadius)
    {
        Vector3 position =
            particle.Position;

        ParticleSizeBand band =
            Density.ResolveBand(
                particle.ColorSeed);

        double normalizedDepth =
            Math.Clamp(
                (position.Z +
                 1.0) *
                0.5,
                0.0,
                1.0);

        double perspective =
            0.86 +
            normalizedDepth *
            0.22;

        // Tiny particles sit a little wider and act as the atmospheric
        // fill. Large particles stay slightly tighter so their motion
        // can define the body/style later.
        double spread =
            band switch
            {
                ParticleSizeBand.Tiny =>
                    1.045,

                ParticleSizeBand.Large =>
                    0.975,

                _ =>
                    1.0
            };

        double screenX =
            centerX +
            position.X *
            baseRadius *
            perspective *
            spread;

        double screenY =
            centerY +
            position.Y *
            baseRadius *
            perspective *
            spread;

        double bandSize =
            band switch
            {
                ParticleSizeBand.Tiny =>
                    0.58,

                ParticleSizeBand.Normal =>
                    1.00,

                _ =>
                    1.43
            };

        // Keep the difference obvious enough to read but not cartoonish.
        double roleSize =
            particle.Role switch
            {
                ParticleRole.Core =>
                    0.84,

                ParticleRole.Accent =>
                    1.05,

                _ =>
                    1.0
            };

        const double globalParticleSizeScale =
            1.95;

        double calculatedSize =
            particle.Size *
            bandSize *
            roleSize *
            globalParticleSizeScale *
            (0.60 +
             normalizedDepth *
             0.40);

        // Keep every size class readable in the real main UI.
        // These are ellipse radii in WPF device-independent pixels.
        double minimumReadableSize =
            band switch
            {
                ParticleSizeBand.Tiny =>
                    0.78,

                ParticleSizeBand.Normal =>
                    1.30,

                _ =>
                    1.95
            };

        double size =
            Math.Max(
                calculatedSize,
                minimumReadableSize);

        // Reduce central/core brightness. The old bright middle should no
        // longer overpower the actual particle structure.
        double roleBrightness =
            particle.Role switch
            {
                ParticleRole.Core =>
                    0.78,

                ParticleRole.Halo =>
                    0.84,

                ParticleRole.Accent =>
                    1.10,

                _ =>
                    1.04
            };

        double bandBrightness =
            band switch
            {
                ParticleSizeBand.Tiny =>
                    0.88,

                ParticleSizeBand.Large =>
                    1.12,

                _ =>
                    1.04
            };

        double rawIntensity =
            particle.Brightness *
            roleBrightness *
            bandBrightness *
            (0.78 +
             normalizedDepth *
             0.48);

        // The live main-UI body must never collapse into near-invisibility.
        // Tiny particles stay subdued, while normal and large particles
        // retain a clear readable floor for the NIRA silhouette.
        double visibilityFloor =
            band switch
            {
                ParticleSizeBand.Tiny =>
                    0.30,

                ParticleSizeBand.Normal =>
                    0.46,

                _ =>
                    0.58
            };

        int brightness =
            ResolveBrightnessLevel(
                Math.Max(
                    rawIntensity,
                    visibilityFloor));

        Brush[] ramp =
            ResolveBrushRamp(
                particle.Palette,
                particle.ColorSeed,
                band);

        // Tiny points intentionally have no per-particle halo. Their job is
        // to build the field, not to become individual glowing blobs.
        double glowScale =
            band switch
            {
                ParticleSizeBand.Tiny =>
                    0.0,

                ParticleSizeBand.Large when particle.Role == ParticleRole.Accent =>
                    1.86,

                ParticleSizeBand.Large when brightness >= 2 =>
                    1.62,

                ParticleSizeBand.Normal when particle.Role == ParticleRole.Accent =>
                    1.58,

                ParticleSizeBand.Normal when brightness >= 2 =>
                    1.34,

                _ =>
                    0.0
            };

        if (glowScale > 0.0)
        {
            dc.DrawEllipse(
                ramp[1],
                null,
                new Point(
                    screenX,
                    screenY),
                size *
                    glowScale,
                size *
                    glowScale);
        }

        dc.DrawEllipse(
            ramp[brightness],
            null,
            new Point(
                screenX,
                screenY),
            size,
            size);
    }


    // =========================================================
    // BRIGHTNESS
    // =========================================================

    private static int ResolveBrightnessLevel(
        double intensity)
    {
        if (intensity < 0.24)
        {
            return 0;
        }

        if (intensity < 0.46)
        {
            return 1;
        }

        if (intensity < 0.72)
        {
            return 2;
        }

        return 3;
    }


    // =========================================================
    // PALETTE + SIZE BAND
    // =========================================================

    private static Brush[] ResolveBrushRamp(
        ParticlePalette palette,
        int seed,
        ParticleSizeBand band)
    {
        int variation =
            (int)(
                (uint)seed %
                100U);

        ParticlePalette resolved =
            palette switch
            {
                ParticlePalette.White =>
                    ParticlePalette.White,

                ParticlePalette.Violet =>
                    variation < 88
                        ? ParticlePalette.Violet
                        : ParticlePalette.Blue,

                ParticlePalette.Blue =>
                    variation < 88
                        ? ParticlePalette.Blue
                        : ParticlePalette.Cyan,

                _ =>
                    variation < 92
                        ? ParticlePalette.Cyan
                        : ParticlePalette.Blue
            };

        return (
            resolved,
            band) switch
            {
                (ParticlePalette.White, ParticleSizeBand.Tiny) =>
                    WhiteTinyBrushes,

                (ParticlePalette.White, ParticleSizeBand.Normal) =>
                    WhiteNormalBrushes,

                (ParticlePalette.White, ParticleSizeBand.Large) =>
                    WhiteLargeBrushes,

                (ParticlePalette.Violet, ParticleSizeBand.Tiny) =>
                    VioletTinyBrushes,

                (ParticlePalette.Violet, ParticleSizeBand.Normal) =>
                    VioletNormalBrushes,

                (ParticlePalette.Violet, ParticleSizeBand.Large) =>
                    VioletLargeBrushes,

                (ParticlePalette.Blue, ParticleSizeBand.Tiny) =>
                    BlueTinyBrushes,

                (ParticlePalette.Blue, ParticleSizeBand.Normal) =>
                    BlueNormalBrushes,

                (ParticlePalette.Blue, ParticleSizeBand.Large) =>
                    BlueLargeBrushes,

                (ParticlePalette.Cyan, ParticleSizeBand.Tiny) =>
                    CyanTinyBrushes,

                (ParticlePalette.Cyan, ParticleSizeBand.Normal) =>
                    CyanNormalBrushes,

                _ =>
                    CyanLargeBrushes
            };
    }


    // =========================================================
    // BRUSH HELPERS
    // =========================================================

    private static Brush[] CreateBrushRamp(
        Color color,
        byte[] alpha)
    {
        Brush[] result =
            new Brush[
                alpha.Length];

        for (
            int index = 0;
            index < alpha.Length;
            index++)
        {
            SolidColorBrush brush =
                new(
                    Color.FromArgb(
                        alpha[index],
                        color.R,
                        color.G,
                        color.B));

            brush.Freeze();

            result[index] =
                brush;
        }

        return result;
    }

    private static Brush CreateAuraBrush(
        Color color)
    {
        RadialGradientBrush brush =
            new()
            {
                MappingMode =
                    BrushMappingMode.RelativeToBoundingBox,

                Center =
                    new Point(
                        0.5,
                        0.5),

                GradientOrigin =
                    new Point(
                        0.48,
                        0.46),

                RadiusX =
                    0.5,

                RadiusY =
                    0.5
            };

        brush.GradientStops.Add(
            new GradientStop(
                Color.FromArgb(
                    84,
                    color.R,
                    color.G,
                    color.B),
                0.0));

        brush.GradientStops.Add(
            new GradientStop(
                Color.FromArgb(
                    24,
                    color.R,
                    color.G,
                    color.B),
                0.52));

        brush.GradientStops.Add(
            new GradientStop(
                Color.FromArgb(
                    0,
                    color.R,
                    color.G,
                    color.B),
                1.0));

        brush.Freeze();

        return brush;
    }


    // =========================================================
    // TIME
    // =========================================================

    private static double GetSeconds()
    {
        return
            Environment.TickCount64 /
            1000.0;
    }
}

