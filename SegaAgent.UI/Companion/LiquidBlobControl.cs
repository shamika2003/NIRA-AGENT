using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace SegaAgent.UI.Companion;

/// <summary>
/// SEGAAI futuristic liquid-energy companion.
///
/// Design:
/// - Completely transparent center.
/// - Small organic circular energy membrane.
/// - Thick multi-layer neon perimeter.
/// - Continuous aura rings are born from the perimeter.
/// - Aura rings expand outward and fade.
/// - Cyan / electric blue / violet energy palette.
/// - State changes control energy behavior.
/// </summary>
public sealed class LiquidBlobControl : FrameworkElement
{
    // =========================================================
    // VISUAL
    // =========================================================

    private readonly DrawingVisual _visual = new();

    // =========================================================
    // ANIMATION
    // =========================================================

    private double _time;

    private bool _isHovered;

    private bool _isRendering;

    private double _lastFrameTime;

    // =========================================================
    // STATE
    // =========================================================

    private CompanionState _state =
        CompanionState.Idle;

    // =========================================================
    // GEOMETRY
    // =========================================================

    // Previous size was ~58.
    // This is approximately 75% of that size.
    private const double BaseRadius = 44.0;

    private const int PointCount = 96;

    // =========================================================
    // AURA
    // =========================================================

    private readonly List<AuraWave> _waves = new();

    private double _waveSpawnAccumulator;

    private const int MaximumAuraWaves = 28;

    // =========================================================
    // COLORS
    // =========================================================

    private static readonly Color ElectricCyan =
        Color.FromRgb(
            75,
            235,
            255);

    private static readonly Color NeonBlue =
        Color.FromRgb(
            65,
            125,
            255);

    private static readonly Color ElectricViolet =
        Color.FromRgb(
            155,
            90,
            255);

    private static readonly Color HotWhite =
        Color.FromRgb(
            220,
            250,
            255);

    // =========================================================
    // RANDOM
    // =========================================================

    private static readonly Random Random =
        new();

    // =========================================================
    // EVENTS
    // =========================================================

    public event EventHandler? BlobHovered;

    public event EventHandler? BlobLeft;

    // =========================================================
    // STATE PROPERTY
    // =========================================================

    public CompanionState State
    {
        get => _state;

        set
        {
            if (_state == value)
                return;

            _state = value;

            InvalidateVisual();
        }
    }

    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public LiquidBlobControl()
    {
        AddVisualChild(_visual);

        IsHitTestVisible = true;

        MouseEnter += OnMouseEnter;
        MouseLeave += OnMouseLeave;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

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
    // LOADED
    // =========================================================

    private void OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        if (_isRendering)
            return;

        _isRendering = true;

        _lastFrameTime =
            GetCurrentSeconds();

        CompositionTarget.Rendering +=
            OnRendering;

        RenderBlob();
    }

    // =========================================================
    // UNLOADED
    // =========================================================

    private void OnUnloaded(
        object sender,
        RoutedEventArgs e)
    {
        if (!_isRendering)
            return;

        _isRendering = false;

        CompositionTarget.Rendering -=
            OnRendering;
    }

    // =========================================================
    // HIT TEST
    // =========================================================

    protected override HitTestResult HitTestCore(
        PointHitTestParameters hitTestParameters)
    {
        return new PointHitTestResult(
            this,
            hitTestParameters.HitPoint);
    }

    // =========================================================
    // MOUSE ENTER
    // =========================================================

    private void OnMouseEnter(
        object sender,
        MouseEventArgs e)
    {
        if (_isHovered)
            return;

        _isHovered = true;

        BlobHovered?.Invoke(
            this,
            EventArgs.Empty);

        InvalidateVisual();
    }

    // =========================================================
    // MOUSE LEAVE
    // =========================================================

    private void OnMouseLeave(
        object sender,
        MouseEventArgs e)
    {
        if (!_isHovered)
            return;

        _isHovered = false;

        BlobLeft?.Invoke(
            this,
            EventArgs.Empty);

        InvalidateVisual();
    }

    // =========================================================
    // ANIMATION
    // =========================================================

