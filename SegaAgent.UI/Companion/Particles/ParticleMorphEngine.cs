/*
 * filename: ParticleMorphEngine.cs
 */

using System.Numerics;

using SegaAgent.Embodiment;

namespace SegaAgent.UI.Companion.Particles;

public sealed class ParticleMorphEngine
{
    // =========================================================
    // STATE
    // =========================================================

    private readonly ParticleRuntimeState[]
        _particles;


    private readonly ParticleTarget[]
        _targets;


    private readonly ParticleFormRegistry
        _forms;


    // =========================================================
    // INTENT
    // =========================================================

    private SegaVisualIntent
        _intent =
            SegaVisualIntent.RestingOrb;


    // =========================================================
    // PROVIDER
    // =========================================================

    private IParticleFormProvider
        _provider;


    // =========================================================
    // TIME
    // =========================================================

    private double
        _time;


    // =========================================================
    // STARTUP MATERIALIZATION
    // =========================================================

    private const double StartupEntranceDurationSeconds =
        1.80;


    private bool
        _startupEntrancePrepared;


    private bool
        _startupEntranceActive;


    private double
        _startupEntranceElapsed;


    private float
        _startupVisibility =
            1.0f;


    private float
        _startupCorePulse;


    // =========================================================
    // OUTPUT
    // =========================================================

    public int Count =>
        _particles.Length;


    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public ParticleMorphEngine(
        int particleCount,
        ParticleFormRegistry forms)
    {
        if (particleCount <=
            0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(particleCount));
        }


        _forms =
            forms
            ?? throw new ArgumentNullException(
                nameof(forms));


        _particles =
            new ParticleRuntimeState[
                particleCount];


        _targets =
            new ParticleTarget[
                particleCount];


        _provider =
            _forms.Resolve(
                _intent);


        InitializeParticles();
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


        _provider =
            _forms.Resolve(
                _intent);


