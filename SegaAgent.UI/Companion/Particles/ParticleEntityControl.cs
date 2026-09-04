/*
 * filename: ParticleEntityControl.cs
 */

using System.Numerics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

using SegaAgent.Embodiment;

namespace SegaAgent.UI.Companion.Particles;

public sealed class ParticleEntityControl
    : FrameworkElement
{
    // =========================================================
    // PERFORMANCE
    // =========================================================

    private const int ParticleCount =
        1200;


    private const double TargetFrameSeconds =
        1.0 /
        30.0;


    // =========================================================
    // VISUAL
    // =========================================================

    private readonly DrawingVisual
        _visual =
            new();


    // =========================================================
    // PARTICLE SYSTEM
    // =========================================================

    private readonly ParticleFormRegistry
        _forms =
            new();


    private readonly ParticleMorphEngine
        _engine;


    // =========================================================
    // PREALLOCATED RENDER BUFFER
    //
    // Avoid allocating a new particle array every frame.
    // =========================================================

    private readonly ParticleRenderState[]
        _renderBuffer =
            new ParticleRenderState[
                ParticleCount];


    // =========================================================
    // ANIMATION
    // =========================================================

    private bool
        _rendering;


    private double
        _lastTime;


    // =========================================================
    // INTENT
    // =========================================================

    private SegaVisualIntent
        _intent =
            SegaVisualIntent.RestingOrb;


    // =========================================================
    // COLORS
    // =========================================================

    private static readonly Color ElectricCyan =
        Color.FromRgb(
            78,
            236,
            255);


    private static readonly Color ElectricBlue =
        Color.FromRgb(
            72,
            132,
            255);


    private static readonly Color ElectricViolet =
        Color.FromRgb(
            160,
            94,
            255);


    private static readonly Color HotWhite =
        Color.FromRgb(
            226,
            252,
            255);


    // =========================================================
    // BRUSHES
    // =========================================================

    private static readonly Brush[] CyanBrushes =
        CreateBrushRamp(
            ElectricCyan);


    private static readonly Brush[] BlueBrushes =
        CreateBrushRamp(
            ElectricBlue);


    private static readonly Brush[] VioletBrushes =
        CreateBrushRamp(
            ElectricViolet);


    private static readonly Brush[] WhiteBrushes =
        CreateBrushRamp(
            HotWhite);


    // =========================================================
    // VISUAL TREE
    // =========================================================

    protected override int VisualChildrenCount =>
        1;


    protected override Visual GetVisualChild(
        int index)
    {
        if (index !=
            0)
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
        SegaVisualIntent intent)
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

    protected override HitTestResult?
        HitTestCore(
            PointHitTestParameters
                hitTestParameters)
    {
        double centerX =
            ActualWidth *
            0.5;


        double centerY =
            ActualHeight *
            0.5;


        double dx =
            hitTestParameters
                .HitPoint
                .X
            -
            centerX;


        double dy =
            hitTestParameters
                .HitPoint
                .Y
            -
            centerY;


        double radius =
            Math.Min(
                ActualWidth,
                ActualHeight)
            *
            0.38;


        double distanceSquared =
            dx *
            dx
            +
            dy *
            dy;


        if (distanceSquared >
            radius *
            radius)
        {
            return null;
        }


        return new PointHitTestResult(
            this,
            hitTestParameters.HitPoint);
    }


    // =========================================================
    // LOADED
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


    // =========================================================
    // UNLOADED
    // =========================================================

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


        if (elapsed <
            TargetFrameSeconds)
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
            ActualWidth <=
                0
            ||
            ActualHeight <=
                0)
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


        // =====================================================
        // REAL 2.5D DEPTH ORDER
        //
        // Negative Z renders first.
        // Positive/front Z renders last.
        // =====================================================

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
                ActualHeight)
            *
            0.36
            *
            _intent.Scale;


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
    // DEPTH COMPARISON
    // =========================================================

    private static int CompareDepth(
        ParticleRenderState left,
        ParticleRenderState right)
    {
        return left
            .Position
            .Z
            .CompareTo(
                right
                    .Position
                    .Z);
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


        double normalizedDepth =
            Math.Clamp(
                (
                    position.Z +
                    1.0
                )
                *
                0.5,
                0.0,
                1.0);


        double perspective =
            0.86 +
            normalizedDepth *
            0.22;


        double screenX =
            centerX +
            position.X *
            baseRadius *
            perspective;


        double screenY =
            centerY +
            position.Y *
            baseRadius *
            perspective;


        double size =
            particle.Size
            *
            (
                0.58 +
                normalizedDepth *
                0.42
            );


        int brightness =
            ResolveBrightnessLevel(
                particle.Brightness *
                (
                    0.70 +
                    normalizedDepth *
                    0.46
                ));


        Brush[] ramp =
            ResolveBrushRamp(
                particle.Palette,
                particle.ColorSeed);


        // =====================================================
        // GLOW
        //
        // Accent particles are allowed a clear glow.
        //
        // Normal particles remain crisp points.
        // =====================================================

        if (
            particle.Role ==
                ParticleRole.Accent
            ||
            (
                particle.Role ==
                    ParticleRole.Core
                &&
                brightness >=
                    3
            ))
        {
            dc.DrawEllipse(
                ramp[0],
                null,
                new Point(
                    screenX,
                    screenY),
                size *
                    2.45,
                size *
                    2.45);
        }


        // =====================================================
        // POINT
        // =====================================================

        dc.DrawEllipse(
            ramp[
                brightness],
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
        if (intensity <
            0.42)
        {
            return 0;
        }


        if (intensity <
            0.68)
        {
            return 1;
        }


        if (intensity <
            0.92)
        {
            return 2;
        }


        return 3;
    }


    // =========================================================
    // PALETTE
    // =========================================================

    private static Brush[] ResolveBrushRamp(
        ParticlePalette palette,
        int seed)
    {
        int variation =
            Math.Abs(
                seed)
            %
            100;


        return palette switch
        {
            ParticlePalette.White =>
                WhiteBrushes,


            ParticlePalette.Violet =>
                variation <
                    88
                    ? VioletBrushes
                    : BlueBrushes,


            ParticlePalette.Blue =>
                variation <
                    88
                    ? BlueBrushes
                    : CyanBrushes,


            _ =>
                variation <
                    92
                    ? CyanBrushes
                    : BlueBrushes
        };
    }


    // =========================================================
    // BRUSH RAMP
    // =========================================================

    private static Brush[] CreateBrushRamp(
        Color color)
    {
        byte[] alpha =
        {
            12,
            38,
            105,
            220
        };


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
