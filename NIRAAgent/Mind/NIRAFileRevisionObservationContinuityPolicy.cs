using System.Text.Json;
using NIRAAgent.Capabilities;

namespace NIRAAgent.Mind;

/// <summary>
/// Invalidates only stale read-only observations of a successfully revised file.
/// The original evidence remains in the turn transcript; this clears only the
/// per-run de-duplication ledger, permitting a post-change verification read.
/// Never clears state-changing requests, even if a model repeats them.
/// </summary>
internal static class NIRAFileRevisionObservationContinuityPolicy
{
    internal static int InvalidateAfterRevision(
        NIRACapabilityRequest mutation,
        NIRACapabilityResult result,
        HashSet<string> executed,
        HashSet<string> duplicateReports,
        Dictionary<string, NIRACapabilityRequest> requests,
        Dictionary<string, NIRACapabilityResult> results)
    {
        if (mutation.CapabilityId is not (
                NIRACapabilityIds.FileRevisionApply or NIRACapabilityIds.FileRevisionRollback) ||
            result.CapabilityId != mutation.CapabilityId ||
            !result.Succeeded || !result.ChangedSystemState || result.OutcomeUncertain ||
            !TryPath(mutation, "destination", out string destination))
            return 0;

        string[] stale = requests
            .Where(pair => IsObservationOf(pair.Value, destination) &&
                results.TryGetValue(pair.Key, out NIRACapabilityResult? prior) &&
                prior.Risk == NIRACapabilityRisk.Observe)
            .Select(pair => pair.Key)
            .ToArray();

        foreach (string signature in stale)
        {
            executed.Remove(signature);
            duplicateReports.Remove(signature);
            requests.Remove(signature);
            results.Remove(signature);
        }
        return stale.Length;
    }

    private static bool IsObservationOf(NIRACapabilityRequest read, string destination) =>
        read.CapabilityId switch
        {
            NIRACapabilityIds.FileHash or NIRACapabilityIds.FileRead or
            NIRACapabilityIds.FileMetadata or NIRACapabilityIds.FileRevisionInspect =>
                Matches(read, "path", destination) ||
                Matches(read, "destination", destination),
            NIRACapabilityIds.FileCompare =>
                Matches(read, "source", destination) ||
                Matches(read, "destination", destination),
            _ => false
        };

    private static bool Matches(NIRACapabilityRequest request, string key, string expected) =>
        TryPath(request, key, out string path) &&
        string.Equals(path, expected, StringComparison.OrdinalIgnoreCase);

    private static bool TryPath(NIRACapabilityRequest request, string key, out string canonical)
    {
        canonical = string.Empty;
        if (request.Arguments.ValueKind != JsonValueKind.Object ||
            !request.Arguments.TryGetProperty(key, out JsonElement value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            return false;
        try
        {
            canonical = Path.GetFullPath(value.GetString()!);
            return Path.IsPathFullyQualified(canonical);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