        RebuildTargets();
    }


    // =========================================================
    // PREPARE STARTUP ENTRANCE
    //
    // Particles are moved into a dispersed orbital field before
    // the first visible frame. The actual entrance begins only
    // when StartStartupEntrance is called by CompanionWindow.
    // =========================================================

    public void PrepareStartupEntrance()
    {
        RebuildTargets();


        _startupEntrancePrepared =
            true;


        _startupEntranceActive =
            false;


        _startupEntranceElapsed =
            0.0;


        _startupVisibility =
            0.0f;


        _startupCorePulse =
            0.0f;


        for (
            int index = 0;
            index < _particles.Length;
            index++)
        {
            ParticleRuntimeState particle =
                _particles[index];


            ParticleTarget target =
                _targets[index];


            float seed =
                (
                    particle.ColorSeed %
                    997
                )
                /
                997.0f;


            float angle =
                particle.Phase *
                    1.73f
                +
                seed *
                    8.0f;


            float vertical =
                -0.72f
                +
                seed *
                    1.44f;


            float horizontal =
                MathF.Sqrt(
                    MathF.Max(
                        0.05f,
                        1.0f -
                        vertical *
                            vertical));


            Vector3 direction =
                new(
                    MathF.Cos(
                        angle)
                    *
                    horizontal,

                    vertical,

                    MathF.Sin(
                        angle)
                    *
                    horizontal);


            if (direction.LengthSquared() >
                0.0001f)
            {
                direction =
                    Vector3.Normalize(
                        direction);
            }


            float spread =
                1.45f
                +
                seed *
                    1.15f;


            particle.Position =
                direction *
                spread;


            Vector3 tangent =
                new(
                    -direction.Z,

                    MathF.Sin(
                        angle *
                            1.7f)
                    *
                    0.16f,

                    direction.X);


            if (tangent.LengthSquared() >
                0.0001f)
            {
                tangent =
                    Vector3.Normalize(
                        tangent);
            }


            particle.Velocity =
                tangent *
                (
                    0.42f
                    +
                    seed *
                        0.48f
                );


            particle.Size =
                target.Size *
                0.14f;


            particle.Brightness =
                target.Brightness *
                0.04f;


            particle.Role =
                target.Role;


            particle.Palette =
                target.Palette;


            _particles[index] =
                particle;
        }
    }


    // =========================================================
    // START STARTUP ENTRANCE
    // =========================================================

    public void StartStartupEntrance()
    {
        if (!_startupEntrancePrepared)
        {
            PrepareStartupEntrance();
        }


        _startupEntrancePrepared =
            false;


        _startupEntranceActive =
            true;


        _startupEntranceElapsed =
            0.0;
    }


    // =========================================================
    // UPDATE
    // =========================================================

    public void Update(
        double deltaSeconds)
    {
        deltaSeconds =
            Math.Clamp(
                deltaSeconds,
                0.001,
                0.05);


        _time +=
            deltaSeconds;


        if (
            _startupEntrancePrepared
            &&
            !_startupEntranceActive)
        {
            return;
        }


        UpdateStartupEntrance(
            deltaSeconds);


        _provider.BuildTargets(
            _targets,
            _intent,
            _time);


        float delta =
            (float)deltaSeconds;


        float cohesion =
            (float)_intent.Cohesion;


        float energy =
            (float)_intent.Energy;


        float tension =
            (float)_intent.Tension;


        float flow =
            (float)_intent.Flow;


        float focus =
            (float)_intent.Focus;


        // =====================================================
        // TARGET CONTROL
        //
        // Focus and cohesion tighten Sega's body.
        // Tension slightly increases responsiveness without
        // turning the orb into uncontrolled noise.
        // =====================================================

        float stiffness =
            10.5f
            +
            cohesion *
                18.5f
            +
            focus *
                7.0f
            +
            tension *
                3.0f;


        float damping =
            5.8f
            +
            cohesion *
                4.3f
            +
            focus *
                2.2f;


        float entranceProgress =
            ResolveStartupEntranceProgress();


        float entranceControl =
            SmoothStep01(
                entranceProgress);


        if (_startupEntranceActive)
        {
            stiffness *=
                0.20f
                +
                entranceControl *
                    0.80f;


            damping *=
                0.42f
                +
                entranceControl *
                    0.58f;
        }


        // =====================================================
        // FREE SWARM LIFE
        //
        // This is intentionally microscopic.
        //
        // The form provider owns meaningful motion such as
        // breathing, circulation and tension ripples.
        //
        // This layer only prevents perfect mechanical movement.
        // =====================================================

        float driftAmplitude =
            (
                0.008f
                +
                energy *
                    0.018f
                +
                flow *
                    0.010f
                +
                tension *
                    0.006f
            )
            *
            (
                1.0f -
                cohesion *
                    0.50f
            )
            *
            (
                1.0f -
                focus *
                    0.68f
            );


        float driftSpeed =
            0.48f
            +
            energy *
                0.48f
            +
            flow *
                0.30f;


        float sizeSpeed =
            8.5f
            +
            focus *
                4.0f;


        float brightnessSpeed =
            7.0f
            +
            energy *
                3.5f;


        for (
            int index = 0;
            index < _particles.Length;
            index++)
        {
            ParticleRuntimeState particle =
                _particles[index];


            ParticleTarget target =
                _targets[index];


            Vector3 displacement =
                target.Position -
                particle.Position;


            Vector3 acceleration =
                displacement *
                    stiffness
                -
                particle.Velocity *
                    damping;


            float phase =
                particle.Phase;


            Vector3 drift =
                new(
                    MathF.Sin(
                        (float)_time *
                            0.73f *
                            driftSpeed
                        +
                        phase),

                    MathF.Cos(
                        (float)_time *
                            0.57f *
                            driftSpeed
                        +
                        phase *
                            1.31f),

                    MathF.Sin(
                        (float)_time *
                            0.49f *
                            driftSpeed
                        +
                        phase *
                            1.77f));


            acceleration +=
                drift *
                driftAmplitude;


            // =================================================
            // STARTUP ORBITAL MATERIALIZATION
            // =================================================

            if (_startupEntranceActive)
            {
                float remaining =
                    1.0f -
                    entranceProgress;


                Vector3 tangent =
                    new(
                        -particle.Position.Z,

                        MathF.Sin(
                            phase +
                            (float)_time *
                                2.10f)
                        *
                        0.16f,

                        particle.Position.X);


                if (tangent.LengthSquared() >
                    0.0001f)
                {
                    tangent =
                        Vector3.Normalize(
                            tangent);


                    acceleration +=
                        tangent *
                        (
                            2.7f *
                            remaining *
                            remaining
                        );
                }
            }


            particle.Velocity +=
                acceleration *
                delta;


            particle.Position +=
                particle.Velocity *
                delta;


            particle.Size =
                Smooth(
                    particle.Size,
                    target.Size,
                    delta,
                    sizeSpeed);


            particle.Brightness =
                Smooth(
                    particle.Brightness,
                    target.Brightness,
                    delta,
                    brightnessSpeed);


            particle.Role =
                target.Role;


            particle.Palette =
                target.Palette;


            _particles[index] =
                particle;
        }
    }


    // =========================================================
    // READ
    // =========================================================

    public ParticleRenderState Get(
        int index)
    {
        if (
            index <
                0
            ||
            index >=
                _particles.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index));
        }


        ParticleRuntimeState particle =
            _particles[index];


        float pulseInfluence =
            particle.Role switch
            {
                ParticleRole.Core =>
                    1.00f,

                ParticleRole.Accent =>
                    0.82f,

                ParticleRole.Surface =>
                    0.28f,

                ParticleRole.Halo =>
                    0.16f,

                _ =>
                    0.20f
            };


        float size =
            particle.Size
            *
            (
                0.34f
                +
                _startupVisibility *
                    0.66f
            )
            *
            (
                1.0f
                +
                _startupCorePulse *
                    pulseInfluence *
                    0.10f
            );


        float brightness =
            particle.Brightness
            *
            _startupVisibility
            *
            (
                1.0f
                +
                _startupCorePulse *
                    pulseInfluence
            );


        return new ParticleRenderState(
            particle.Position,
            size,
            brightness,
            particle.Role,
            particle.Palette,
            particle.ColorSeed);
    }


    // =========================================================
    // INITIALIZE
    // =========================================================

    private void InitializeParticles()
    {
        Random random =
            new(
                731927);


        _provider.BuildTargets(
            _targets,
            _intent,
            0.0);


        for (
            int index = 0;
            index < _particles.Length;
            index++)
        {
            ParticleTarget target =
                _targets[index];


            Vector3 offset =
                new(
                    (float)(
                        random.NextDouble() *
                            0.20
                        -
                        0.10),

                    (float)(
                        random.NextDouble() *
                            0.20
                        -
                        0.10),

                    (float)(
                        random.NextDouble() *
                            0.20
                        -
                        0.10));


            _particles[index] =
                new ParticleRuntimeState
                {
                    Position =
                        target.Position +
                        offset,

                    Velocity =
                        Vector3.Zero,

                    Size =
                        target.Size,

                    Brightness =
                        target.Brightness,

                    Role =
                        target.Role,

                    Palette =
                        target.Palette,

                    Phase =
                        (float)(
                            random.NextDouble() *
                            Math.PI *
                            2.0),

                    ColorSeed =
                        random.Next(
                            0,
                            1000)
                };
        }
    }


    // =========================================================
    // REBUILD
    // =========================================================

    private void RebuildTargets()
    {
        _provider.BuildTargets(
            _targets,
            _intent,
            _time);
    }


    // =========================================================
    // UPDATE STARTUP ENTRANCE
    // =========================================================

    private void UpdateStartupEntrance(
        double deltaSeconds)
    {
        if (!_startupEntranceActive)
        {
            return;
        }


        _startupEntranceElapsed +=
            deltaSeconds;


        double progress =
            Math.Clamp(
                _startupEntranceElapsed /
                    StartupEntranceDurationSeconds,
                0.0,
                1.0);


        double reveal =
            Math.Clamp(
                (
                    progress -
                    0.02
                )
                /
                0.58,
                0.0,
                1.0);


        _startupVisibility =
            SmoothStep01(
                (float)reveal);


        double pulsePosition =
            (
                progress -
                0.82
            )
            /
            0.085;


        _startupCorePulse =
            (float)(
                Math.Exp(
                    -pulsePosition *
                    pulsePosition)
                *
                0.42);


        if (progress <
            1.0)
        {
            return;
        }


        _startupEntranceActive =
            false;


        _startupVisibility =
            1.0f;


        _startupCorePulse =
            0.0f;
    }


    // =========================================================
    // STARTUP PROGRESS
    // =========================================================

    private float ResolveStartupEntranceProgress()
    {
        if (_startupEntrancePrepared)
        {
            return 0.0f;
        }


        if (!_startupEntranceActive)
        {
            return 1.0f;
        }


        return (float)Math.Clamp(
            _startupEntranceElapsed /
                StartupEntranceDurationSeconds,
            0.0,
            1.0);
    }


    // =========================================================
    // SMOOTH STEP
    // =========================================================

    private static float SmoothStep01(
        float value)
    {
        value =
            Math.Clamp(
                value,
                0.0f,
                1.0f);


        return
            value *
            value *
            (
                3.0f -
                2.0f *
                value
            );
    }


    // =========================================================
    // SMOOTH
    // =========================================================

    private static float Smooth(
        float current,
        float target,
        float deltaSeconds,
        float speed)
    {
        float amount =
            1.0f -
            MathF.Exp(
                -deltaSeconds *
                speed);


        return
            current +
            (
                target -
                current
            )
            *
            amount;
    }


    // =========================================================
    // INTERNAL PARTICLE
    // =========================================================

    private struct ParticleRuntimeState
    {
        public Vector3 Position;

        public Vector3 Velocity;

        public float Size;

        public float Brightness;

        public ParticleRole Role;

        public ParticlePalette Palette;

        public float Phase;

        public int ColorSeed;
    }
}


// =============================================================
// RENDER STATE
// =============================================================

public readonly record struct ParticleRenderState(
    Vector3 Position,
    float Size,
    float Brightness,
    ParticleRole Role,
    ParticlePalette Palette,
    int ColorSeed);
