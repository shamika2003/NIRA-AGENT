using NIRAAgent.Diagnostics;
using NIRAAgent.Runtime.Contracts;

namespace NIRAAgent.Runtime.Runtime.AgentLoop;

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
    private readonly IAgentTaskLifecycle? _taskLifecycle;

    public AgentLoopEngine(IAgentReasoner reasoner, IAgentStateStore store,
        IAgentPermissionGate permissions, IEnumerable<IAgentTool> tools,
        AgentLoopLimits? limits = null, IProgress<AgentActivityEvent>? progress = null,
        IAgentTaskLifecycle? taskLifecycle = null)
    {
        _reasoner = reasoner ?? throw new ArgumentNullException(nameof(reasoner));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _tools = tools.ToDictionary(t => t.Spec.Id, StringComparer.Ordinal);
        _limits = limits ?? new AgentLoopLimits();
        _limits.Validate();
        _progress = progress;
        _taskLifecycle = taskLifecycle;
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

    /// <summary>
    /// One durable run per trusted mind event. A redelivered event with the SAME ID
    /// cannot create a second tool execution. Dispatched writes are NEVER replayed
    /// after interruption, even if their final receipt is absent.
    /// </summary>
    public async Task<AgentRunSnapshot> RunOrResumeAsync(string request, Guid eventId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request);
        if (eventId == Guid.Empty) throw new ArgumentException("Event ID required.", nameof(eventId));
        await using var lease = await _store.AcquireLeaseAsync(eventId, ct);
        AgentRunSnapshot? state = await _store.LoadAsync(eventId, ct);
        if (state is null)
        {
            state = new AgentRunSnapshot { Id = eventId, UserRequest = request };
            await LogAsync(state, "RUN_CREATED", null, null, ct);
            return await ExecuteAsync(state, ct);
        }
        if (!string.Equals(state.UserRequest, request, StringComparison.Ordinal))
            throw new InvalidDataException("An event ID cannot be reused with a different task.");
        if (state.Status is AgentRunStatus.Completed or AgentRunStatus.Cancelled or
            AgentRunStatus.NeedsReconciliation or AgentRunStatus.Incomplete)
            return state;
        if (state.PendingTool is { Stage: PendingToolStage.Dispatched,
                Access: not AccessLevel.Observe })
        {
            state.Status = AgentRunStatus.NeedsReconciliation;
            state.LastErrorCode = "UNCERTAIN_MUTATION_AFTER_RESTART";
            await LogAsync(state, "RECONCILIATION_REQUIRED", state.PendingTool.ToolId,
                state.LastErrorCode, CancellationToken.None);
            return state;
        }
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
            // One contract per trusted current request, persisted BEFORE tool use.
            // A restart reuses the original criteria; later model turns cannot
            // quietly reinterpret the user's objective or erase pending work.
            if (state.TaskContract is null && _taskLifecycle is not null &&
                AgentTaskContractPolicy.ShouldPlan(state.UserRequest))
            {
                state.TaskContract = await _taskLifecycle.CreateAsync(state.UserRequest, ct);
                await LogAsync(state, "TASK_CONTRACT_CHECKPOINTED", null, null, ct);
            }
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
                        // A model-generated final message is a PROPOSAL, never
                        // evidence of fulfilling a multi-stage user request.
                        // Reconcile actual encrypted, audited receipts before
                        // accepting it as a terminal success.
                        if (state.TaskContract is not null)
                        {
                            if (_taskLifecycle is null)
                                return await EndIncompleteAsync(state, "TASK_COMPLETION_GUARD_MISSING");
                            AgentTaskCompletionAssessment review =
                                await _taskLifecycle.AssessAsync(state, ct);
                            if (!review.Accept)
                            {
                                state.TaskContract.RejectedPrematureAnswers++;
                                state.TaskContract.LastUnfinishedReason = review.Reason.Length > 380
                                    ? review.Reason[..380] : review.Reason;
                                await LogAsync(state, "ANSWER_DEFERRED_UNFINISHED_TASK", null,
                                    "UNFINISHED_TASK", CancellationToken.None);
                                // Allow a small number of new MODEL decisions, not
                                // any automatic tool replay. A second promise
                                // without an executable action cannot run forever.
                                if (state.TaskContract.RejectedPrematureAnswers <= 2 &&
                                    state.DecisionCount < _limits.MaxDecisions &&
                                    state.ToolCallCount < _limits.MaxTools)
                                    continue;
                                return await EndIncompleteAsync(state, "TASK_DELIVERABLE_UNVERIFIED");
                            }
                        }
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
            NIRADiagnosticTrace.Event("AGENT-TOOL-RESULT",
                $"Run={state.Id:N} Tool={pending.ToolId} " +
                $"Succeeded={receipt.Succeeded} OutcomeConfirmed={receipt.OutcomeConfirmed} " +
                $"Code={receipt.OutcomeCode} HasReceipt={!string.IsNullOrWhiteSpace(receipt.ResultReference)}");
            if (pending.Access != AccessLevel.Observe &&
                !receipt.Succeeded && receipt.OutcomeConfirmed)
            {
                // The authoritative executor proved that NO mutation occurred.
                // Never replay an attempted write, but do not falsely request
                // reconciliation of a change that definitively did not happen.
                state.Observations.Add(new AgentObservation(pending.ToolId,
                    receipt.ResultReference, false, receipt.OutcomeCode, DateTimeOffset.UtcNow));
                state.PendingTool = null;
                await LogAsync(state, "TOOL_CONFIRMED_NO_EFFECT", pending.ToolId,
                    receipt.OutcomeCode, CancellationToken.None);
                // Only a precisely audited pre-publication rejection may lead
                // to a NEW model decision. Do not replay the original call.
                // All authorization denials and other no-effect cases still
                // terminate. An uncertain mutation was handled separately.
                if (receipt.OutcomeCode == "CAPABILITY_REJECTED_NO_EFFECT" &&
                    state.DecisionCount < _limits.MaxDecisions &&
                    state.ToolCallCount < _limits.MaxTools &&
                    state.Observations.AsEnumerable().Reverse()
                        .TakeWhile(x => !x.Succeeded &&
                            x.OutcomeCode == "CAPABILITY_REJECTED_NO_EFFECT")
                        .Count() <= 2)
                    return false;
                state.Status = AgentRunStatus.Incomplete;
                state.LastErrorCode = receipt.OutcomeCode;
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

    // A failed but audited pre-publication write should not force an uncertain
    // outcome or end the entire task. The NEXT tool is a separately proposed,
    // separately authorized operation; the engine never replays the old write.
    public static async Task<int> RunConfirmedNoEffectRecoverySelfCheckAsync(
        IAgentStateStore store, CancellationToken ct = default)
    {
        var correction = new NoEffectCorrectionFixture();
        var tools = new IAgentTool[] {
            new ReceiptFixture("test.failed_write", AgentToolReceipt.ConfirmedNoChange(
                "CAPABILITY_REJECTED_NO_EFFECT")),
            new ReceiptFixture("test.prerequisite", AgentToolReceipt.Verified("created-parent"))
        };
        var engine = new AgentLoopEngine(correction, store, new AllowFixture(), tools,
            new AgentLoopLimits(6, 4));
        AgentRunSnapshot successful = await engine.RunOrResumeAsync(
            "Check a missing prerequisite then create it as a fresh operation.", Guid.NewGuid(), ct);
        if (successful.Status != AgentRunStatus.Completed || correction.Decisions != 3 ||
            successful.ToolCallCount != 2 || successful.Observations.Count != 2 ||
            successful.Observations[0].Succeeded ||
            successful.Observations[0].OutcomeCode != "CAPABILITY_REJECTED_NO_EFFECT" ||
            !successful.Observations[1].Succeeded)
            throw new InvalidOperationException("Audited no-effect did not permit a new decision.");
        var uncertainReasoner = new SingleToolFixture("test.uncertain");
        var uncertain = new AgentLoopEngine(uncertainReasoner, store, new AllowFixture(),
            new IAgentTool[] { new ReceiptFixture("test.uncertain",
                AgentToolReceipt.Uncertain("AUTHORITATIVE_OUTCOME_UNCERTAIN")) },
            new AgentLoopLimits(3, 2));
        AgentRunSnapshot paused = await uncertain.RunOrResumeAsync(
            "Perform one modification and verify its outcome.", Guid.NewGuid(), ct);
        if (paused.Status != AgentRunStatus.NeedsReconciliation ||
            paused.ToolCallCount != 1 || uncertainReasoner.Decisions != 1)
            throw new InvalidOperationException("Uncertain writes must still stop, without replay.");
        var deniedReasoner = new SingleToolFixture("test.denied");
        var denied = new AgentLoopEngine(deniedReasoner, store, new AllowFixture(),
            new IAgentTool[] { new ReceiptFixture("test.denied",
                AgentToolReceipt.ConfirmedNoChange("AUTHORIZATION_NOT_GRANTED")) },
            new AgentLoopLimits(3, 2));
        AgentRunSnapshot rejected = await denied.RunOrResumeAsync(
            "Attempt one authorized change.", Guid.NewGuid(), ct);
        if (rejected.Status != AgentRunStatus.Incomplete ||
            deniedReasoner.Decisions != 1 || rejected.ToolCallCount != 1)
            throw new InvalidOperationException("Denied modifications must never be retried.");
        return 3;
    }

    private sealed class AllowFixture : IAgentPermissionGate
    {
        public Task<bool> IsAuthorizedAsync(Guid runId, AgentToolSpec spec,
            string reference, CancellationToken ct) => Task.FromResult(true);
    }
    private sealed class ReceiptFixture(string id, AgentToolReceipt receipt) : IAgentTool
    {
        public AgentToolSpec Spec { get; } = new(id, "Isolated no-effect regression", AccessLevel.Modify);
        public Task<AgentToolReceipt> ExecuteAsync(string reference, CancellationToken ct) =>
            Task.FromResult(receipt);
    }
    private sealed class SingleToolFixture(string id) : IAgentReasoner
    {
        internal int Decisions { get; private set; }
        public Task<AgentDecision> DecideAsync(AgentRunSnapshot state,
            IReadOnlyList<AgentToolSpec> tools, CancellationToken ct)
        {
            Decisions++;
            return Task.FromResult(AgentDecision.Tool(id, "test-input", "unique-operation"));
        }
    }
    private sealed class NoEffectCorrectionFixture : IAgentReasoner
    {
        internal int Decisions { get; private set; }
        public Task<AgentDecision> DecideAsync(AgentRunSnapshot state,
            IReadOnlyList<AgentToolSpec> tools, CancellationToken ct)
        {
            Decisions++;
            return Task.FromResult(Decisions switch {
                1 => AgentDecision.Tool("test.failed_write", "request", "first-intent"),
                2 => AgentDecision.Tool("test.prerequisite", "new-request", "new-intent"),
                _ => AgentDecision.Respond("The new authorized corrective operation was observed.")
            });
        }
    }

    // Actual checkpointed loop regression: a model that promises a file but
    // never issues the write must NOT reach Completed. The independent receipt
    // inspector is exercised separately with encrypted real-shaped receipts.
    public static async Task<int> RunTaskExecutionSelfCheckAsync(
        IAgentStateStore store, CancellationToken ct = default)
    {
        var reasoner = new PrematureAnswerFixture();
        var engine = new AgentLoopEngine(reasoner, store,
            new ObserveOnlyPermissionGate(), Array.Empty<IAgentTool>(),
            new AgentLoopLimits(6, 5), taskLifecycle: reasoner);
        AgentRunSnapshot run = await engine.RunOrResumeAsync(
            "Create Tesla_Report.docx and inspect the saved Word file.",
            Guid.NewGuid(), ct);
        if (run.Status != AgentRunStatus.Incomplete ||
            run.LastErrorCode != "TASK_DELIVERABLE_UNVERIFIED" ||
            run.TaskContract?.RejectedPrematureAnswers != 3 ||
            reasoner.Decisions != 3 || run.ToolCallCount != 0)
            throw new InvalidOperationException(
                "Native agent loop incorrectly accepted an unexecuted file promise.");
        AgentRunSnapshot? persisted = await store.LoadAsync(run.Id, ct);
        if (persisted?.TaskContract?.RequestedFileNames.SingleOrDefault() !=
            "Tesla_Report.docx" || persisted.FinalAnswer is not null ||
            persisted.Status != AgentRunStatus.Incomplete)
            throw new InvalidOperationException("Unfinished task contract was not checkpointed.");
        var chat = new AgentLoopEngine(new SimpleChatFixture(), store,
            new ObserveOnlyPermissionGate(), Array.Empty<IAgentTool>(),
            new AgentLoopLimits(2, 0));
        AgentRunSnapshot basic = await chat.RunOrResumeAsync("Hey Nira!", Guid.NewGuid(), ct);
        if (basic.Status != AgentRunStatus.Completed || basic.TaskContract is not null ||
            basic.FinalAnswer != "Hello!")
            throw new InvalidOperationException("Ordinary conversation was blocked by task contracts.");
        return 3;
    }

    private sealed class PrematureAnswerFixture : IAgentReasoner, IAgentTaskLifecycle
    {
        internal int Decisions { get; private set; }
        public Task<AgentTaskContract> CreateAsync(string userRequest, CancellationToken ct) =>
            Task.FromResult(AgentTaskContractPolicy.Create(userRequest));
        public Task<AgentTaskCompletionAssessment> AssessAsync(AgentRunSnapshot state,
            CancellationToken ct) =>
            Task.FromResult(new AgentTaskCompletionAssessment(false,
                "No audited artifact publication receipt"));
        public Task<AgentDecision> DecideAsync(AgentRunSnapshot state,
            IReadOnlyList<AgentToolSpec> tools, CancellationToken ct)
        {
            Decisions++;
            return Task.FromResult(AgentDecision.Respond("I'm going to build that file now."));
        }
    }
    private sealed class SimpleChatFixture : IAgentReasoner
    {
        public Task<AgentDecision> DecideAsync(AgentRunSnapshot state,
            IReadOnlyList<AgentToolSpec> tools, CancellationToken ct) =>
            Task.FromResult(AgentDecision.Respond("Hello!"));
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
