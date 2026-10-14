using System.Diagnostics;

namespace NIRAAgent.Mind;

/// <summary>
/// Runtime-owned, bounded lifecycle of overlapping user reasoning runs.
/// This is observation only: it neither approves capability use nor claims an
/// Executive objective succeeded. Each request has a stable identity.
/// </summary>
public sealed class NIRAActiveTurnLedger
{
    private const int MaximumRecentRuns = 24;
    private readonly object _sync = new();
    private readonly Dictionary<Guid, Turn> _active = new();
    private readonly Queue<Turn> _recent = new();

    public Lease Enter(string request)
    {
        Guid id = Guid.NewGuid();
        string objective = (request ?? string.Empty)
            .Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (objective.Length > 180) objective = objective[..180] + "…";
        lock (_sync)
            _active.Add(id, new Turn(id, objective, DateTimeOffset.UtcNow,
                NIRAActiveTurnState.Running, false));
        return new Lease(this, id);
    }

    // Older S1 callers and tests still use Snapshot(). The captured identities
    // separately bind any later delivery correction to the exact observed work.
    public string Snapshot() => Observe().Context;

    public NIRAActiveWorkObservation Observe()
    {
        lock (_sync)
        {
            // The oldest outstanding run is the primary work already in
            // progress when follow-up check-ins arrive. Keep other runs in
            // context, but only attach the status-correction identity to that
            // original work, not to incidental chat replies also in flight.
            Turn[] active = _active.Values.OrderBy(x => x.StartedAtUtc)
                .Take(3).ToArray();
            if (active.Length == 0)
                return new NIRAActiveWorkObservation(string.Empty, string.Empty);
            string context = "OTHER ACTIVE USER WORK (authoritative in-flight status, not completed):\n" +
                string.Join("\n", active.Select(x => "- " + x.Objective)) +
                "\nThese are ongoing, independent runs. Do not claim progress, completion or tool results not yet observed.";
            return new NIRAActiveWorkObservation(context,
                active[0].Id.ToString("D"));
        }
    }

    /// <summary>
    /// A source-grounded supplement produced only when a run *actually* ended
    /// after being shown to another conversation. No lexical user-message or
    /// model-output classifier, and no extra LLM call.
    /// </summary>
    public string DeliveryUpdate(string? observedIds)
    {
        if (string.IsNullOrWhiteSpace(observedIds)) return string.Empty;
        List<string> notices = new();
        lock (_sync)
        {
            foreach (string token in observedIds.Split(';', StringSplitOptions.RemoveEmptyEntries).Take(3))
            {
                if (!Guid.TryParseExact(token, "D", out Guid id)) continue;
                Turn? finished = _recent.FirstOrDefault(x => x.Id == id);
                if (finished is null) continue; // Still running or no retained evidence.
                string label = finished.Objective.Length > 90
                    ? finished.Objective[..90].TrimEnd() + "…"
                    : finished.Objective;
                string message = finished.State == NIRAActiveTurnState.Finished
                    ? finished.ResultPublished
                        ? "finished processing, and its response is already in this conversation"
                        : "finished processing, but no answer was published"
                    : "stopped before finishing";
                notices.Add($"The earlier task ({label}) {message} while I was replying.");
            }
        }
        return notices.Count == 0 ? string.Empty :
            "Latest work status: " + string.Join(" ", notices);
    }

    // Fresh runtime evidence for a new turn that arrives just AFTER work ended.
    // Conversation history alone may still describe that work as running. This
    // is a factual lifecycle note, not a claim of successful objective completion.
    public string RecentOutcomeContext()
    {
        lock (_sync)
        {
            Turn? recent = _recent.LastOrDefault();
            if (recent is null || recent.EndedAtUtc is null ||
                DateTimeOffset.UtcNow - recent.EndedAtUtc.Value > TimeSpan.FromMinutes(10))
                return string.Empty;
            return "CURRENT FOREGROUND USER WORK: none active in this runtime " +
                "snapshot. This says nothing about separate branch/goal work. " +
                "RECENT AUTHORITATIVE USER WORK STATE (not currently active): " +
                recent.Objective + " — " +
                (recent.State == NIRAActiveTurnState.Stopped
                    ? "Stopped before processing finished."
                    : "Processing ended; check the conversation result " +
                      "to determine whether the requested work completed or was blocked.") +
                " Do not claim it is still running without a new active-work record.";
        }
    }

    private void Close(Guid id, NIRAActiveTurnState state, bool published)
    {
        lock (_sync)
        {
            if (!_active.Remove(id, out Turn? turn)) return;
            _recent.Enqueue(turn with { State = state, ResultPublished = published,
                EndedAtUtc = DateTimeOffset.UtcNow });
            while (_recent.Count > MaximumRecentRuns) _recent.Dequeue();
        }
        Debug.WriteLine($"[ConcurrentMind] TASK_STATE | Task={id:D} | State={state} | Published={published}");
    }

    private sealed record Turn(Guid Id, string Objective, DateTimeOffset StartedAtUtc,
        NIRAActiveTurnState State, bool ResultPublished, DateTimeOffset? EndedAtUtc = null);

    public sealed class Lease : IDisposable
    {
        private NIRAActiveTurnLedger? _owner;
        private readonly Guid _id;
        internal Lease(NIRAActiveTurnLedger owner, Guid id)
        {
            _owner = owner;
            _id = id;
        }

        public void Finish(bool resultPublished)
        {
            NIRAActiveTurnLedger? owner = Interlocked.Exchange(ref _owner, null);
            owner?.Close(_id, NIRAActiveTurnState.Finished, resultPublished);
        }

        public void Dispose()
        {
            NIRAActiveTurnLedger? owner = Interlocked.Exchange(ref _owner, null);
            owner?.Close(_id, NIRAActiveTurnState.Stopped, false);
        }
    }
}

public enum NIRAActiveTurnState { Running, Finished, Stopped }
public sealed record NIRAActiveWorkObservation(string Context, string ObservedIds);
