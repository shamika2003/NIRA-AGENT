namespace Nira.V2.Contracts;

// One NIRA identity. This enum selects a workflow, not another character.
public enum WorkflowKind { Conversation, Research, ComputerAction, ArtifactCreation }
public enum AccessLevel { Observe, Modify, Execute, Destructive }
public enum RunStatus { Planned, Running, Completed, Failed, AwaitingAuthorization, NeedsReconciliation, Cancelled }
public enum StepStatus { Pending, InProgress, Succeeded, Failed, NeedsReconciliation }

public sealed record AgentRequest(Guid RequestId, string UserText, WorkflowKind Kind, DateTimeOffset ReceivedUtc);
public sealed record StepDefinition(string Id, string HandlerId, AccessLevel Access,
    IReadOnlyList<string> DependsOn, string? IdempotencyKey = null);
public sealed record WorkflowPlan(Guid WorkflowId, AgentRequest Request,
    IReadOnlyList<StepDefinition> Steps);
public sealed record WorkflowEvent(DateTimeOffset AtUtc, string Code, string? StepId = null);

public sealed class StepRecord
{
    public required string Id { get; init; }
    public StepStatus Status { get; set; } = StepStatus.Pending;
    public int Attempts { get; set; }
    public string? ResultReference { get; set; }
    public string? ErrorCode { get; set; }
}

// JSON checkpoint schema v1. No API keys, retrieved page text, or secret tool
// arguments belong here; those are owned by scoped, encrypted adapters later.
public sealed class WorkflowRun
{
    public required Guid Id { get; init; }
    public required WorkflowPlan Plan { get; init; }
    public RunStatus Status { get; set; } = RunStatus.Planned;
    public Dictionary<string, StepRecord> Steps { get; init; } = new(StringComparer.Ordinal);
    public List<WorkflowEvent> Events { get; init; } = new();
    public string? LastErrorCode { get; set; }
}

public sealed record StepResult(bool Succeeded, string? ResultReference = null,
    string? ErrorCode = null)
{
    public static StepResult Success(string? reference = null) => new(true, reference);
    public static StepResult Failure(string code) => new(false, null, code);
}
