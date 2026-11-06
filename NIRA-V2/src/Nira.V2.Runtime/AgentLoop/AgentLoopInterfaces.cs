using Nira.V2.Contracts;

namespace Nira.V2.Runtime.AgentLoop;

// A reasoner chooses the next useful action, but it cannot create tools or grant permissions.
public interface IAgentReasoner
{
    Task<AgentDecision> DecideAsync(AgentRunSnapshot state,
        IReadOnlyList<AgentToolSpec> availableTools, CancellationToken cancellationToken);
}

public interface IAgentTool
{
    AgentToolSpec Spec { get; }
    Task<AgentToolReceipt> ExecuteAsync(string argumentReference,
        CancellationToken cancellationToken);
}

public interface IAgentPermissionGate
{
    Task<bool> IsAuthorizedAsync(Guid runId, AgentToolSpec tool,
        string argumentReference, CancellationToken cancellationToken);
}

public sealed class ObserveOnlyPermissionGate : IAgentPermissionGate
{
    public Task<bool> IsAuthorizedAsync(Guid runId, AgentToolSpec tool,
        string argumentReference, CancellationToken cancellationToken) =>
        Task.FromResult(tool.Access == AccessLevel.Observe);
}

public interface IAgentStateStore
{
    Task<AgentRunSnapshot?> LoadAsync(Guid id, CancellationToken cancellationToken);
    Task SaveAsync(AgentRunSnapshot state, CancellationToken cancellationToken);
    // Exclusive run-scoped ownership must span the entire agent loop, not just a JSON write.
    ValueTask<IAsyncDisposable> AcquireLeaseAsync(Guid id, CancellationToken cancellationToken);
}
