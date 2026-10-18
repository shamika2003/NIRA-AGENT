using System.Text.Json;
using NIRAAgent.Capabilities;

namespace NIRAAgent.Mind;

// Run-local observation continuity: a failed specifically named HWND does not
// make an unrelated foreground screen a valid replacement. No task-text or
// application-name matching is used.
internal static class NIRAWindowObservationContinuityPolicy
{
    public static bool IsNamedWindowCapture(NIRACapabilityRequest request)
    {
        if (request.CapabilityId != NIRACapabilityIds.VisionCapture ||
            request.Arguments.ValueKind != JsonValueKind.Object ||
            !TryString(request.Arguments, "target", out string target) ||
            !target.Trim().Equals("window", StringComparison.OrdinalIgnoreCase))
            return false;
        return HasSelector(request.Arguments, "windowHandle") ||
            HasSelector(request.Arguments, "processName") ||
            HasSelector(request.Arguments, "titleContains");
    }

    public static bool IsUnrelatedForegroundCapture(
        NIRACapabilityRequest request, bool namedTargetStillUnresolved)
    {
        if (!namedTargetStillUnresolved ||
            request.CapabilityId != NIRACapabilityIds.VisionCapture ||
            request.Arguments.ValueKind != JsonValueKind.Object)
            return false;
        if (HasSelector(request.Arguments, "windowHandle") ||
            HasSelector(request.Arguments, "processName") ||
            HasSelector(request.Arguments, "titleContains"))
            return false;
        return !TryString(request.Arguments, "target", out string target) ||
            target.Equals("foregroundWindow", StringComparison.OrdinalIgnoreCase) ||
            target.Equals("foreground", StringComparison.OrdinalIgnoreCase) ||
            target.Equals("lastExternalWindow", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasSelector(JsonElement args, string name) =>
        TryString(args, name, out string value) && !string.IsNullOrWhiteSpace(value);

    private static bool TryString(JsonElement args, string name, out string value)
    {
        value = string.Empty;
        foreach (JsonProperty p in args.EnumerateObject())
            if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                p.Value.ValueKind == JsonValueKind.String)
            {
                value = p.Value.GetString() ?? string.Empty;
                return true;
            }
        return false;
    }
}