    private void OnRendering(
        object? sender,
        EventArgs e)
    {
        double now =
            GetCurrentSeconds();

        double delta =
            now - _lastFrameTime;

        _lastFrameTime = now;

        /*
         * Protect against huge jumps when
         * the application is paused/minimized.
         */

        delta =
            Math.Clamp(
                delta,
                0.001,
                0.05);

        _time += delta;

        UpdateAura(delta);

        RenderBlob();
    }

    // =========================================================
    // CURRENT TIME
    // =========================================================

    private static double GetCurrentSeconds()
    {
        return System.Environment.TickCount64 / 1000.0;
    }

    // =========================================================
    // AURA UPDATE
    // =========================================================

    private void UpdateAura(
        double delta)
    {
        AuraSettings settings =
            GetAuraSettings();

        // -----------------------------------------------------
        // SPAWN
        // -----------------------------------------------------

        _waveSpawnAccumulator +=
            delta *
            settings.SpawnRate;

        while (_waveSpawnAccumulator >= 1.0)
        {
            _waveSpawnAccumulator -= 1.0;

            SpawnAuraWave();
        }

        // -----------------------------------------------------
        // UPDATE WAVES
        // -----------------------------------------------------

        for (int i = _waves.Count - 1;
             i >= 0;
             i--)
        {
            AuraWave wave =
                _waves[i];

            wave.Progress +=
                delta /
                wave.Lifetime;

            if (wave.Progress >= 1.0)
            {
                _waves.RemoveAt(i);
            }
        }

        // -----------------------------------------------------
        // SAFETY
        // -----------------------------------------------------

        if (_waves.Count > MaximumAuraWaves)
        {
            _waves.RemoveRange(
                0,
                _waves.Count -
                MaximumAuraWaves);
        }
    }

    // =========================================================
    // SPAWN AURA WAVE
    // =========================================================

    private void SpawnAuraWave()
    {
        AuraSettings settings =
            GetAuraSettings();

        AuraWave wave =
            new()
            {
                Progress = 0.0,

                Lifetime =
                    settings.Lifetime *
                    RandomRange(
                        0.88,
                        1.12),

                Strength =
                    settings.Strength *
                    RandomRange(
                        0.72,
                        1.18),

                RadiusOffset =
                    RandomRange(
                        -1.0,
                        1.0),

                OrganicOffset =
                    RandomRange(
                        0.0,
                        Math.PI * 2.0),

                ColorIndex =
                    Random.Next(0, 3),

                Rotation =
                    RandomRange(
                        -1.0,
                        1.0)
            };

        _waves.Add(wave);
    }

    // =========================================================
    // RANDOM
    // =========================================================

    private static double RandomRange(
        double minimum,
        double maximum)
    {
        return minimum +
               Random.NextDouble() *
               (maximum - minimum);
    }

    // =========================================================
    // RENDER
    // =========================================================

    private void RenderBlob()
    {
        if (ActualWidth <= 0 ||
            ActualHeight <= 0)
        {
            return;
        }

        using DrawingContext dc =
            _visual.RenderOpen();

        double centerX =
            ActualWidth * 0.5;

        double centerY =
            ActualHeight * 0.5;

        AuraSettings settings =
            GetAuraSettings();

        // =====================================================
        // ORGANIC PERIMETER
        // =====================================================

        Point[] borderPoints =
            CreateOrganicCircle(
                centerX,
                centerY,
                BaseRadius);

        // =====================================================
        // AURA
        // =====================================================

        DrawAura(
            dc,
            centerX,
            centerY,
            settings);

        // =====================================================
        // MAIN ENERGY BORDER
        // =====================================================

        DrawNeonBorder(
            dc,
            borderPoints,
            settings);

        // =====================================================
        // MICRO ENERGY PARTICLES
        // =====================================================

        DrawEnergyParticles(
            dc,
            centerX,
            centerY,
            settings);
    }

    // =========================================================
    // ORGANIC CIRCLE
    // =========================================================

