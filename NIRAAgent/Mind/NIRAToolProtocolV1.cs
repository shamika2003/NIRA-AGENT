using System.Text.Json;
using System.Text.RegularExpressions;
using NIRAAgent.Capabilities;

namespace NIRAAgent.Mind;

// General purpose, transport-neutral shape shared by every registered capability.
// It never executes requests, confers authority, modifies existing handler payloads,
// or upgrades a model assertion into evidence.
internal static class NIRAToolProtocolV1
{
    private const string Version = "nira.tool.v1";

    internal static string RequestEnvelope(NIRACapabilityRequest request) =>
        JsonSerializer.Serialize(new
        {
            protocol = Version,
            kind = "capability.request",
            requestId = request.RequestId.ToString("D"),
            capabilityId = request.CapabilityId,
            arguments = request.Arguments.ValueKind == JsonValueKind.Object
                ? request.Arguments : JsonSerializer.SerializeToElement(new { }),
            reason = request.Reason
        });

    internal static string ResultEnvelope(NIRACapabilityResult result) =>
        JsonSerializer.Serialize(new
        {
            protocol = Version,
            kind = "capability.result",
            requestId = result.RequestId.ToString("D"),
            capabilityId = result.CapabilityId,
            status = result.Status.ToString(),
            success = result.Succeeded && !result.OutcomeUncertain,
            risk = result.Risk.ToString(),
            changedSystemState = result.ChangedSystemState,
            outcomeUncertain = result.OutcomeUncertain,
            auditRecorded = result.AuditRecorded,
            auditAttemptId = result.AuditAttemptId?.ToString("D"),
            exitCode = result.ExitCode,
            httpStatusCode = result.HttpStatusCode,
            result = new
            {
                summary = result.Summary,
                outputPresent = !string.IsNullOrWhiteSpace(result.Output),
                outputChars = result.Output?.Length ?? 0,
                visualArtifacts = result.VisualArtifacts?.Count ?? 0
            }
        });

    // A final reviewer may reject quality, but cannot erase a verified output.
    // Only pair a successful mutation and subsequent read of the EXACT same path.
    // Do not assert requirements, source validity or no-overwrite based on this.
    internal static string? VerifiedPartialArtifact(
        IReadOnlyCollection<NIRACapabilityResult> results, string? gap)
    {
        if (results is null || results.Count == 0) return null;
        var ordered = results.Where(r => r.Succeeded && !r.OutcomeUncertain)
            .OrderBy(r => r.FinishedAtUtc).ToArray();
        for (int i = ordered.Length - 1; i >= 0; i--)
        {
            var write = ordered[i];
            if (write.CapabilityId != NIRACapabilityIds.FileWrite ||
                !write.ChangedSystemState || write.Risk != NIRACapabilityRisk.Modify)
                continue;
            Match file = Regex.Match(write.Summary ?? string.Empty,
                @"^Wrote '([^'\r\n]+)' successfully\.", RegexOptions.CultureInvariant);
            if (!file.Success) continue;
            string path = file.Groups[1].Value;
            var read = ordered.FirstOrDefault(r =>
                r.CapabilityId == NIRACapabilityIds.FileRead &&
                r.Risk == NIRACapabilityRisk.Observe &&
                r.FinishedAtUtc >= write.FinishedAtUtc &&
                r.Summary.Contains("from '" + path + "'", StringComparison.OrdinalIgnoreCase) &&
                r.Summary.Contains("Truncated=False", StringComparison.OrdinalIgnoreCase));
            if (read is null) continue;
            string? sha = Regex.Match(write.Summary,
                @"\bSHA256=([A-Fa-f0-9]{64})\b", RegexOptions.CultureInvariant)
                is Match match && match.Success ? match.Groups[1].Value : null;
            // Gap is an independent reviewer assessment, not tool evidence.
            // Bound it and quote nothing claiming to be a new operation.
            string limitation = new string((gap ?? "").Where(c => !char.IsControl(c))
                .Take(400).ToArray()).Trim();
            if (limitation.Length == 0)
                limitation = "The full requested outcome did not pass independent quality review.";
            return "I saved a real file and successfully read it back, but the task is " +
                "only partially complete.\n\nSaved file: `" + path + "`\n" +
                (sha is null ? "" : "Saved SHA-256: `" + sha + "`\n") +
                "Verified: successful filesystem write and subsequent untruncated " +
                "read of the same path.\n" +
                "Still needs work: " + limitation + "\n" +
                "I did not verify a corrected revision, so I cannot claim full completion.";
        }
        return null;
    }
}
