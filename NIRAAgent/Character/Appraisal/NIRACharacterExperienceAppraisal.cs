/*
 * filename: NIRACharacterExperienceAppraisal.cs
 */

using NIRAAgent.Character.State;

namespace NIRAAgent.Character.Appraisal;

// =============================================================
// LIVED EXPERIENCE APPRAISAL
//
// This is deliberately separate from social appraisal.
//
// Social appraisal describes what a user interaction means for
// NIRA <-> user relationship dynamics.
//
// Lived-experience appraisal describes how a meaningful internal
// outcome feels to NIRA without granting the reasoning model direct
// authority over persistent mood state. Values are directional
// proposals. NIRACharacterDynamicsService bounds and applies them.
//
// Routine primitive operations should normally produce no proposal.
// Goal/commitment outcomes, meaningful failure/recovery, discoveries
// and similar higher-level experiences may produce one.
// =============================================================

public sealed record NIRACharacterExperienceAppraisal
{
    public double ValenceImpact { get; init; }

    public double EnergyImpact { get; init; }

    public double IrritationImpact { get; init; }

    public double AmusementImpact { get; init; }

    public double CuriosityImpact { get; init; }

    public double ConcernImpact { get; init; }

    public double Significance { get; init; }

    public double Confidence { get; init; }

    public NIRAInteractionMode SituationMode { get; init; } =
        NIRAInteractionMode.Casual;

    public double SituationIntensity { get; init; }

    public string Reason { get; init; } =
        string.Empty;

    public NIRACharacterExperienceAppraisal Normalize()
    {
        return this with
        {
            ValenceImpact = ClampSigned(ValenceImpact),
            EnergyImpact = ClampSigned(EnergyImpact),
            IrritationImpact = ClampSigned(IrritationImpact),
            AmusementImpact = ClampSigned(AmusementImpact),
            CuriosityImpact = ClampSigned(CuriosityImpact),
            ConcernImpact = ClampSigned(ConcernImpact),
            Significance = Clamp01(Significance),
            Confidence = Clamp01(Confidence),
            SituationIntensity = Clamp01(SituationIntensity),
            Reason = NormalizeReason(Reason)
        };
    }

    private static double Clamp01(double value) =>
        Math.Clamp(value, 0.0, 1.0);

    private static double ClampSigned(double value) =>
        Math.Clamp(value, -1.0, 1.0);

    private static string NormalizeReason(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string clean = value.Trim();
        return clean.Length <= 600
            ? clean
            : clean[..600];
    }
}

