using System.Diagnostics;

namespace NIRAAgent.Mind;

/// <summary>
/// Bounded admission for independent background cognition. Events belonging to
/// the same persisted goal/commitment are serialized; unrelated work can run
/// concurrently. Temporal wakes have their own lane so a long branch analysis
/// cannot occupy every available admission slot.
/// </summary>
public sealed class NIRAInternalWorkGate
{
    internal const int MaximumOrdinaryRuns = 2;

    private readonly SemaphoreSlim _ordinarySlots =
        new(MaximumOrdinaryRuns, MaximumOrdinaryRuns);
    private readonly object _sync = new();
    private readonly Dictionary<string, OwnerGate> _owners = new(StringComparer.Ordinal);

    public static string OwnershipKey(NIRAMindEvent mindEvent)
    {
        ArgumentNullException.ThrowIfNull(mindEvent);
        if (mindEvent.Metadata.TryGetValue("goalId", out string? goal) &&
            Guid.TryParse(goal, out Guid goalId) && goalId != Guid.Empty)
            return "goal:" + goalId.ToString("D");
        if (mindEvent.Metadata.TryGetValue("commitmentId", out string? commitment) &&
            Guid.TryParse(commitment, out Guid commitmentId) && commitmentId != Guid.Empty)
            return "commitment:" + commitmentId.ToString("D");
        return "topic:" + (string.IsNullOrWhiteSpace(mindEvent.TopicKey)
            ? mindEvent.Id.ToString("D") : mindEvent.TopicKey);
    }

    public async Task<IDisposable> EnterAsync(
        NIRAMindEvent mindEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mindEvent);
        if (mindEvent.Source != NIRAMindEventSource.Internal)
            throw new ArgumentException("Internal admission requires an internal event.", nameof(mindEvent));

        bool urgentTemporal = mindEvent.Name == "TemporalCommitmentDue";
        string key = OwnershipKey(mindEvent);
        OwnerGate owner;
        lock (_sync)
        {
            if (!_owners.TryGetValue(key, out OwnerGate? existing))
            {
                owner = new OwnerGate();
                _owners.Add(key, owner);
            }
            else
            {
                owner = existing!;
            }
            owner.References++;
        }

        bool ordinaryAcquired = false;
        bool ownerAcquired = false;
        try
        {
            // Acquire the same-owner lock BEFORE an ordinary slot. Otherwise
            // queued siblings could consume every slot while waiting for one
            // active goal, starving independent work.
            await owner.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            ownerAcquired = true;
            if (!urgentTemporal)
            {
                await _ordinarySlots.WaitAsync(cancellationToken).ConfigureAwait(false);
                ordinaryAcquired = true;
            }
            Debug.WriteLine($"[InternalAdmission] ENTER | Event={mindEvent.Name} | TemporalLane={urgentTemporal}");
            return new Lease(this, key, owner, ordinaryAcquired);
        }
        catch
        {
            if (ordinaryAcquired) _ordinarySlots.Release();
            if (ownerAcquired) owner.Semaphore.Release();
            ReleaseReference(key, owner);
            throw;
        }
    }

    private void ReleaseReference(string key, OwnerGate owner)
    {
        lock (_sync)
        {
            owner.References--;
            if (owner.References == 0)
            {
                _owners.Remove(key);
                owner.Semaphore.Dispose();
            }
        }
    }

    private sealed class OwnerGate
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int References;
    }

    private sealed class Lease : IDisposable
    {
        private NIRAInternalWorkGate? _parent;
        private readonly string _key;
        private readonly OwnerGate _owner;
        private readonly bool _ordinary;

        public Lease(NIRAInternalWorkGate parent, string key, OwnerGate owner, bool ordinary)
        {
            _parent = parent;
            _key = key;
            _owner = owner;
            _ordinary = ordinary;
        }

        public void Dispose()
        {
            NIRAInternalWorkGate? parent = Interlocked.Exchange(ref _parent, null);
            if (parent == null) return;
            _owner.Semaphore.Release();
            if (_ordinary) parent._ordinarySlots.Release();
            parent.ReleaseReference(_key, _owner);
        }
    }
}
