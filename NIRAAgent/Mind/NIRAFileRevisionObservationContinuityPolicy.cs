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

    // R43D.2: a failed inspect/hash of a NEW DOCX destination, observed BEFORE
    // its verified creation, is not a post-write inspection. Once the audited
    // DOCX writer has safely published that exact new path, allow that failed
    // read to be reissued. This does not permit another write, remove a
    // successful read, or alter any different file's observation cache.
    internal static int InvalidatePrematureDocxDestinationReads(
        NIRACapabilityRequest mutation,
        NIRACapabilityResult result,
        HashSet<string> executed,
        HashSet<string> duplicateReports,
        Dictionary<string, NIRACapabilityRequest> requests,
        Dictionary<string, NIRACapabilityResult> results)
    {
        if (mutation.CapabilityId != NIRACapabilityIds.DocxPageBreakInsert ||
            result.CapabilityId != mutation.CapabilityId ||
            !result.Succeeded || result.OutcomeUncertain ||
            !result.ChangedSystemState || !result.AuditRecorded ||
            result.Risk != NIRACapabilityRisk.Modify ||
            !TryPath(mutation, "destination", out string destination) ||
            !destination.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            return 0;

        // The verified write receipt, not the LLM request, proves a NEW
        // file was published and that the original remained unchanged.
        string output = result.Output ?? string.Empty;
        if (!HasExactReceiptLine(output, "Destination", destination) ||
            !HasExactReceiptLine(output, "PostWriteVerified", "True") ||
            !HasExactReceiptLine(output, "SourceUnchanged", "True") ||
            !HasExactReceiptLine(output, "OverwroteExisting", "False"))
            return 0;

        string[] premature = requests
            .Where(pair =>
                (pair.Value.CapabilityId is NIRACapabilityIds.DocxInspect or NIRACapabilityIds.FileHash) &&
                Matches(pair.Value, "path", destination) &&
                results.TryGetValue(pair.Key, out var earlier) &&
                earlier.CapabilityId == pair.Value.CapabilityId &&
                earlier.Risk == NIRACapabilityRisk.Observe &&
                earlier.Status == NIRACapabilityResultStatus.Failed &&
                !earlier.ChangedSystemState && !earlier.OutcomeUncertain)
            .Select(pair => pair.Key).ToArray();
        foreach (string signature in premature)
        {
            executed.Remove(signature);
            duplicateReports.Remove(signature);
            requests.Remove(signature);
            results.Remove(signature);
        }
        return premature.Length;
    }

    private static bool HasExactReceiptLine(string receipt, string key, string value) =>
        receipt.Split('\n').Any(line => string.Equals(line.TrimEnd('\r'),
            key + "=" + value, StringComparison.OrdinalIgnoreCase));

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
