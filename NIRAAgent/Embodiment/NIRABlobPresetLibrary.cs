/*
 * filename: NIRABlobPresetLibrary.cs
 */

namespace NIRAAgent.Embodiment;

public static class NIRABlobPresetLibrary
{
    public static NIRAVisualIntent Get(
        NIRABlobPresetId preset)
    {
        return preset switch
        {
            NIRABlobPresetId.Core => BuildCore(),
            NIRABlobPresetId.Flow => BuildFlow(),
            NIRABlobPresetId.Focus => BuildFocus(),
            NIRABlobPresetId.Speak => BuildSpeak(),
            NIRABlobPresetId.Warm => BuildWarm(),
            NIRABlobPresetId.Alert => BuildAlert(),
            NIRABlobPresetId.Calm => BuildCalm(),
            NIRABlobPresetId.Custom => BuildCustom(),
            _ => NIRAVisualIntent.RestingOrb
        };
    }

    private static NIRAVisualIntent BuildCore()
    {
        return new NIRAVisualIntent()
        {
            Description = "preset:core",
            FormId = NIRAVisualFormIds.Orb,
            ExpressionId = NIRAVisualExpressionIds.None,
            ExpressionStrength = 0.0,
            Energy = 0.24,
            Cohesion = 0.84,
            Presence = 0.58,
            Scale = 1.00,
            Tension = 0.07,
            Flow = 0.28,
            Pulse = 0.18,
            Focus = 0.46,
            Source = NIRAVisualIntentSource.Tool
        }.Normalize();
    }

    private static NIRAVisualIntent BuildFlow()
    {
        return new NIRAVisualIntent()
        {
            Description = "preset:flow",
            FormId = NIRAVisualFormIds.Orb,
            ExpressionId = NIRAVisualExpressionIds.None,
            ExpressionStrength = 0.0,
            Energy = 0.40,
            Cohesion = 0.78,
            Presence = 0.68,
            Scale = 1.00,
            Tension = 0.12,
            Flow = 0.84,
            Pulse = 0.30,
            Focus = 0.44,
            Source = NIRAVisualIntentSource.Tool
        }.Normalize();
    }

    private static NIRAVisualIntent BuildFocus()
    {
        return new NIRAVisualIntent()
        {
            Description = "preset:focus",
            FormId = NIRAVisualFormIds.Orb,
            ExpressionId = NIRAVisualExpressionIds.None,
            ExpressionStrength = 0.0,
            Energy = 0.46,
            Cohesion = 0.92,
            Presence = 0.72,
            Scale = 1.02,
            Tension = 0.16,
            Flow = 0.30,
            Pulse = 0.16,
            Focus = 0.94,
            Source = NIRAVisualIntentSource.Tool
        }.Normalize();
    }

    private static NIRAVisualIntent BuildSpeak()
    {
        return new NIRAVisualIntent()
        {
            Description = "preset:speak",
            FormId = NIRAVisualFormIds.Orb,
            ExpressionId = NIRAVisualExpressionIds.None,
            ExpressionStrength = 0.0,
            Energy = 0.60,
            Cohesion = 0.82,
            Presence = 0.84,
            Scale = 1.03,
            Tension = 0.20,
            Flow = 0.76,
            Pulse = 0.58,
            Focus = 0.62,
            Source = NIRAVisualIntentSource.Tool
        }.Normalize();
    }

    private static NIRAVisualIntent BuildWarm()
    {
        return new NIRAVisualIntent()
        {
            Description = "preset:warm",
            FormId = NIRAVisualFormIds.Orb,
            ExpressionId = NIRAVisualExpressionIds.None,
            ExpressionStrength = 0.0,
            Energy = 0.42,
            Cohesion = 0.84,
            Presence = 0.86,
            Scale = 1.02,
            Tension = 0.06,
            Flow = 0.52,
            Pulse = 0.34,
            Focus = 0.46,
            Source = NIRAVisualIntentSource.Tool
        }.Normalize();
    }

    private static NIRAVisualIntent BuildAlert()
    {
        return new NIRAVisualIntent()
        {
            Description = "preset:alert",
            FormId = NIRAVisualFormIds.Orb,
            ExpressionId = NIRAVisualExpressionIds.None,
            ExpressionStrength = 0.0,
            Energy = 0.78,
            Cohesion = 0.88,
            Presence = 0.94,
            Scale = 1.05,
            Tension = 0.64,
            Flow = 0.86,
            Pulse = 0.56,
            Focus = 0.88,
            Source = NIRAVisualIntentSource.Tool
        }.Normalize();
    }

    private static NIRAVisualIntent BuildCalm()
    {
        return new NIRAVisualIntent()
        {
            Description = "preset:calm",
            FormId = NIRAVisualFormIds.Orb,
            ExpressionId = NIRAVisualExpressionIds.None,
            ExpressionStrength = 0.0,
            Energy = 0.14,
            Cohesion = 0.80,
            Presence = 0.42,
            Scale = 0.96,
            Tension = 0.03,
            Flow = 0.16,
            Pulse = 0.10,
            Focus = 0.34,
            Source = NIRAVisualIntentSource.Tool
        }.Normalize();
    }

    private static NIRAVisualIntent BuildCustom()
    {
        return new NIRAVisualIntent()
        {
            Description = "preset:custom",
            FormId = NIRAVisualFormIds.Orb,
            ExpressionId = NIRAVisualExpressionIds.IdentityMark,
            ExpressionStrength = 0.32,
            Energy = 0.54,
            Cohesion = 0.86,
            Presence = 0.88,
            Scale = 1.04,
            Tension = 0.18,
            Flow = 0.64,
            Pulse = 0.42,
            Focus = 0.74,
            Source = NIRAVisualIntentSource.Tool
        }.Normalize();
    }
}

