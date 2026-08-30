using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace SegaAgent.UI.Companion;

public sealed class LiquidBlobControl : FrameworkElement
{
    // =========================================================
    // VISUAL
    // =========================================================

    private readonly DrawingVisual _visual =
        new();


    // =========================================================
    // PARTICLES
    // =========================================================

    private const int CoreParticleCount = 255;

    private const int FragmentParticleCount = 48;


    private readonly Particle[] _coreParticles;

    private readonly Particle[] _fragmentParticles;


    // =========================================================
    // ANIMATION
    // =========================================================

    private double _time;

    private double _lastFrameTime;

    private bool _isRendering;

    private bool _isHovered;


    // =========================================================
    // STATE
    // =========================================================

    private CompanionState _state =
        CompanionState.Idle;


    private ParticleSettings _currentSettings;

    private ParticleSettings _targetSettings;


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
    // BRUSH RAMPS
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
    // EVENTS
    // =========================================================

    public event EventHandler? BlobHovered;

    public event EventHandler? BlobLeft;


    // =========================================================
    // STATE PROPERTY
    // =========================================================

    public CompanionState State
    {
        get =>
            _state;

        set
        {
            if (_state == value)
            {
                return;
            }


            _state =
                value;


            _targetSettings =
                GetSettings(
                    value);


            InvalidateVisual();
        }
    }


    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public LiquidBlobControl()
    {
        AddVisualChild(
            _visual);


        IsHitTestVisible =
            true;


        _coreParticles =
            CreateCoreParticles();


        _fragmentParticles =
            CreateFragmentParticles();


        _currentSettings =
            GetSettings(
                CompanionState.Idle);


        _targetSettings =
            _currentSettings;


        MouseEnter +=
            OnMouseEnter;


        MouseLeave +=
            OnMouseLeave;


        Loaded +=
            OnLoaded;


        Unloaded +=
            OnUnloaded;
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
    // LOADED
    // =========================================================

    private void OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        if (_isRendering)
        {
            return;
        }


        _isRendering =
            true;


        _lastFrameTime =
            GetCurrentSeconds();


        CompositionTarget.Rendering +=
            OnRendering;


        RenderEntity();
    }


    // =========================================================
    // UNLOADED
    // =========================================================

    private void OnUnloaded(
        object sender,
        RoutedEventArgs e)
    {
        if (!_isRendering)
        {
            return;
        }


        _isRendering =
            false;


        CompositionTarget.Rendering -=
            OnRendering;
    }


    // =========================================================
    // MOUSE
    // =========================================================

    private void OnMouseEnter(
        object sender,
        MouseEventArgs e)
    {
        if (_isHovered)
        {
            return;
        }


        _isHovered =
            true;


        BlobHovered?.Invoke(
            this,
            EventArgs.Empty);
    }


    private void OnMouseLeave(
        object sender,
        MouseEventArgs e)
    {
        if (!_isHovered)
        {
            return;
        }


        _isHovered =
            false;


        BlobLeft?.Invoke(
            this,
            EventArgs.Empty);
    }


    // =========================================================
    // FRAME
    // =========================================================

    private void OnRendering(
        object? sender,
        EventArgs e)
    {
        double now =
            GetCurrentSeconds();


        double delta =
            now -
            _lastFrameTime;


        _lastFrameTime =
            now;


        delta =
            Math.Clamp(
                delta,
                0.001,
                0.05);


        _time +=
            delta;


        // =====================================================
        // SMOOTH STATE MORPHING
        // =====================================================

        double transition =
            1.0 -
            Math.Exp(
                -delta * 4.5);


        _currentSettings =
            ParticleSettings.Lerp(
                _currentSettings,
                _targetSettings,
                transition);


        RenderEntity();
    }


    // =========================================================
    // TIME
    // =========================================================

    private static double GetCurrentSeconds()
    {
        return
            System.Environment.TickCount64 /
            1000.0;
    }


    // =========================================================
    // RENDER ENTITY
    // =========================================================

    private void RenderEntity()
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


        ParticleSettings settings =
            _currentSettings;


        // =====================================================
        // OUTER FRAGMENTS
        //
        // Render first so they remain behind the main body.
        // =====================================================