    private Point[] CreateOrganicCircle(
        double centerX,
        double centerY,
        double radius)
    {
        Point[] points =
            new Point[PointCount];

        AuraSettings settings =
            GetAuraSettings();

        double breathing =
            Math.Sin(
                _time *
                settings.BorderPulseSpeed)
            *
            settings.BorderPulseAmount;

        if (_isHovered)
        {
            breathing += 1.5;
        }

        double finalRadius =
            radius +
            breathing;

        for (int i = 0;
             i < PointCount;
             i++)
        {
            double angle =
                Math.PI * 2.0 *
                i /
                PointCount;

            // -------------------------------------------------
            // Very subtle organic movement.
            //
            // The goal is:
            //
            //     organic circle
            //
            // NOT:
            //
            //     liquid blob
            // -------------------------------------------------

            double wave1 =
                Math.Sin(
                    angle * 3.0 +
                    _time * 0.72)
                * 1.25;

            double wave2 =
                Math.Sin(
                    angle * 5.0 -
                    _time * 0.46)
                * 0.70;

            double wave3 =
                Math.Sin(
                    angle * 7.0 +
                    _time * 0.30)
                * 0.35;

            double organic =
                wave1 +
                wave2 +
                wave3;

            double r =
                finalRadius +
                organic;

            points[i] =
                new Point(
                    centerX +
                    Math.Cos(angle) * r,

                    centerY +
                    Math.Sin(angle) * r);
        }

        return points;
    }

    // =========================================================
    // AURA
    // =========================================================

    private void DrawAura(
        DrawingContext dc,
        double centerX,
        double centerY,
        AuraSettings settings)
    {
        foreach (AuraWave wave in _waves)
        {
            double progress =
                Math.Clamp(
                    wave.Progress,
                    0.0,
                    1.0);

            // -------------------------------------------------
            // EXPANSION
            // -------------------------------------------------

            double expansion =
                EaseOutCubic(progress);

            double radius =
                BaseRadius +
                wave.RadiusOffset +
                expansion *
                settings.AuraDistance;

            // -------------------------------------------------
            // ORGANIC RING
            // -------------------------------------------------

            Point[] ring =
                CreateAuraRing(
                    centerX,
                    centerY,
                    radius,
                    wave);

            StreamGeometry geometry =
                CreateClosedCurve(ring);

            // -------------------------------------------------
            // FADE
            // -------------------------------------------------

            double fade =
                1.0 -
                SmoothStep(
                    0.05,
                    1.0,
                    progress);

            /*
             * Small birth fade.
             *
             * The ring starts at the perimeter
             * instead of suddenly appearing outside.
             */

            double birth =
                SmoothStep(
                    0.0,
                    0.10,
                    progress);

            double alpha =
                fade *
                birth *
                wave.Strength;

            if (alpha <= 0.01)
                continue;

            // -------------------------------------------------
            // COLOR
            // -------------------------------------------------

            Color color =
                GetAuraColor(
                    wave.ColorIndex,
                    settings);

            // -------------------------------------------------
            // LARGE DIFFUSE GLOW
            // -------------------------------------------------

            DrawCurve(
                dc,
                geometry,
                WithAlpha(
                    color,
                    alpha * 0.16),
                7.0);

            // -------------------------------------------------
            // MEDIUM GLOW
            // -------------------------------------------------

            DrawCurve(
                dc,
                geometry,
                WithAlpha(
                    color,
                    alpha * 0.30),
                3.6);

            // -------------------------------------------------
            // SHARP ENERGY EDGE
            // -------------------------------------------------

            DrawCurve(
                dc,
                geometry,
                WithAlpha(
                    color,
                    alpha * 0.72),
                1.15);
        }
    }

    // =========================================================
    // AURA RING
    // =========================================================

    private Point[] CreateAuraRing(
        double centerX,
        double centerY,
        double radius,
        AuraWave wave)
    {
        Point[] points =
            new Point[PointCount];

        for (int i = 0;
             i < PointCount;
             i++)
        {
            double angle =
                Math.PI * 2.0 *
                i /
                PointCount;

            // -------------------------------------------------
            // Large organic movement
            // -------------------------------------------------

            double organic1 =
                Math.Sin(
                    angle * 3.0 +
                    _time * 0.42 +
                    wave.OrganicOffset)
                * 1.45;

            double organic2 =
                Math.Sin(
                    angle * 5.0 -
                    _time * 0.27 +
                    wave.OrganicOffset * 1.6)
                * 0.80;

            double organic3 =
                Math.Sin(
                    angle * 8.0 +
                    _time * 0.19 +
                    wave.OrganicOffset * 0.5)
                * 0.35;

            double organic =
                organic1 +
                organic2 +
                organic3;

            double r =
                radius +
                organic;

            points[i] =
                new Point(
                    centerX +
                    Math.Cos(angle) * r,

                    centerY +
                    Math.Sin(angle) * r);
        }

        return points;
    }

