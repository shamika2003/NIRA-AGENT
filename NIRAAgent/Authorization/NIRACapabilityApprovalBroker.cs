using System.Collections.Concurrent;

namespace NIRAAgent.Authorization;

// Only the trusted host UI resolves these requests. This service is never
// registered as an LLM capability and does not interpret chat text as consent.
public sealed class NIRACapabilityApprovalBroker
{
    private sealed record Pending(NIRAApprovalRequest Request,
        TaskCompletionSource<NIRAApprovalResponse> Completion);
    private readonly ConcurrentDictionary<Guid, Pending> _pending = new();
    public event Action? Changed;
    public IReadOnlyList<NIRAApprovalRequest> PendingRequests =>
        _pending.Values.Select(x => x.Request).OrderBy(x => x.CreatedAtUtc).ToArray();

    public async Task<NIRAApprovalResponse> RequestAsync(
        NIRAAuthorityOperation operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_pending.Count >= 16)
            return new() { Reason = "Too many pending approvals. Review Permissions first." };
        NIRAApprovalRequest request = new() { Operation = operation };
        TaskCompletionSource<NIRAApprovalResponse> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[request.Id] = new(request, completion);
        RaiseChanged();
        try
        {
            return await completion.Task.WaitAsync(TimeSpan.FromMinutes(5), cancellationToken);
        }
        catch (TimeoutException)
        {
            return new() { Reason = "Approval expired after five minutes. No action was started." };
        }
        finally
        {
            _pending.TryRemove(request.Id, out _);
            RaiseChanged();
        }
    }

    public bool Resolve(Guid id, NIRAApprovalResponse response) =>
        _pending.TryGetValue(id, out Pending? pending) &&
        pending.Completion.TrySetResult(response);

    public void DenyAll()
    {
        foreach (Pending pending in _pending.Values)
            pending.Completion.TrySetResult(new() { Reason = "Dismissed by the user." });
    }

    private void RaiseChanged()
    {
        if (Changed == null) return;
        foreach (Action subscriber in Changed.GetInvocationList().Cast<Action>())
        {
            try { subscriber(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[AuthorizationUI] {ex.GetType().Name}"); }
        }
    }
}

