using Nira.V2.Contracts;

namespace Nira.V2.Runtime.AgentLoop;

// Bounded observe/decide/act loop. Checkpoint at every decision and tool boundary.
// Never blindly replay an action whose external outcome is uncertain.
public sealed class AgentLoopEngine
{
    private readonly IAgentReasoner _reasoner;
    private readonly IAgentStateStore _store;
    private readonly IAgentPermissionGate _permissions;
    private readonly IReadOnlyDictionary<string, IAgentTool> _tools;
    private readonly AgentLoopLimits _limits;
    private readonly IProgress<AgentActivityEvent>? _progress;

    public AgentLoopEngine(IAgentReasoner reasoner, IAgentStateStore store,
        IAgentPermissionGate permissions, IEnumerable<IAgentTool> tools,
        AgentLoopLimits? limits = null, IProgress<AgentActivityEvent>? progress = null)
    {
        _reasoner = reasoner ?? throw new ArgumentNullException(nameof(reasoner));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _tools = tools.ToDictionary(t => t.Spec.Id, StringComparer.Ordinal);
        _limits = limits ?? new AgentLoopLimits();
        _limits.Validate();
        _progress = progress;
    }

    public async Task<AgentRunSnapshot> StartAsync(string userRequest,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userRequest);
        var state = new AgentRunSnapshot { Id = Guid.NewGuid(), UserRequest = userRequest };
        await using var lease = await _store.AcquireLeaseAsync(state.Id, ct);
        await LogAsync(state, "RUN_CREATED", null, null, ct);
        return await ExecuteAsync(state, ct);
    }

    public async Task<AgentRunSnapshot> ResumeAsync(Guid id, CancellationToken ct = default)
    {
        await using var lease = await _store.AcquireLeaseAsync(id, ct);
        AgentRunSnapshot state = await _store.LoadAsync(id, ct)
            ?? throw new KeyNotFoundException("Agent checkpoint not found.");
        if (state.Status is AgentRunStatus.Completed or AgentRunStatus.NeedsReconciliation or
            AgentRunStatus.Cancelled) return state;
        if (state.PendingTool is { Stage: PendingToolStage.Dispatched, Access: not AccessLevel.Observe })
        {
            state.Status = AgentRunStatus.NeedsReconciliation;
            state.LastErrorCode = "UNCERTAIN_MUTATION_AFTER_RESTART";
            await LogAsync(state, "RECONCILIATION_REQUIRED", state.PendingTool.ToolId,
                state.LastErrorCode, CancellationToken.None);
            return state;
        }
        if (state.Status == AgentRunStatus.Incomplete) return state;
        return await ExecuteAsync(state, ct);
    }

    private async Task<AgentRunSnapshot> ExecuteAsync(AgentRunSnapshot state, CancellationToken ct)
    {
        state.Status = AgentRunStatus.Running;
        await LogAsync(state, "RUN_ACTIVE", null, null, CancellationToken.None);
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (state.PendingTool is not null)
                {
                    if (await ProcessPendingToolAsync(state, ct)) return state;
                    continue;
                }
                if (state.DecisionCount >= _limits.MaxDecisions)
                    return await EndIncompleteAsync(state, "DECISION_BUDGET_EXHAUSTED");

                state.DecisionCount++;
                await LogAsync(state, "REASONING_STARTED", null, null, ct);
                AgentDecision choice = await _reasoner.DecideAsync(state,
                    _tools.Values.Select(t => t.Spec).ToArray(), ct);
                switch (choice.Kind)
                {
                    case AgentDecisionKind.Answer:
                        if (string.IsNullOrWhiteSpace(choice.Answer))
                            return await EndIncompleteAsync(state, "EMPTY_ANSWER");
                        state.FinalAnswer = choice.Answer.Trim();
                        state.Status = AgentRunStatus.Completed;
                        await LogAsync(state, "RUN_COMPLETED", null, null, CancellationToken.None);
                        return state;

                    case AgentDecisionKind.CannotComplete:
                        return await EndIncompleteAsync(state,
                            string.IsNullOrWhiteSpace(choice.ReasonCode) ? "MODEL_CANNOT_COMPLETE" : choice.ReasonCode);

                    case AgentDecisionKind.InvokeTool:
                        if (state.ToolCallCount >= _limits.MaxTools)
                            return await EndIncompleteAsync(state, "TOOL_BUDGET_EXHAUSTED");
                        if (string.IsNullOrWhiteSpace(choice.ToolId) ||
                            !_tools.TryGetValue(choice.ToolId, out IAgentTool? selected))
                            return await EndIncompleteAsync(state, "UNKNOWN_TOOL");
                        if (string.IsNullOrWhiteSpace(choice.ToolArgumentReference))
                            return await EndIncompleteAsync(state, "MISSING_TOOL_ARGUMENT_REFERENCE");
                        if (selected.Spec.Access != AccessLevel.Observe &&
                            string.IsNullOrWhiteSpace(choice.IdempotencyKey))
                            return await EndIncompleteAsync(state, "MISSING_IDEMPOTENCY_KEY");
                        state.PendingTool = new PendingAgentTool
                        {
                            ToolId = selected.Spec.Id,
                            Access = selected.Spec.Access,
                            ArgumentReference = choice.ToolArgumentReference,
                            IdempotencyKey = choice.IdempotencyKey
                        };
                        await LogAsync(state, "TOOL_PROPOSED", selected.Spec.Id, null, ct);
                        break;

                    default:
                        return await EndIncompleteAsync(state, "UNKNOWN_DECISION");
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (state.PendingTool is { Stage: PendingToolStage.Dispatched,
                    Access: not AccessLevel.Observe })
            {
                state.Status = AgentRunStatus.NeedsReconciliation;
                state.LastErrorCode = "MUTATION_INTERRUPTED";
                await LogAsync(state, "RECONCILIATION_REQUIRED", state.PendingTool.ToolId,
                    state.LastErrorCode, CancellationToken.None);
            }
            else
            {
                state.Status = AgentRunStatus.Cancelled;
                state.LastErrorCode = "CANCELLED";
                await LogAsync(state, "RUN_CANCELLED", null, "CANCELLED", CancellationToken.None);
            }
            return state;
        }
        catch (Exception)
        {
            if (state.PendingTool is { Stage: PendingToolStage.Dispatched,
                    Access: not AccessLevel.Observe })
            {
                state.Status = AgentRunStatus.NeedsReconciliation;
                state.LastErrorCode = "MUTATION_RESULT_UNKNOWN";
                await LogAsync(state, "RECONCILIATION_REQUIRED", state.PendingTool.ToolId,
                    state.LastErrorCode, CancellationToken.None);
                return state;
            }
            return await EndIncompleteAsync(state, "AGENT_EXECUTION_FAILED");
        }
    }

    // Returns true when the run must pause/stop, false when reasoner can proceed.
    private async Task<bool> ProcessPendingToolAsync(AgentRunSnapshot state, CancellationToken ct)
    {
        PendingAgentTool pending = state.PendingTool!;
        if (!_tools.TryGetValue(pending.ToolId, out IAgentTool? tool) ||
            tool.Spec.Access != pending.Access)
        {
            await EndIncompleteAsync(state, "TOOL_REGISTRY_CHANGED");
            return true;
        }
        if (pending.Stage == PendingToolStage.Proposed)
        {
            if (!await _permissions.IsAuthorizedAsync(state.Id, tool.Spec,
                    pending.ArgumentReference, ct))
            {
                state.Status = AgentRunStatus.AwaitingAuthorization;
                await LogAsync(state, "AUTHORIZATION_REQUIRED", pending.ToolId, null, ct);
                return true;
            }
            pending.Stage = PendingToolStage.Dispatched;
            state.ToolCallCount++;
            await LogAsync(state, "TOOL_DISPATCHED", pending.ToolId, null, ct);
        }
        // On restart read-only dispatched steps can be re-run. Mutations are stopped in ResumeAsync.
        try
        {
            AgentToolReceipt receipt = await tool.ExecuteAsync(pending.ArgumentReference, ct);
            if (pending.Access != AccessLevel.Observe &&
                !receipt.Succeeded && receipt.OutcomeConfirmed)
            {
                // The authoritative executor proved that NO mutation occurred.
                // Never replay an attempted write, but do not falsely request
                // reconciliation of a change that definitively did not happen.
                state.Observations.Add(new AgentObservation(pending.ToolId,
                    null, false, receipt.OutcomeCode, DateTimeOffset.UtcNow));
                state.PendingTool = null;
                state.Status = AgentRunStatus.Incomplete;
                state.LastErrorCode = receipt.OutcomeCode;
                await LogAsync(state, "TOOL_CONFIRMED_NO_EFFECT", pending.ToolId,
                    receipt.OutcomeCode, CancellationToken.None);
                return true;
            }
            if (pending.Access != AccessLevel.Observe &&
                (!receipt.Succeeded || !receipt.OutcomeConfirmed))
            {
                state.Status = AgentRunStatus.NeedsReconciliation;
                state.LastErrorCode = "UNCERTAIN_MUTATION_OUTCOME";
                await LogAsync(state, "RECONCILIATION_REQUIRED", pending.ToolId,
                    receipt.OutcomeCode, CancellationToken.None);
                return true;
            }
            state.Observations.Add(new AgentObservation(pending.ToolId,
                receipt.ResultReference, receipt.Succeeded,
                receipt.OutcomeCode, DateTimeOffset.UtcNow));
            state.PendingTool = null;
            await LogAsync(state, receipt.Succeeded ? "TOOL_SUCCEEDED" : "TOOL_FAILED",
                pending.ToolId, receipt.OutcomeCode, CancellationToken.None);
            return false;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            if (pending.Access != AccessLevel.Observe)
            {
                state.Status = AgentRunStatus.NeedsReconciliation;
                state.LastErrorCode = "UNCERTAIN_MUTATION_EXCEPTION";
                await LogAsync(state, "RECONCILIATION_REQUIRED", pending.ToolId,
                    state.LastErrorCode, CancellationToken.None);
                return true;
            }
            state.Observations.Add(new AgentObservation(pending.ToolId,
                null, false, "READ_ONLY_TOOL_EXCEPTION", DateTimeOffset.UtcNow));
            state.PendingTool = null;
            await LogAsync(state, "TOOL_FAILED", pending.ToolId, "READ_ONLY_TOOL_EXCEPTION",
                CancellationToken.None);
            return false;
        }
    }

    private async Task<AgentRunSnapshot> EndIncompleteAsync(AgentRunSnapshot state, string code)
    {
        state.Status = AgentRunStatus.Incomplete;
        state.LastErrorCode = code;
        await LogAsync(state, "RUN_INCOMPLETE", null, code, CancellationToken.None);
        return state;
    }

    private async Task LogAsync(AgentRunSnapshot state, string code,
        string? toolId, string? detailCode, CancellationToken ct)
    {
        var entry = new AgentActivityEvent(DateTimeOffset.UtcNow, code, toolId, detailCode);
        state.Activity.Add(entry);
        await _store.SaveAsync(state, ct);
        // Observer/UI failures must not change a persisted workflow outcome.
        try { _progress?.Report(entry); }
        catch (Exception) { /* isolate optional progress subscribers */ }
    }
}