    // =========================================================
    // NEON BORDER
    // =========================================================

    private void DrawNeonBorder(
        DrawingContext dc,
        Point[] points,
        AuraSettings settings)
    {
        StreamGeometry geometry =
            CreateClosedCurve(points);

        double intensity =
            settings.BorderIntensity;

        // =====================================================
        // HUGE OUTER AURA
        // =====================================================

        DrawCurve(
            dc,
            geometry,
            WithAlpha(
                ElectricCyan,
                0.12 * intensity),
            8.0);

        // =====================================================
        // LARGE CYAN GLOW
        // =====================================================

        DrawCurve(
            dc,
            geometry,
            WithAlpha(
                ElectricCyan,
                0.22 * intensity),
            4.5);

        // =====================================================
        // BLUE / VIOLET SECONDARY GLOW
        // =====================================================

        DrawCurve(
            dc,
            geometry,
            WithAlpha(
                ElectricViolet,
                0.16 * intensity),
            2.5);

        // =====================================================
        // STRONG CYAN GLOW
        // =====================================================

        DrawCurve(
            dc,
            geometry,
            WithAlpha(
                ElectricCyan,
                0.48 * intensity),
            1.8);

        // =====================================================
        // ELECTRIC BLUE CORE
        // =====================================================

        DrawCurve(
            dc,
            geometry,
            WithAlpha(
                NeonBlue,
                0.90 * intensity),
            0.85);

        // =====================================================
        // HOT WHITE-CYAN EDGE
        // =====================================================

        DrawCurve(
            dc,
            geometry,
            WithAlpha(
                HotWhite,
                0.92 * intensity),
            0.50);

        // =====================================================
        // HOVER ENERGY
        // =====================================================

        if (_isHovered)
        {
            DrawCurve(
                dc,
                geometry,
                WithAlpha(
                    ElectricViolet,
                    0.80),
                1.4);
        }
    }

    // =========================================================
    // ENERGY PARTICLES
    // =========================================================

    private void DrawEnergyParticles(
        DrawingContext dc,
        double centerX,
        double centerY,
        AuraSettings settings)
    {
        /*
         * Tiny moving points around the perimeter.
         *
         * These prevent the ring from feeling static.
         */

        int particleCount =
            settings.ParticleCount;

        double radius =
            BaseRadius + 1.0;

        for (int i = 0;
             i < particleCount;
             i++)
        {
            double seed =
                i * 17.371;

            double angle =
                seed +
                _time *
                settings.ParticleSpeed *
                (0.65 +
                 (i % 3) * 0.16);

            double wobble =
                Math.Sin(
                    _time * 1.7 +
                    seed)
                * 1.8;

            double r =
                radius +
                wobble;

            double x =
                centerX +
                Math.Cos(angle) * r;

            double y =
                centerY +
                Math.Sin(angle) * r;

            double pulse =
                0.45 +
                0.55 *
                ((Math.Sin(
                    _time * 3.0 +
                    seed) + 1.0) * 0.5);

            Color color =
                i % 3 == 0
                    ? ElectricViolet
                    : ElectricCyan;

            double size =
                i % 4 == 0
                    ? 1.35
                    : 0.75;

            SolidColorBrush brush =
                new(
                    WithAlpha(
                        color,
                        0.55 * pulse));

            brush.Freeze();

            dc.DrawEllipse(
                brush,
                null,
                new Point(x, y),
                size,
                size);
        }
    }

    // =========================================================
    // CURVE DRAW
    // =========================================================

