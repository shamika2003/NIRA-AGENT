/*
 * filename: ParticleMorphEngine.cs
 */

using System.Numerics;

using NIRAAgent.Embodiment;

namespace NIRAAgent.UI.Companion.Particles;

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

    private NIRAVisualIntent
        _intent =
            NIRAVisualIntent.RestingOrb;


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

    // =========================================================
    // CINEMATIC STARTUP
    //
    // 1. distant dust wakes from darkness
    // 2. particles spiral through the local volume
    // 3. the living blob collapses into its real body
    // 4. NIRA resolves and the body gives one soft pulse
    // =========================================================

    private const double StartupEntranceDurationSeconds =
        3.20;


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
        NIRAVisualIntent intent)
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
                    9.4f;


            float vertical =
                -0.82f
                +
                seed *
                    1.64f;


            float horizontal =
                MathF.Sqrt(
                    MathF.Max(
                        0.04f,
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


            float organicSpread =
                0.62f
                +
                seed *
                    1.46f
                +
                MathF.Sin(
                    angle *
                        2.31f
                    +
                    particle.Phase)
                *
                0.13f;


            particle.Position =
                direction *
                organicSpread;


            Vector3 tangent =
                new(
                    -direction.Z,

                    MathF.Sin(
                        angle *
                            1.7f)
                    *
                    0.18f,

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
                    0.72f
                    +
                    seed *
                        0.92f
                )
                -
                direction *
                (
                    0.08f
                    +
                    seed *
                        0.10f
                );


            particle.Size =
                target.Size *
                0.10f;


            particle.Brightness =
                target.Brightness *
                0.018f;


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
        // Focus and cohesion tighten NIRA's body.
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
            float formation =
                SmoothStep01(
                    Math.Clamp(
                        (
                            entranceProgress -
                            0.18f
                        )
                        /
                        0.66f,
                        0.0f,
                        1.0f));


            stiffness *=
                0.10f
                +
                formation *
                    0.90f;


            damping *=
                0.24f
                +
                formation *
                    0.76f;
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
            // CINEMATIC STARTUP MATERIALIZATION
            // =================================================

            if (_startupEntranceActive)
            {
                float p =
                    entranceProgress;


                float swirl =
                    1.0f -
                    SmoothStep01(
                        Math.Clamp(
                            (
                                p -
                                0.48f
                            )
                            /
                            0.34f,
                            0.0f,
                            1.0f));


                float collapse =
                    SmoothStep01(
                        Math.Clamp(
                            (
                                p -
                                0.18f
                            )
                            /
                            0.58f,
                            0.0f,
                            1.0f));


                Vector3 radial =
                    particle.Position;


                if (radial.LengthSquared() >
                    0.0001f)
                {
                    radial =
                        Vector3.Normalize(
                            radial);
                }


                Vector3 tangent =
                    new(
                        -particle.Position.Z,

                        MathF.Sin(
                            phase +
                            (float)_time *
                                2.25f)
                        *
                        0.20f,

                        particle.Position.X);


                if (tangent.LengthSquared() >
                    0.0001f)
                {
                    tangent =
                        Vector3.Normalize(
                            tangent);
                }


                acceleration +=
                    tangent *
                    (
                        4.35f *
                        swirl *
                        swirl
                    );


                acceleration -=
                    radial *
                    (
                        1.20f *
                        swirl
                        +
                        1.55f *
                        collapse
                    );


                acceleration.Y +=
                    MathF.Sin(
                        phase *
                            1.37f
                        +
                        (float)_time *
                            3.0f)
                    *
                    0.46f
                    *
                    swirl;
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


        float startupReveal =
            _startupVisibility;


        if (_startupEntranceActive)
        {
            float progress =
                ResolveStartupEntranceProgress();


            float seed =
                (
                    Math.Abs(
                        particle.ColorSeed)
                    %
                    997
                )
                /
                997.0f;


            float roleDelay =
                particle.Role switch
                {
                    ParticleRole.Halo =>
                        0.00f,

                    ParticleRole.Surface =>
                        0.025f,

                    ParticleRole.Core =>
                        0.10f,

                    ParticleRole.Accent =>
                        0.15f,

                    _ =>
                        0.04f
                };


            float stagger =
                seed *
                0.13f;


            float localReveal =
                SmoothStep01(
                    Math.Clamp(
                        (
                            progress -
                            roleDelay -
                            stagger
                        )
                        /
                        0.32f,
                        0.0f,
                        1.0f));


            startupReveal *=
                localReveal;
        }


        float size =
            particle.Size
            *
            (
                0.20f
                +
                startupReveal *
                    0.80f
            )
            *
            (
                1.0f
                +
                _startupCorePulse *
                    pulseInfluence *
                    0.18f
            );


        float brightness =
            particle.Brightness
            *
            startupReveal
            *
            (
                1.0f
                +
                _startupCorePulse *
                    pulseInfluence *
                    1.35f
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
                    0.01
                )
                /
                0.47,
                0.0,
                1.0);


        _startupVisibility =
            SmoothStep01(
                (float)reveal);


        double ignitionPosition =
            (
                progress -
                0.20
            )
            /
            0.075;


        double lockPosition =
            (
                progress -
                0.82
            )
            /
            0.060;


        double settlePosition =
            (
                progress -
                0.94
            )
            /
            0.045;


        double ignitionPulse =
            Math.Exp(
                -ignitionPosition *
                ignitionPosition)
            *
            0.16;


        double lockPulse =
            Math.Exp(
                -lockPosition *
                lockPosition)
            *
            0.62;


        double settlePulse =
            Math.Exp(
                -settlePosition *
                settlePosition)
            *
            0.12;


        _startupCorePulse =
            (float)(
                ignitionPulse +
                lockPulse +
                settlePulse);


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
