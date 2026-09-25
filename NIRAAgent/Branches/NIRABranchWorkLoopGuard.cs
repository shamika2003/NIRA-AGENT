using System;
using System.Collections.Generic;
using System.Linq;

namespace NIRAAgent.Branches;

// Shared, domain-neutral guard. A new model invocation, branch, or process
// does not reset the underlying persisted work ledger. Do NOT count a tool's
// mechanical success as proof that the user's objective is complete.
internal static class NIRABranchWorkLoopGuard
{
    public static string? Evaluate(IEnumerable<NIRABranchWorkItem> goalWork)
    {
        // Only terminal work counts. A user could legitimately have a worker
        // running or waiting for consent; never count waiting as failure.
        NIRABranchWorkItem[] last = goalWork
            .Where(w => w.IsTerminal && w.Status != NIRABranchWorkStatus.Cancelled)
            .OrderBy(w => w.FinishedAtUtc ?? w.CreatedAtUtc)
            .ThenBy(w => w.CreatedAtUtc)
            .TakeLast(12)
            .ToArray();
        if (last.Length == 0) return null;

        static bool Failed(NIRABranchWorkItem w) => w.Status is
            NIRABranchWorkStatus.Failed or NIRABranchWorkStatus.Rejected;

        static string Family(NIRABranchWorkItem w) => w.Kind switch
        {
            NIRABranchWorkKind.Capability =>
                "capability:" + (w.CapabilityRequest?.CapabilityId ?? "unknown"),
            NIRABranchWorkKind.DynamicTool =>
                "dynamic-tool:" + (w.DynamicToolInvocation?.ToolId ?? "unknown"),
            _ => "unknown"
        };

        NIRABranchWorkItem[] recent = last.TakeLast(8).ToArray();
        NIRABranchWorkItem newest = last[^1];
        // A cross-primitive failure tail is terminal even if its latest item
        // also belongs to a failed primitive family. Test this FIRST so a
        // family-local circuit cannot indefinitely mask the global budget.
        if (Failed(newest) && recent.Length >= 8 && recent.Count(Failed) >= 6)
            return "At least six of the last eight attempted steps failed. " +
                "Report the concrete blocker or ask for a different direction.";

        // Three failures of the same primitive within the latest work window
        // are enough to stop repeating it, including across different branch
        // IDs or changing request arguments. A different successful step can
        // still move the task forward, so this checks a recent failure tail.
        if (Failed(newest))
        {
            string family = Family(newest);
            if (recent.Count(w => Failed(w) && Family(w) == family) >= 3 &&
                !recent.TakeLast(4).Any(w => w.Succeeded && Family(w) == family))
                return "The same operation failed repeatedly (" + family +
                    "). Inspect the last failure and change the plan rather than retrying it.";
        }

        // Cross-capability browser observation loop: navigate/follow/inspect
        // can alternate while still returning the SAME page and content hash,
        // which defeats exact request-signature guards and burns one cognition
        // call per no-op observation. Reset this tail on any non-observation
        // operation so real clicks/fills/downloads remain fully available.
        static bool BrowserObservation(NIRABranchWorkItem w)
        {
            string? id = w.CapabilityRequest?.CapabilityId;
            return w.Succeeded && id is
                "browser.inspect" or
                "browser.navigate" or
                "browser.follow" or
                "browser.current";
        }

        static string? ObservationField(NIRABranchWorkItem w, string key)
        {
            string? line = w.ResultEvidence?
                .Split('\n')
                .Select(x => x.Trim())
                .FirstOrDefault(x => x.StartsWith(key + "=", StringComparison.Ordinal));
            return line?[(key.Length + 1)..].Trim();
        }

        NIRABranchWorkItem[] observationTail = last
            .Reverse()
            .TakeWhile(BrowserObservation)
            .Take(6)
            .ToArray();

        static string? ObservationSignature(NIRABranchWorkItem w)
        {
            string? url =
                ObservationField(w, "Url") ??
                ObservationField(w, "FinalUrl");
            string? hash = ObservationField(w, "ContentSha256");
            return string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(hash)
                ? null
                : url.Trim() + "|" + hash.Trim();
        }

        // Detect A->B->A->B observation ping-pong before either page has to be
        // revisited three times. This is common when cognition alternates
        // between an instructions page and an adjacent generic download page
        // even though neither state is changing. A genuine click/fill/download
        // breaks BrowserObservation() and therefore resets this tail.
        if (observationTail.Length >= 4)
        {
            string? a0 = ObservationSignature(observationTail[0]);
            string? b0 = ObservationSignature(observationTail[1]);
            string? a1 = ObservationSignature(observationTail[2]);
            string? b1 = ObservationSignature(observationTail[3]);
            if (a0 != null && b0 != null &&
                string.Equals(a0, a1, StringComparison.Ordinal) &&
                string.Equals(b0, b1, StringComparison.Ordinal) &&
                !string.Equals(a0, b0, StringComparison.Ordinal))
            {
                return "Read-only browser work is alternating between the same two " +
                    "unchanged page states. Reuse the accumulated evidence or choose " +
                    "a materially different grounded action instead of revisiting them.";
            }
        }

        if (observationTail.Length >= 3)
        {
            string? currentUrl =
                ObservationField(observationTail[0], "Url") ??
                ObservationField(observationTail[0], "FinalUrl");
            string? currentHash =
                ObservationField(observationTail[0], "ContentSha256");
            if (!string.IsNullOrWhiteSpace(currentUrl) &&
                !string.IsNullOrWhiteSpace(currentHash) &&
                observationTail.Count(w =>
                    (ObservationField(w, "Url") ??
                     ObservationField(w, "FinalUrl")) == currentUrl &&
                    ObservationField(w, "ContentSha256") == currentHash) >= 3)
            {
                return "Repeated read-only browser observations reached the same " +
                    "page URL and content hash at least three times without an " +
                    "intervening browser action. Reason from the existing evidence " +
                    "or choose a materially different grounded action.";
            }
        }

        // Browser snapshots have fresh InspectionIds and element refs on
        // every inspect, so byte-for-byte evidence comparison misses a real
        // unchanged-page loop. Compare the page content hash + URL instead.
        // This is task/goal-local and requires consecutive inspections with
        // no intervening action. It never assumes that a successful inspect
        // proves login, navigation or the user's original objective.
        NIRABranchWorkItem[] inspectionTail = last
            .Reverse()
            .TakeWhile(w => w.Succeeded &&
                w.CapabilityRequest?.CapabilityId == "browser.inspect")
            .Take(4)
            .ToArray();
        if (inspectionTail.Length >= 4)
        {
            static string? Field(NIRABranchWorkItem w, string key)
            {
                string? line = w.ResultEvidence?
                    .Split('\n')
                    .Select(x => x.Trim())
                    .FirstOrDefault(x => x.StartsWith(key + "=", StringComparison.Ordinal));
                return line?[(key.Length + 1)..].Trim();
            }
            string? currentHash = Field(inspectionTail[0], "ContentSha256");
            string? currentUrl = Field(inspectionTail[0], "Url");
            if (!string.IsNullOrWhiteSpace(currentHash) &&
                !string.IsNullOrWhiteSpace(currentUrl) &&
                inspectionTail.All(w =>
                    Field(w, "ContentSha256") == currentHash &&
                    Field(w, "Url") == currentUrl))
                return "Four consecutive browser inspections saw the same page URL " +
                    "and content hash without an intervening action. Use the " +
                    "latest grounded links/controls or report why you cannot proceed; " +
                    "more inspection is not progress.";
        }

        // Prevent 'success' loops where an inspection/search/tool returns the
        // identical evidence without advancing the world. Compare the entire
        // already persisted result (no new secrets or data are recorded).
        if (newest.Succeeded &&
            !string.IsNullOrWhiteSpace(newest.ResultEvidence))
        {
            string family = Family(newest);
            if (last.Count(w => w.Succeeded && Family(w) == family &&
                string.Equals(w.ResultEvidence, newest.ResultEvidence,
                    StringComparison.Ordinal) &&
                string.Equals(w.ResultSummary, newest.ResultSummary,
                    StringComparison.Ordinal)) >= 5)
                return "The same operation returned identical evidence five times " +
                    "without an independently verified result. Stop reconsidering it.";
        }

        return null;
    }
}