        DrawParticleSet(
            dc,
            centerX,
            centerY,
            _fragmentParticles,
            settings,
            true);


        // =====================================================
        // CORE ENTITY
        // =====================================================

        DrawParticleSet(
            dc,
            centerX,
            centerY,
            _coreParticles,
            settings,
            false);
    }


    // =========================================================
    // DRAW PARTICLES
    // =========================================================

    private void DrawParticleSet(
        DrawingContext dc,
        double centerX,
        double centerY,
        Particle[] particles,
        ParticleSettings settings,
        bool fragments)
    {
        double breathing =
            1.0 +
            Math.Sin(
                _time *
                settings.BreathSpeed)
            *
            settings.BreathAmount;


        double hoverExpansion =
            _isHovered
                ? 1.025
                : 1.0;


        double globalScale =
            breathing *
            hoverExpansion;


        double rotationY =
            _time *
            settings.RotationYSpeed;


        double rotationX =
            _time *
            settings.RotationXSpeed;


        for (int i = 0;
             i < particles.Length;
             i++)
        {
            Particle particle =
                particles[i];


            double particleWave =
                1.0 +
                Math.Sin(
                    _time *
                    settings.WaveSpeed +
                    particle.Phase)
                *
                settings.WaveAmount;


            double fragmentExpansion =
                1.0;


            if (fragments)
            {
                double pulse =
                    Math.Max(
                        0.0,
                        Math.Sin(
                            _time *
                            settings.FragmentPulseSpeed +
                            particle.Phase));


                fragmentExpansion =
                    settings.FragmentScale +
                    pulse *
                    settings.FragmentPulseAmount;
            }


            double scale =
                globalScale *
                particleWave *
                fragmentExpansion;


            double x =
                particle.X *
                settings.RadiusX *
                scale;


            double y =
                particle.Y *
                settings.RadiusY *
                scale;


            double z =
                particle.Z *
                settings.RadiusZ *
                scale;


            // =================================================
            // PARTICLE SWIRL
            // =================================================

            double swirl =
                settings.SwirlStrength *
                particle.Y +
                Math.Sin(
                    particle.Phase +
                    _time * 0.65)
                *
                settings.SwirlStrength *
                0.14;


            RotateY(
                ref x,
                ref z,
                swirl);


            // =================================================
            // LOCAL PARTICLE DRIFT
            // =================================================

            double driftTime =
                _time *
                particle.DriftSpeed;


            double turbulence =
                settings.Turbulence;


            x +=
                Math.Sin(
                    driftTime +
                    particle.Phase)
                *
                turbulence;


            y +=
                Math.Cos(
                    driftTime * 0.87 +
                    particle.Phase * 1.7)
                *
                turbulence *
                0.72;


            z +=
                Math.Sin(
                    driftTime * 0.61 +
                    particle.Phase * 2.3)
                *
                turbulence *
                0.56;


            // =================================================
            // GLOBAL 3D ROTATION
            // =================================================

            RotateY(
                ref x,
                ref z,
                rotationY);


            RotateX(
                ref y,
                ref z,
                rotationX);


            // =================================================
            // PERSPECTIVE
            // =================================================

            double normalizedDepth =
                Math.Clamp(
                    (
                        z /
                        Math.Max(
                            settings.RadiusZ,
                            1.0)
                        +
                        1.0
                    )
                    *
                    0.5,
                    0.0,
                    1.0);


            double perspective =
                0.88 +
                normalizedDepth *
                0.24;


            double screenX =
                centerX +
                x *
                perspective;


            double screenY =
                centerY +
                y *
                perspective;


            // =================================================
            // DEPTH SIZE
            // =================================================

            double size =
                particle.Size *
                settings.PointScale *
                (
                    0.62 +
                    normalizedDepth *
                    0.78
                );


            if (fragments)
            {
                size *=
                    0.78;
            }


            // =================================================
            // BRIGHTNESS
            // =================================================

            double intensity =
                (
                    0.42 +
                    normalizedDepth *
                    0.72
                )
                *
                settings.Brightness;


            if (fragments)
            {
                intensity *=
                    settings.FragmentVisibility;
            }


            int brightnessLevel =
                ResolveBrightnessLevel(
                    intensity);


            int colorGroup =
                ResolveColorGroup(
                    settings.ColorMode,
                    particle.ColorSeed);


            Brush[] brushes =
                GetBrushRamp(
                    colorGroup);


            // =================================================
            // SOFT PARTICLE GLOW
            //
            // No lines.
            // No border.
            //
            // The glow belongs to each individual particle.
            // =================================================

            if (brightnessLevel >= 2 ||
                particle.GlowSeed)
            {
                int glowLevel =
                    Math.Max(
                        0,
                        brightnessLevel - 2);


                dc.DrawEllipse(
                    brushes[glowLevel],
                    null,
                    new Point(
                        screenX,
                        screenY),
                    size * 2.35,
                    size * 2.35);
            }


            // =================================================
            // PARTICLE CORE
            // =================================================

            dc.DrawEllipse(
                brushes[brightnessLevel],
                null,
                new Point(
                    screenX,
                    screenY),
                size,
                size);
        }
    }


    // =========================================================
    // CREATE CORE PARTICLES
    // =========================================================

    private static Particle[] CreateCoreParticles()
    {
        Random random =
            new(
                731927);


        Particle[] particles =
            new Particle[
                CoreParticleCount];


        for (int i = 0;
             i < particles.Length;
             i++)
        {
            // =================================================
            // RANDOM POINT INSIDE A SPHERE
            // =================================================

            double longitude =
                random.NextDouble() *
                Math.PI *
                2.0;


            double z =
                random.NextDouble() *
                2.0 -
                1.0;


            double horizontal =
                Math.Sqrt(
                    Math.Max(
                        0.0,
                        1.0 -
                        z * z));


            // Bias some particles toward the outside,
            // while still keeping a populated center.

            double radius =
                0.10 +
                Math.Pow(
                    random.NextDouble(),
                    0.58)
                *
                0.90;


            double x =
                Math.Cos(
                    longitude)
                *
                horizontal *
                radius;


            double y =
                Math.Sin(
                    longitude)
                *
                horizontal *
                radius;


            double finalZ =
                z *
                radius;


            particles[i] =
                new Particle(
                    x,
                    y,
                    finalZ,

                    random.NextDouble() *
                    Math.PI *
                    2.0,

                    0.45 +
                    random.NextDouble() *
                    0.85,

                    0.50 +
                    random.NextDouble() *
                    0.70,

                    random.Next(
                        0,
                        1000),

                    random.NextDouble() <
                    0.16);
        }


        return particles;
    }


    // =========================================================
    // CREATE FRAGMENT PARTICLES
    // =========================================================

    private static Particle[]
        CreateFragmentParticles()
    {
        Random random =
            new(
                183521);


        Particle[] particles =
            new Particle[
                FragmentParticleCount];


        for (int i = 0;
             i < particles.Length;
             i++)
        {
            double longitude =
                random.NextDouble() *
                Math.PI *
                2.0;


            double z =
                random.NextDouble() *
                2.0 -
                1.0;


            double horizontal =
                Math.Sqrt(
                    Math.Max(
                        0.0,
                        1.0 -
                        z * z));


            double radius =
                1.03 +
                random.NextDouble() *
                0.38;


            double x =
                Math.Cos(
                    longitude)
                *
                horizontal *
                radius;


            double y =
                Math.Sin(
                    longitude)
                *
                horizontal *
                radius;


            double finalZ =
                z *
                radius;


            particles[i] =
                new Particle(
                    x,
                    y,
                    finalZ,

                    random.NextDouble() *
                    Math.PI *
                    2.0,

                    0.55 +
                    random.NextDouble() *
                    1.15,

                    0.40 +
                    random.NextDouble() *
                    0.55,

                    random.Next(
                        0,
                        1000),

                    random.NextDouble() <
                    0.10);
        }


        return particles;
    }


    // =========================================================
    // ROTATE Y
    // =========================================================

    private static void RotateY(
        ref double x,
        ref double z,
        double angle)
    {
        double cosine =
            Math.Cos(
                angle);


        double sine =
            Math.Sin(
                angle);


        double newX =
            x *
            cosine +
            z *
            sine;


        double newZ =
            -x *
            sine +
            z *
            cosine;


        x =
            newX;


        z =
            newZ;
    }


    // =========================================================
    // ROTATE X
    // =========================================================

    private static void RotateX(
        ref double y,
        ref double z,
        double angle)
    {
        double cosine =
            Math.Cos(
                angle);


        double sine =
            Math.Sin(
                angle);


        double newY =
            y *
            cosine -
            z *
            sine;


        double newZ =
            y *
            sine +
            z *
            cosine;


        y =
            newY;


        z =
            newZ;
    }


    // =========================================================
    // BRIGHTNESS
    // =========================================================

    private static int ResolveBrightnessLevel(
        double intensity)
    {
        if (intensity < 0.48)
        {
            return 0;
        }


        if (intensity < 0.76)
        {
            return 1;
        }


        if (intensity < 1.05)
        {
            return 2;
        }


        return 3;
    }


    // =========================================================
    // COLOR GROUP
    // =========================================================

    private static int ResolveColorGroup(
        ParticleColorMode mode,
        int seed)
    {
        int value =
            Math.Abs(seed) %
            100;


        return mode switch
        {
            // =================================================
            // CYAN
            // =================================================

            ParticleColorMode.CyanDominant =>
                value switch
                {
                    < 58 => 0,
                    < 82 => 1,
                    < 94 => 2,
                    _ => 3
                },


            // =================================================
            // BLUE
            // =================================================

            ParticleColorMode.BlueDominant =>
                value switch
                {
                    < 52 => 1,
                    < 76 => 0,
                    < 92 => 2,
                    _ => 3
                },


            // =================================================
            // VIOLET
            // =================================================

            ParticleColorMode.VioletDominant =>
                value switch
                {
                    < 48 => 2,
                    < 73 => 1,
                    < 91 => 0,
                    _ => 3
                },


            _ =>
                0
        };
    }


    // =========================================================
    // BRUSH GROUP
    // =========================================================

    private static Brush[] GetBrushRamp(
        int group)
    {
        return group switch
        {
            0 =>
                CyanBrushes,

            1 =>
                BlueBrushes,

            2 =>
                VioletBrushes,

            3 =>
                WhiteBrushes,

            _ =>
                CyanBrushes
        };
    }


    // =========================================================
    // CREATE BRUSH RAMP
    // =========================================================

    private static Brush[] CreateBrushRamp(
        Color color)
    {
        byte[] alpha =
        {
            30,
            78,
            155,
            235
        };


        Brush[] brushes =
            new Brush[
                alpha.Length];


        for (int i = 0;
             i < alpha.Length;
             i++)
        {
            SolidColorBrush brush =
                new(
                    Color.FromArgb(
                        alpha[i],
                        color.R,
                        color.G,
                        color.B));


            brush.Freeze();


            brushes[i] =
                brush;
        }


        return brushes;
    }


    // =========================================================
    // SETTINGS
    // =========================================================

    private static ParticleSettings GetSettings(
        CompanionState state)
    {
        return state switch
        {
            // =================================================
            // IDLE
            //
            // Loose floating intelligent cloud.
            // Slow breathing.
            // Slow rotation.
            // =================================================

            CompanionState.Idle =>
                new ParticleSettings(
                    RadiusX: 38.0,
                    RadiusY: 38.0,
                    RadiusZ: 38.0,

                    RotationYSpeed: 0.22,
                    RotationXSpeed: 0.07,

                    Turbulence: 0.70,

                    BreathAmount: 0.035,
                    BreathSpeed: 1.15,

                    WaveAmount: 0.012,
                    WaveSpeed: 1.30,

                    SwirlStrength: 0.10,

                    PointScale: 1.0,

                    Brightness: 0.90,

                    FragmentScale: 1.0,
                    FragmentPulseAmount: 0.035,
                    FragmentPulseSpeed: 0.75,
                    FragmentVisibility: 0.58,

                    ColorMode:
                        ParticleColorMode.CyanDominant),


            // =================================================
            // LISTENING
            //
            // More coherent.
            // Slightly taller.
            // Cyan becomes stronger.
            // =================================================

            CompanionState.Listening =>
                new ParticleSettings(
                    RadiusX: 36.0,
                    RadiusY: 43.0,
                    RadiusZ: 37.0,

                    RotationYSpeed: 0.40,
                    RotationXSpeed: 0.10,

                    Turbulence: 0.82,

                    BreathAmount: 0.048,
                    BreathSpeed: 2.0,

                    WaveAmount: 0.025,
                    WaveSpeed: 2.25,

                    SwirlStrength: 0.18,

                    PointScale: 1.04,

                    Brightness: 1.05,

                    FragmentScale: 1.02,
                    FragmentPulseAmount: 0.06,
                    FragmentPulseSpeed: 1.65,
                    FragmentVisibility: 0.72,

                    ColorMode:
                        ParticleColorMode.CyanDominant),


            // =================================================
            // THINKING
            //
            // Faster internal rotation.
            // More compression.
            // Violet/blue intelligence pattern.
            // =================================================

            CompanionState.Thinking =>
                new ParticleSettings(
                    RadiusX: 35.0,
                    RadiusY: 36.0,
                    RadiusZ: 39.0,

                    RotationYSpeed: 1.10,
                    RotationXSpeed: 0.32,

                    Turbulence: 1.02,

                    BreathAmount: 0.025,
                    BreathSpeed: 1.75,

                    WaveAmount: 0.018,
                    WaveSpeed: 2.40,

                    SwirlStrength: 0.72,

                    PointScale: 1.03,

                    Brightness: 1.10,

                    FragmentScale: 1.05,
                    FragmentPulseAmount: 0.075,
                    FragmentPulseSpeed: 1.8,
                    FragmentVisibility: 0.76,

                    ColorMode:
                        ParticleColorMode.VioletDominant),


            // =================================================
            // SPEAKING
            //
            // Pulses physically travel through the particle
            // structure instead of drawing sound rings.
            // =================================================

            CompanionState.Speaking =>
                new ParticleSettings(
                    RadiusX: 40.0,
                    RadiusY: 40.0,
                    RadiusZ: 40.0,

                    RotationYSpeed: 0.68,
                    RotationXSpeed: 0.18,

                    Turbulence: 1.20,

                    BreathAmount: 0.055,
                    BreathSpeed: 3.7,

                    WaveAmount: 0.075,
                    WaveSpeed: 5.5,

                    SwirlStrength: 0.28,

                    PointScale: 1.10,

                    Brightness: 1.25,

                    FragmentScale: 1.05,
                    FragmentPulseAmount: 0.17,
                    FragmentPulseSpeed: 4.8,
                    FragmentVisibility: 0.90,

                    ColorMode:
                        ParticleColorMode.CyanDominant),


            // =================================================
            // AVOIDING
            //
            // Compact fast escape configuration.
            // =================================================

            CompanionState.Avoiding =>
                new ParticleSettings(
                    RadiusX: 47.0,
                    RadiusY: 29.0,
                    RadiusZ: 34.0,

                    RotationYSpeed: 1.65,
                    RotationXSpeed: 0.48,

                    Turbulence: 2.65,

                    BreathAmount: 0.035,
                    BreathSpeed: 5.0,

                    WaveAmount: 0.045,
                    WaveSpeed: 5.8,

                    SwirlStrength: 0.80,

                    PointScale: 1.04,

                    Brightness: 1.28,

                    FragmentScale: 1.16,
                    FragmentPulseAmount: 0.18,
                    FragmentPulseSpeed: 5.2,
                    FragmentVisibility: 0.92,

                    ColorMode:
                        ParticleColorMode.VioletDominant),


            // =================================================
            // MOVING
            //
            // Streamlined particle structure.
            // =================================================

            CompanionState.Moving =>
                new ParticleSettings(
                    RadiusX: 45.0,
                    RadiusY: 31.0,
                    RadiusZ: 35.0,

                    RotationYSpeed: 1.0,
                    RotationXSpeed: 0.25,

                    Turbulence: 1.55,

                    BreathAmount: 0.026,
                    BreathSpeed: 3.2,

                    WaveAmount: 0.030,
                    WaveSpeed: 4.0,

                    SwirlStrength: 0.48,

                    PointScale: 1.02,

                    Brightness: 1.15,

                    FragmentScale: 1.10,
                    FragmentPulseAmount: 0.11,
                    FragmentPulseSpeed: 3.5,
                    FragmentVisibility: 0.82,

                    ColorMode:
                        ParticleColorMode.BlueDominant),


            // =================================================
            // DEFAULT
            // =================================================

            _ =>
                GetSettings(
                    CompanionState.Idle)
        };
    }


    // =========================================================
    // PARTICLE
    // =========================================================

    private readonly record struct Particle(
        double X,
        double Y,
        double Z,
        double Phase,
        double DriftSpeed,
        double Size,
        int ColorSeed,
        bool GlowSeed);


    // =========================================================
    // COLOR MODE
    // =========================================================

    private enum ParticleColorMode
    {
        CyanDominant,

        BlueDominant,

        VioletDominant
    }


    // =========================================================
    // PARTICLE SETTINGS
    // =========================================================

    private readonly record struct ParticleSettings(
        double RadiusX,
        double RadiusY,
        double RadiusZ,

        double RotationYSpeed,
        double RotationXSpeed,

        double Turbulence,

        double BreathAmount,
        double BreathSpeed,

        double WaveAmount,
        double WaveSpeed,

        double SwirlStrength,

        double PointScale,

        double Brightness,

        double FragmentScale,
        double FragmentPulseAmount,
        double FragmentPulseSpeed,
        double FragmentVisibility,

        ParticleColorMode ColorMode)
    {
        public static ParticleSettings Lerp(
            ParticleSettings current,
            ParticleSettings target,
            double amount)
        {
            amount =
                Math.Clamp(
                    amount,
                    0.0,
                    1.0);


            return new ParticleSettings(
                RadiusX:
                    Mix(
                        current.RadiusX,
                        target.RadiusX,
                        amount),

                RadiusY:
                    Mix(
                        current.RadiusY,
                        target.RadiusY,
                        amount),

                RadiusZ:
                    Mix(
                        current.RadiusZ,
                        target.RadiusZ,
                        amount),

                RotationYSpeed:
                    Mix(
                        current.RotationYSpeed,
                        target.RotationYSpeed,
                        amount),

                RotationXSpeed:
                    Mix(
                        current.RotationXSpeed,
                        target.RotationXSpeed,
                        amount),

                Turbulence:
                    Mix(
                        current.Turbulence,
                        target.Turbulence,
                        amount),

                BreathAmount:
                    Mix(
                        current.BreathAmount,
                        target.BreathAmount,
                        amount),

                BreathSpeed:
                    Mix(
                        current.BreathSpeed,
                        target.BreathSpeed,
                        amount),

                WaveAmount:
                    Mix(
                        current.WaveAmount,
                        target.WaveAmount,
                        amount),

                WaveSpeed:
                    Mix(
                        current.WaveSpeed,
                        target.WaveSpeed,
                        amount),

                SwirlStrength:
                    Mix(
                        current.SwirlStrength,
                        target.SwirlStrength,
                        amount),

                PointScale:
                    Mix(
                        current.PointScale,
                        target.PointScale,
                        amount),

                Brightness:
                    Mix(
                        current.Brightness,
                        target.Brightness,
                        amount),

                FragmentScale:
                    Mix(
                        current.FragmentScale,
                        target.FragmentScale,
                        amount),

                FragmentPulseAmount:
                    Mix(
                        current.FragmentPulseAmount,
                        target.FragmentPulseAmount,
                        amount),

                FragmentPulseSpeed:
                    Mix(
                        current.FragmentPulseSpeed,
                        target.FragmentPulseSpeed,
                        amount),

                FragmentVisibility:
                    Mix(
                        current.FragmentVisibility,
                        target.FragmentVisibility,
                        amount),

                ColorMode:
                    target.ColorMode);
        }


        private static double Mix(
            double a,
            double b,
            double amount)
        {
            return
                a +
                (
                    b - a
                )
                *
                amount;
        }
    }
}