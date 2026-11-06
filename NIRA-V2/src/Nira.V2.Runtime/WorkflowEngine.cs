using Nira.V2.Contracts;

namespace Nira.V2.Runtime;

// Foundation: deterministic, typed, one-step-at-a-time, checkpointed execution.
// This intentionally does NOT implement automatic background execution or
// replay uncertain side effects. A mutating in-flight step needs reconciliation.
public sealed class WorkflowEngine
{
    private readonly IWorkflowPlanner _planner;
    private readonly IWorkflowStore _store;
    private readonly IAuthorizationGate _authorization;
    private readonly IReadOnlyDictionary<string, IWorkflowStepHandler> _handlers;

    public WorkflowEngine(IWorkflowPlanner planner, IWorkflowStore store,
        IAuthorizationGate authorization, IEnumerable<IWorkflowStepHandler> handlers)
    {
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _handlers = handlers.ToDictionary(x => x.HandlerId, StringComparer.Ordinal);
    }

    public async Task<WorkflowRun> StartAsync(AgentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UserText);
        WorkflowPlan plan = _planner.Plan(request);
        ValidatePlan(request, plan);
        var run = new WorkflowRun
        {
            Id = plan.WorkflowId,
            Plan = plan,
            Steps = plan.Steps.ToDictionary(s => s.Id,
                s => new StepRecord { Id = s.Id }, StringComparer.Ordinal)
        };
        Event(run, "RUN_PLANNED");
        await _store.SaveAsync(run, cancellationToken);
        return await ContinueAsync(run, cancellationToken);
    }

    public async Task<WorkflowRun> ResumeAsync(Guid workflowId, CancellationToken cancellationToken = default)
    {
        WorkflowRun run = await _store.LoadAsync(workflowId, cancellationToken)
            ?? throw new KeyNotFoundException("Workflow checkpoint not found.");
        if (run.Status is RunStatus.Completed or RunStatus.Failed or
            RunStatus.Cancelled or RunStatus.NeedsReconciliation) return run;

        // A process may die between dispatching an action and saving its receipt.
        // Never replay an unverified side effect just to make progress.
        foreach (var step in run.Plan.Steps)
        {
            StepRecord saved = run.Steps[step.Id];
            if (saved.Status != StepStatus.InProgress) continue;
            if (step.Access != AccessLevel.Observe)
            {
                saved.Status = StepStatus.NeedsReconciliation;
                run.Status = RunStatus.NeedsReconciliation;
                run.LastErrorCode = "UNCERTAIN_SIDE_EFFECT";
                Event(run, "RECONCILIATION_REQUIRED", step.Id);
                await _store.SaveAsync(run, cancellationToken);
                return run;
            }
            saved.Status = StepStatus.Pending;
            Event(run, "READ_ONLY_RETRY_AFTER_RESTART", step.Id);
        }
        return await ContinueAsync(run, cancellationToken);
    }

    private async Task<WorkflowRun> ContinueAsync(WorkflowRun run, CancellationToken ct)
    {
        run.Status = RunStatus.Running;
        Event(run, "RUN_ACTIVE");
        await _store.SaveAsync(run, ct);
        foreach (StepDefinition step in run.Plan.Steps)
        {
            StepRecord state = run.Steps[step.Id];
            if (state.Status == StepStatus.Succeeded) continue;
            if (state.Status != StepStatus.Pending)
                throw new InvalidOperationException("Unexpected step state in pending workflow.");
            if (step.DependsOn.Any(d => run.Steps[d].Status != StepStatus.Succeeded))
                throw new InvalidOperationException("Workflow dependency was not satisfied.");

            if (step.Access != AccessLevel.Observe &&
                !await _authorization.IsAuthorizedAsync(run, step, ct))
            {
                run.Status = RunStatus.AwaitingAuthorization;
                Event(run, "AUTHORIZATION_REQUIRED", step.Id);
                await _store.SaveAsync(run, ct);
                return run;
            }
            if (!_handlers.TryGetValue(step.HandlerId, out var handler))
            {
                state.Status = StepStatus.Failed;
                state.ErrorCode = "HANDLER_NOT_REGISTERED";
                run.Status = RunStatus.Failed;
                run.LastErrorCode = state.ErrorCode;
                Event(run, "STEP_FAILED", step.Id);
                await _store.SaveAsync(run, ct);
                return run;
            }

            state.Status = StepStatus.InProgress;
            state.Attempts++;
            Event(run, "STEP_STARTED", step.Id);
            await _store.SaveAsync(run, ct); // Persist before any possible side effect.
            try
            {
                StepResult receipt = await handler.ExecuteAsync(run, step, ct);
                if (!receipt.Succeeded)
                {
                    state.Status = StepStatus.Failed;
                    state.ErrorCode = receipt.ErrorCode ?? "STEP_REPORTED_FAILURE";
                    run.Status = RunStatus.Failed;
                    run.LastErrorCode = state.ErrorCode;
                    Event(run, "STEP_FAILED", step.Id);
                    await _store.SaveAsync(run, CancellationToken.None);
                    return run;
                }
                state.Status = StepStatus.Succeeded;
                state.ResultReference = receipt.ResultReference;
                Event(run, "STEP_SUCCEEDED", step.Id);
                await _store.SaveAsync(run, CancellationToken.None);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Cancellation while modifying might have changed external state.
                bool uncertain = step.Access != AccessLevel.Observe;
                state.Status = uncertain ? StepStatus.NeedsReconciliation : StepStatus.Pending;
                run.Status = uncertain ? RunStatus.NeedsReconciliation : RunStatus.Cancelled;
                run.LastErrorCode = uncertain ? "CANCELLED_DURING_SIDE_EFFECT" : "CANCELLED";
                Event(run, uncertain ? "RECONCILIATION_REQUIRED" : "RUN_CANCELLED", step.Id);
                await _store.SaveAsync(run, CancellationToken.None);
                return run;
            }
            catch (Exception)
            {
                // Unknown write outcomes cannot safely be retried automatically.
                bool uncertain = step.Access != AccessLevel.Observe;
                state.Status = uncertain ? StepStatus.NeedsReconciliation : StepStatus.Failed;
                state.ErrorCode = uncertain ? "UNKNOWN_SIDE_EFFECT_OUTCOME" : "READ_STEP_EXCEPTION";
                run.Status = uncertain ? RunStatus.NeedsReconciliation : RunStatus.Failed;
                run.LastErrorCode = state.ErrorCode;
                Event(run, uncertain ? "RECONCILIATION_REQUIRED" : "STEP_FAILED", step.Id);
                await _store.SaveAsync(run, CancellationToken.None);
                return run;
            }
        }
        run.Status = RunStatus.Completed;
        Event(run, "RUN_COMPLETED");
        await _store.SaveAsync(run, CancellationToken.None);
        return run;
    }

    private static void ValidatePlan(AgentRequest request, WorkflowPlan plan)
    {
        if (plan.Request != request || plan.WorkflowId == Guid.Empty || plan.Steps.Count == 0)
            throw new InvalidOperationException("Invalid workflow plan identity or empty plan.");
        var done = new HashSet<string>(StringComparer.Ordinal);
        foreach (StepDefinition step in plan.Steps)
        {
            if (string.IsNullOrWhiteSpace(step.Id) || string.IsNullOrWhiteSpace(step.HandlerId) ||
                !done.Add(step.Id) || step.DependsOn.Any(d => !done.Contains(d)))
                throw new InvalidOperationException("Step IDs or dependency ordering are invalid.");
            if (step.Access != AccessLevel.Observe && string.IsNullOrWhiteSpace(step.IdempotencyKey))
                throw new InvalidOperationException("State-changing steps require an idempotency/reconciliation key.");
        }
    }

    private static void Event(WorkflowRun run, string code, string? stepId = null) =>
        run.Events.Add(new WorkflowEvent(DateTimeOffset.UtcNow, code, stepId));
}
