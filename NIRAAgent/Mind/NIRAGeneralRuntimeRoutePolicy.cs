using NIRAAgent.Mind;

namespace NIRAAgent.Mind;

/// <summary>
/// One host-owned user-message entry for the existing NIRA WPF application.
/// Only selects an execution runtime; never infers a tool or authorizes an action.
/// The legacy NIRAV2CanaryRoutePolicy remains the single validation boundary for
/// supported user-turn data. No second assistant, persona, or memory is created.
/// </summary>
public static class NIRAGeneralRuntimeRoutePolicy
{
    // Exact host controls. Environment values are read by the WPF host, not by the model.
    public const string DefaultReadOnlyValue = "V2_DEFAULT_READONLY";
    public const string LegacyOnlyValue = "LEGACY_ONLY";

    public static bool TryGetRequest(NIRAMindEvent? mindEvent,
        string? canaryConsent, string? runtimeMode, out string request)
    {
        request = string.Empty;
        // LEGACY_ONLY is an explicit kill switch even if a canary flag is also set.
        if (string.Equals(runtimeMode, LegacyOnlyValue, StringComparison.Ordinal))
            return false;

        // Existing explicit /v2 opt-in remains backward compatible.
        if (string.IsNullOrEmpty(runtimeMode))
            return NIRAV2CanaryRoutePolicy.TryGetRequest(mindEvent, canaryConsent,
                out request);

        // Unknown or case-mismatched host modes cannot enable V2.
        if (!string.Equals(runtimeMode, DefaultReadOnlyValue, StringComparison.Ordinal) ||
            mindEvent is null)
            return false;

        // Route normal user text through the IDENTICAL validated canary constraints.
        // A host-only virtual prefix is not saved to conversation or shown to the model.
        // Original /v2 text is accepted during the transition, but no longer required.
        NIRAMindEvent candidate = mindEvent.Content.StartsWith(
            NIRAV2CanaryRoutePolicy.Prefix, StringComparison.Ordinal)
            ? mindEvent
            : mindEvent with { Content = NIRAV2CanaryRoutePolicy.Prefix + mindEvent.Content };
        return NIRAV2CanaryRoutePolicy.TryGetRequest(candidate,
            NIRAV2CanaryRoutePolicy.EnableValue, out request);
    }
}