    private static void DrawCurve(
        DrawingContext dc,
        StreamGeometry geometry,
        Color color,
        double thickness)
    {
        if (color.A == 0)
            return;

        SolidColorBrush brush =
            new(color);

        brush.Freeze();

        Pen pen =
            new(
                brush,
                thickness);

        pen.LineJoin =
            PenLineJoin.Round;

        pen.StartLineCap =
            PenLineCap.Round;

        pen.EndLineCap =
            PenLineCap.Round;

        dc.DrawGeometry(
            null,
            pen,
            geometry);
    }

    // =========================================================
    // CLOSED SMOOTH CURVE
    // =========================================================

    private static StreamGeometry CreateClosedCurve(
        Point[] points)
    {
        StreamGeometry geometry =
            new();

        using StreamGeometryContext context =
            geometry.Open();

        Point first =
            points[0];

        context.BeginFigure(
            first,
            false,
            true);

        int count =
            points.Length;

        for (int i = 0;
             i < count;
             i++)
        {
            Point p0 =
                points[
                    (i - 1 + count) %
                    count];

            Point p1 =
                points[i];

            Point p2 =
                points[
                    (i + 1) %
                    count];

            Point p3 =
                points[
                    (i + 2) %
                    count];

            Point c1 =
                new Point(
                    p1.X +
                    (p2.X - p0.X) / 6.0,

                    p1.Y +
                    (p2.Y - p0.Y) / 6.0);

            Point c2 =
                new Point(
                    p2.X -
                    (p3.X - p1.X) / 6.0,

                    p2.Y -
                    (p3.Y - p1.Y) / 6.0);

            context.BezierTo(
                c1,
                c2,
                p2,
                true,
                true);
        }

        geometry.Freeze();

        return geometry;
    }

    // =========================================================
    // AURA COLOR
    // =========================================================

    private static Color GetAuraColor(
        int colorIndex,
        AuraSettings settings)
    {
        /*
         * State changes the color distribution.
         *
         * Still controlled:
         *
         * cyan
         * blue
         * violet
         *
         * Never rainbow.
         */

        return settings.ColorMode switch
        {
            AuraColorMode.CyanDominant =>
                colorIndex switch
                {
                    0 => ElectricCyan,
                    1 => NeonBlue,
                    _ => ElectricCyan
                },

            AuraColorMode.VioletDominant =>
                colorIndex switch
                {
                    0 => ElectricViolet,
                    1 => ElectricCyan,
                    _ => NeonBlue
                },

            AuraColorMode.BlueDominant =>
                colorIndex switch
                {
                    0 => NeonBlue,
                    1 => ElectricCyan,
                    _ => ElectricViolet
                },

            _ =>
                ElectricCyan
        };
    }

    // =========================================================
    // ALPHA
    // =========================================================

    private static Color WithAlpha(
        Color color,
        double multiplier)
    {
        byte alpha =
            (byte)Math.Clamp(
                color.A * multiplier,
                0,
                255);

        return Color.FromArgb(
            alpha,
            color.R,
            color.G,
            color.B);
    }

    // =========================================================
    // AURA SETTINGS
    // =========================================================

