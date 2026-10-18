using NIRAAgent.Capabilities;

namespace NIRAAgent.Mind;

/// <summary>
/// Per-user-run evidence ledger for irreversible process termination requests.
/// Do not interpret an unsuccessful kill as proof of current process liveness,
/// or repeatedly kill the same PID after its recorded outcome.
/// </summary>
internal sealed class NIRAProcessStopContinuityGuard
{
    private readonly Dictionary<int, (NIRACapabilityResultStatus Status, string Evidence)> _outcomes = new();

    internal static int? TargetPid(NIRACapabilityRequest request)
    {
        if (request.CapabilityId != NIRACapabilityIds.ProcessStop ||
            request.Arguments.ValueKind != System.Text.Json.JsonValueKind.Object ||
            !request.Arguments.TryGetProperty("pid", out var value) ||
            value.ValueKind != System.Text.Json.JsonValueKind.Number ||
            !value.TryGetInt32(out int pid) || pid <= 0) return null;
        return pid;
    }

    internal bool TryBlock(NIRACapabilityRequest request, out string evidence)
    {
        evidence = string.Empty;
        if (TargetPid(request) is not int pid || !_outcomes.TryGetValue(pid, out var previous))
            return false;
        evidence = previous.Status == NIRACapabilityResultStatus.Succeeded
            ? $"PROCESS STOP ALREADY VERIFIED FOR PID={pid}: {previous.Evidence} " +
              "Do not repeat process.stop. If the objective is to close every " +
              "instance, obtain a fresh process.list and inspect other PIDs."
            : $"PROCESS STOP DID NOT VERIFY PID={pid}: {previous.Evidence} " +
              "Do not repeat the same destructive attempt in this run. " +
              "A previous process.list is stale: request a FRESH process.list " +
              "to establish whether that process is CURRENTLY running. " +
              "A stop failure does not prove the window is still open, " +
              "and process absence does not prove NIRA caused the exit.";
        return true;
    }

    internal void Record(NIRACapabilityRequest request, NIRACapabilityResult result)
    {
        if (TargetPid(request) is not int pid || result.CapabilityId != NIRACapabilityIds.ProcessStop)
            return;
        _outcomes[pid] = (result.Status, result.Summary);
    }

    // The same process-list query cannot be treated as current after a stop
    // attempt, even if the stop returned denied/uncertain. The next request
    // must execute a NEW read; never reuse the old cached process inventory.
    internal static int InvalidatePriorProcessLists(
        HashSet<string> executed,
        HashSet<string> duplicateReports,
        Dictionary<string, NIRACapabilityRequest> requests,
        Dictionary<string, NIRACapabilityResult> results)
    {
        string[] stale = requests
            .Where(pair => IsProcessObservation(pair.Value))
            .Select(pair => pair.Key).ToArray();
        foreach (string signature in stale)
        {
            executed.Remove(signature);
            duplicateReports.Remove(signature);
            requests.Remove(signature);
            results.Remove(signature);
        }
        return stale.Length;
    }

    internal static bool IsProcessObservation(NIRACapabilityRequest request) =>
        request.CapabilityId == NIRACapabilityIds.ProcessList;
}
