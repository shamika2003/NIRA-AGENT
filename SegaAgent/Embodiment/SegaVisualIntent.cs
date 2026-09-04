/*
 * filename: SegaVisualIntent.cs
 */

namespace SegaAgent.Embodiment;


// =============================================================
// SOURCE
// =============================================================

public enum SegaVisualIntentSource
{
    Automatic,

    Agent,

    User,

    Tool
}


// =============================================================
// VISUAL INTENT
//
// This is the provider-independent physical expression contract
// for Sega's particle body.
//
// It describes WHAT the body should feel like, not HOW any
// renderer must implement it.
//
// It deliberately contains continuous dimensions rather than
// hard-coded emotional forms such as "angry orb" or "happy orb".
//
// Character state, mind state and future tools can therefore
// combine naturally into one visual result.
// =============================================================

public sealed record SegaVisualIntent
{
    // =========================================================
    // FORM PROVIDER
    // =========================================================

    public string FormId
    {
        get;
        init;
    } =
        SegaVisualFormIds.Orb;


    // =========================================================
    // OPTIONAL SEMANTIC DESCRIPTION
    //
    // Reserved for future procedural/generated forms.
    //
    // The canonical Sega orb does not require a description.
    // =========================================================

    public string Description
    {
        get;
        init;
    } =
        string.Empty;


    // =========================================================
    // ENERGY
    //
    // Overall physical activity / arousal.
    // =========================================================

    public double Energy
    {
        get;
        init;
    } =
        0.30;


    // =========================================================
    // COHESION
    //
    // How tightly particles stay committed to their target form.
    // =========================================================

    public double Cohesion
    {
        get;
        init;
    } =
        0.88;


    // =========================================================
    // PRESENCE
    //
    // Visual confidence / prominence.
    //
    // Providers may express this through brightness, density,
    // halo strength or other non-geometric cues.
    // =========================================================

    public double Presence
    {
        get;
        init;
    } =
        0.65;


    // =========================================================
    // SCALE
    // =========================================================

    public double Scale
    {
        get;
        init;
    } =
        1.0;


    // =========================================================
    // TENSION
    //
    // Surface strain / agitation.
    //
    // High tension does not mean a specific emotion. It can come
    // from irritation, concern, pressure or intense concentration.
    // =========================================================

    public double Tension
    {
        get;
        init;
    } =
        0.10;


    // =========================================================
    // FLOW
    //
    // Strength of internal circulation and directional movement.
    // =========================================================

    public double Flow
    {
        get;
        init;
    } =
        0.30;


    // =========================================================
    // PULSE
    //
    // Rhythmic expansion / contraction and brightness breathing.
    // =========================================================

    public double Pulse
    {
        get;
        init;
    } =
        0.20;


    // =========================================================
    // FOCUS
    //
    // How controlled and deliberate the particle motion is.
    //
    // High focus suppresses unnecessary swarm noise without
    // making Sega visually dead.
    // =========================================================

    public double Focus
    {
        get;
        init;
    } =
        0.25;


    // =========================================================
    // SOURCE
    // =========================================================

    public SegaVisualIntentSource Source
    {
        get;
        init;
    } =
        SegaVisualIntentSource.Automatic;


    // =========================================================
    // RESTING ORB
    // =========================================================

    public static SegaVisualIntent RestingOrb =>
        new()
        {
            FormId =
                SegaVisualFormIds.Orb,

            Energy =
                0.24,

            Cohesion =
                0.80,

            Presence =
                0.56,

            Scale =
                1.0,

            Tension =
                0.08,

            Flow =
                0.28,

            Pulse =
                0.20,

            Focus =
                0.24,

            Source =
                SegaVisualIntentSource.Automatic
        };


    // =========================================================
    // NORMALIZE
    // =========================================================

    public SegaVisualIntent Normalize()
    {
        string formId =
            string.IsNullOrWhiteSpace(
                FormId)
                ? SegaVisualFormIds.Orb
                : FormId.Trim();


        return this with
        {
            FormId =
                formId,

            Description =
                Description?
                    .Trim()
                ?? string.Empty,

            Energy =
                Clamp01(
                    Energy),

            Cohesion =
                Clamp01(
                    Cohesion),

            Presence =
                Clamp01(
                    Presence),

            Scale =
                Math.Clamp(
                    Scale,
                    0.35,
                    2.5),

            Tension =
                Clamp01(
                    Tension),

            Flow =
                Clamp01(
                    Flow),

            Pulse =
                Clamp01(
                    Pulse),

            Focus =
                Clamp01(
                    Focus)
        };
    }


    // =========================================================
    // CLAMP
    // =========================================================

    private static double Clamp01(
        double value)
    {
        return Math.Clamp(
            value,
            0.0,
            1.0);
    }
}
