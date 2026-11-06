using Nira.V2.Contracts;

namespace Nira.V2.Runtime;

// Intent interpretation can use NIRA's selected adaptive model; a model never
// gets to grant authority or write directly into persistent character state.
public interface IWorkflowPlanner
{
    WorkflowPlan Plan(AgentRequest request);
}

public interface IWorkflowStepHandler
{
    string HandlerId { get; }
    Task<StepResult> ExecuteAsync(WorkflowRun run, StepDefinition step,
        CancellationToken cancellationToken);
}

public interface IWorkflowStore
{
    Task SaveAsync(WorkflowRun run, CancellationToken cancellationToken);
    Task<WorkflowRun?> LoadAsync(Guid id, CancellationToken cancellationToken);
}

public interface IAuthorizationGate
{
    Task<bool> IsAuthorizedAsync(WorkflowRun run, StepDefinition step,
        CancellationToken cancellationToken);
}

public sealed class DenyMutationsGate : IAuthorizationGate
{
    public Task<bool> IsAuthorizedAsync(WorkflowRun run, StepDefinition step,
        CancellationToken cancellationToken) =>
        Task.FromResult(step.Access == AccessLevel.Observe);
}
