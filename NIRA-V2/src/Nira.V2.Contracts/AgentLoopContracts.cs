namespace Nira.V2.Contracts;

// All work belongs to ONE NIRA identity. The provider/model is an implementation detail.
public enum AgentRunStatus
{
    Planned, Running, AwaitingAuthorization, NeedsReconciliation,
    Completed, Incomplete, Cancelled
}

public enum AgentDecisionKind { Answer, InvokeTool, CannotComplete }
public enum PendingToolStage { Proposed, Dispatched }

public sealed record AgentToolSpec(string Id, string Description, AccessLevel Access);

// ToolArgumentReference is a key into a scoped input vault. It is not raw JSON,
// credentials, webpage content, or an executable shell command.
public sealed record AgentDecision(
    AgentDecisionKind Kind,
    string? Answer = null,
    string? ToolId = null,
    string? ToolArgumentReference = null,
    string? IdempotencyKey = null,
    string? ReasonCode = null)
{
    public static AgentDecision Respond(string text) => new(AgentDecisionKind.Answer, Answer: text);
    public static AgentDecision Tool(string id, string argumentReference, string? idempotencyKey = null) =>
        new(AgentDecisionKind.InvokeTool, ToolId: id, ToolArgumentReference: argumentReference,
            IdempotencyKey: idempotencyKey);
    public static AgentDecision Stop(string reasonCode) =>
        new(AgentDecisionKind.CannotComplete, ReasonCode: reasonCode);
}

public sealed record AgentObservation(
    string ToolId,
    string? ResultReference,
    bool Succeeded,
    string OutcomeCode,
    DateTimeOffset AtUtc);

// A success for a state-changing tool MUST be independently confirmed.
public sealed record AgentToolReceipt(bool Succeeded, bool OutcomeConfirmed,
    string? ResultReference = null, string OutcomeCode = "OK")
{
    public static AgentToolReceipt Verified(string? resultReference = null) =>
        new(true, true, resultReference);
    public static AgentToolReceipt ReadOnlyResult(string? resultReference = null) =>
        new(true, false, resultReference);
    public static AgentToolReceipt Uncertain(string code) => new(false, false, null, code);
}

public sealed class PendingAgentTool
{
    public required string ToolId { get; init; }
    public required string ArgumentReference { get; init; }
    public string? IdempotencyKey { get; init; }
    public required AccessLevel Access { get; init; }
    public PendingToolStage Stage { get; set; } = PendingToolStage.Proposed;
}

public sealed record AgentActivityEvent(
    DateTimeOffset AtUtc, string Code, string? ToolId = null, string? DetailCode = null);

public sealed class AgentRunSnapshot
{
    public required Guid Id { get; init; }
    public string AgentIdentity { get; init; } = "nira";
    public required string UserRequest { get; init; }
    public AgentRunStatus Status { get; set; } = AgentRunStatus.Planned;
    public int DecisionCount { get; set; }
    public int ToolCallCount { get; set; }
    public PendingAgentTool? PendingTool { get; set; }
    public string? FinalAnswer { get; set; }
    public string? LastErrorCode { get; set; }
    public List<AgentObservation> Observations { get; init; } = new();
    public List<AgentActivityEvent> Activity { get; init; } = new();
}

public sealed record AgentLoopLimits(int MaxDecisions = 12, int MaxTools = 8)
{
    public void Validate()
    {
        if (MaxDecisions < 1 || MaxDecisions > 100 || MaxTools < 0 || MaxTools > MaxDecisions)
            throw new ArgumentOutOfRangeException(nameof(MaxDecisions), "Invalid bounded agent-loop limits.");
    }
}