    private AuraSettings GetAuraSettings()
    {
        return _state switch
        {
            // =================================================
            // IDLE
            // =================================================

            CompanionState.Idle =>
                new AuraSettings
                {
                    SpawnRate = 0.72,

                    Lifetime = 3.0,

                    AuraDistance = 28.0,

                    Strength = 0.72,

                    BorderIntensity = 0.95,

                    BorderPulseSpeed = 1.20,

                    BorderPulseAmount = 1.0,

                    ParticleCount = 8,

                    ParticleSpeed = 0.65,

                    ColorMode =
                        AuraColorMode.CyanDominant
                },

            // =================================================
            // LISTENING
            // =================================================

            CompanionState.Listening =>
                new AuraSettings
                {
                    SpawnRate = 1.35,

                    Lifetime = 2.35,

                    AuraDistance = 31.0,

                    Strength = 0.88,

                    BorderIntensity = 1.12,

                    BorderPulseSpeed = 2.15,

                    BorderPulseAmount = 1.8,

                    ParticleCount = 11,

                    ParticleSpeed = 0.95,

                    ColorMode =
                        AuraColorMode.CyanDominant
                },

            // =================================================
            // THINKING
            // =================================================

            CompanionState.Thinking =>
                new AuraSettings
                {
                    SpawnRate = 1.15,

                    Lifetime = 2.65,

                    AuraDistance = 36.0,

                    Strength = 0.92,

                    BorderIntensity = 1.16,

                    BorderPulseSpeed = 1.75,

                    BorderPulseAmount = 2.1,

                    ParticleCount = 13,

                    ParticleSpeed = 1.15,

                    ColorMode =
                        AuraColorMode.VioletDominant
                },

            // =================================================
            // SPEAKING
            // =================================================

            CompanionState.Speaking =>
                new AuraSettings
                {
                    SpawnRate = 2.55,

                    Lifetime = 1.65,

                    AuraDistance = 40.0,

                    Strength = 1.12,

                    BorderIntensity = 1.38,

                    BorderPulseSpeed = 4.0,

                    BorderPulseAmount = 2.8,

                    ParticleCount = 18,

                    ParticleSpeed = 1.8,

                    ColorMode =
                        AuraColorMode.CyanDominant
                },

            // =================================================
            // AVOIDING
            // =================================================

            CompanionState.Avoiding =>
                new AuraSettings
                {
                    SpawnRate = 3.0,

                    Lifetime = 1.25,

                    AuraDistance = 47.0,

                    Strength = 1.20,

                    BorderIntensity = 1.45,

                    BorderPulseSpeed = 5.0,

                    BorderPulseAmount = 3.4,

                    ParticleCount = 22,

                    ParticleSpeed = 2.3,

                    ColorMode =
                        AuraColorMode.VioletDominant
                },

            // =================================================
            // MOVING
            // =================================================

            CompanionState.Moving =>
                new AuraSettings
                {
                    SpawnRate = 1.85,

                    Lifetime = 1.9,

                    AuraDistance = 33.0,

                    Strength = 1.0,

                    BorderIntensity = 1.25,

                    BorderPulseSpeed = 3.0,

                    BorderPulseAmount = 2.2,

                    ParticleCount = 15,

                    ParticleSpeed = 1.5,

                    ColorMode =
                        AuraColorMode.BlueDominant
                },

            // =================================================
            // DEFAULT
            // =================================================

            _ =>
                new AuraSettings
                {
                    SpawnRate = 0.72,

                    Lifetime = 3.0,

                    AuraDistance = 25.0,

                    Strength = 0.72,

                    BorderIntensity = 0.95,

                    BorderPulseSpeed = 1.20,

                    BorderPulseAmount = 1.0,

                    ParticleCount = 8,

                    ParticleSpeed = 0.65,

                    ColorMode =
                        AuraColorMode.CyanDominant
                }
        };
    }

    // =========================================================
    // EASING
    // =========================================================

    private static double EaseOutCubic(
        double value)
    {
        double inverse =
            1.0 - value;

        return 1.0 -
               inverse *
               inverse *
               inverse;
    }

    // =========================================================
    // SMOOTH STEP
    // =========================================================

    private static double SmoothStep(
        double edge0,
        double edge1,
        double value)
    {
        double t =
            Math.Clamp(
                (value - edge0) /
                (edge1 - edge0),
                0.0,
                1.0);

        return t * t *
               (3.0 - 2.0 * t);
    }

    // =========================================================
    // AURA WAVE
    // =========================================================

    private sealed class AuraWave
    {
        public double Progress;

        public double Lifetime;

        public double Strength;

        public double RadiusOffset;

        public double OrganicOffset;

        public int ColorIndex;

        public double Rotation;
    }

    // =========================================================
    // COLOR MODE
    // =========================================================

    private enum AuraColorMode
    {
        CyanDominant,

        BlueDominant,

        VioletDominant
    }

    // =========================================================
    // AURA SETTINGS
    // =========================================================

    private sealed class AuraSettings
    {
        public double SpawnRate;

        public double Lifetime;

        public double AuraDistance;

        public double Strength;

        public double BorderIntensity;

        public double BorderPulseSpeed;

        public double BorderPulseAmount;

        public int ParticleCount;

        public double ParticleSpeed;

        public AuraColorMode ColorMode;
    }
}