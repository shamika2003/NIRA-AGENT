using System.Text.Json;

namespace Nira.Core;

public enum ConversationStatus
{
    New, Running, AwaitingUser, AwaitingApproval, Completed, Paused,
    NeedsReconciliation, Failed
}

public enum OperationStatus
{
    Proposed, Authorized, Dispatched, Succeeded, Rejected, Failed, Unknown
}

public enum ToolRisk { ReadLocal, ReadExternal, WriteWorkspace, WriteOutsideWorkspace, Dangerous }

// An outcome contract supplied by the executing adapter, never the model.
// Reobserve means the requested mutation did not dispatch. UnverifiedEffect
// means it may have occurred and must not be replayed. Stop covers non-retryable
// outcomes such as a potentially billed provider request.
public enum ToolRecovery { None, Reobserve, Stop, UnverifiedEffect }

// One confirmation only arms abandonment. The second press within a bounded
// interval actually abandons. This is UI safety logic, independent of any task.
public sealed class TaskAbandonConfirmation
{
    private DateTimeOffset? _armedUtc;
    public bool IsArmed => _armedUtc is not null;
    public bool ConfirmOrArm(DateTimeOffset now)
    {
        if (_armedUtc is { } armed && now >= armed &&
            now - armed <= TimeSpan.FromSeconds(15))
        {
            _armedUtc = null;
            return true;
        }
        _armedUtc = now;
        return false;
    }
    public void Reset() => _armedUtc = null;

    public static void MarkAbandoned(ConversationState task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (task.Status is ConversationStatus.Completed or ConversationStatus.Failed)
            return;
        // If an operation could already have changed the outside world, never
        // erase that uncertainty just because the user abandoned future steps.
        if (task.Status == ConversationStatus.NeedsReconciliation ||
            task.Operations.Any(op => op.Status is OperationStatus.Dispatched or OperationStatus.Unknown) ||
            ToolRecoveryPolicy.HasUnverifiedEffect(task))
        {
            task.Status = ConversationStatus.NeedsReconciliation;
            task.RetryBlockCode = "USER_ABANDONED_WITH_UNCERTAIN_ACTION";
        }
        else
        {
            task.Status = ConversationStatus.Failed;
            task.RetryBlockCode = "USER_ABANDONED";
        }
    }
}

public sealed record ModelToolCall(string Id, string Name, string ArgumentsJson);

public sealed record ChatEntry(
    string Role,
    string? Content = null,
    string? ToolCallId = null,
    IReadOnlyList<ModelToolCall>? ToolCalls = null,
    IReadOnlyList<string>? AttachmentPaths = null,
    string? UserDisplayText = null);

public sealed record ModelReply(string? Content, IReadOnlyList<ModelToolCall> ToolCalls);

/// <summary>Optional model capability for a bounded, non-executing workflow decision pass.</summary>
/// <remarks>The planner can only propose a plan; it cannot authorize or execute tools.</remarks>
public interface IWorkflowPlanningModel
{
    Task<ModelReply> ProposeWorkflowAsync(IReadOnlyList<ChatEntry> messages,
        ToolDescriptor decisionTool, CancellationToken ct);
}

public sealed class AgentOperation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ToolName { get; set; } = "";
    public string ArgumentsJson { get; set; } = "{}";
    public OperationStatus Status { get; set; }
    public string? ResultJson { get; set; }
    public string? ErrorCode { get; set; }
    public ToolRecovery Recovery { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedUtc { get; set; }
}

public sealed class PlanStep
{
    public string Description { get; set; } = "";
    public string SuccessCriterion { get; set; } = "";
    public string Status { get; set; } = "pending";
    // Old checkpoints are general; new typed steps can require durable proof.
    public string Kind { get; set; } = "general";
    public string? EvidenceId { get; set; }
    // Optional registered-tool binding; old saved plans without it stay compatible.
    public string? ToolName { get; set; }
}

public sealed class ConversationState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long Revision { get; set; }
    public ConversationStatus Status { get; set; } = ConversationStatus.New;
    public string Objective { get; set; } = "";
    // A desktop chat groups independent task runs without merging their authority or receipts.
    public Guid? ChatThreadId { get; set; }
    public Guid? PreviousTaskId { get; set; }
    public string? ChatThreadTitle { get; set; }
    public string? PreviousChatContext { get; set; }
    public List<ChatEntry> Messages { get; set; } = [];
    public List<AgentOperation> Operations { get; set; } = [];
    public List<PlanStep> Plan { get; set; } = [];
    // Persisted so a resumed task does not spend another model round re-planning.
    public bool AutoPlanningAttempted { get; set; }
    // Visible and durable preflight outcome; never an authorization or execution receipt.
    public string AutomaticPlanningOutcome { get; set; } = "not_attempted";
    public string? RetryBlockCode { get; set; }
    public string? PendingQuestion { get; set; }
    public string? LastAnswer { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record ToolReceipt(
    bool Success,
    string Summary,
    JsonElement Data,
    bool ChangedState = false,
    string? ErrorCode = null)
{
    public ToolRecovery Recovery { get; init; }

    public static ToolReceipt Ok(string message, object? payload = null, bool changedState = false,
        ToolRecovery recovery = ToolRecovery.None) =>
        new(true, message, JsonSerializer.SerializeToElement(payload ?? new { }), changedState) { Recovery = recovery };

    public static ToolReceipt Fail(string errorCode, string message, object? detail = null,
        ToolRecovery recovery = ToolRecovery.None) =>
        new(false, message, JsonSerializer.SerializeToElement(detail ?? new { }), false, errorCode) { Recovery = recovery };
}

public sealed record AgentRunResult(
    ConversationStatus Status,
    string Message,
    Guid ConversationId,
    IReadOnlyList<string> EvidenceIds);

public sealed record AgentProgress(string Kind, string Summary, string? ToolId = null);

public sealed record ApprovalRequest(
    string ToolName, ToolRisk Risk, string Description, string Target, string ArgumentsJson);

/// <summary>Only an explicit yes token authorizes the particular displayed action.</summary>
public static class ApprovalInput
{
    public static bool IsExplicitYes(string? input) =>
        string.Equals(input?.Trim(), "YES", StringComparison.OrdinalIgnoreCase);
}

public interface IApprovalBroker
{
    Task<bool> RequestApprovalAsync(ApprovalRequest request, CancellationToken ct);
}

public interface IChatModel
{
    Task<ModelReply> CompleteAsync(IReadOnlyList<ChatEntry> messages,
        IReadOnlyList<ToolDescriptor> tools, CancellationToken ct);
}

/// <summary>Optional provisional visible-text stream; the final ModelReply remains authoritative.</summary>
public interface IStreamingProgress : IProgress<AgentProgress> { }

public interface IStreamingChatModel
{
    Task<ModelReply> CompleteStreamingAsync(IReadOnlyList<ChatEntry> messages,
        IReadOnlyList<ToolDescriptor> tools, Action<string?> preview, CancellationToken ct);
}

public interface IConversationStore
{
    Task<ConversationState?> LoadAsync(Guid id, CancellationToken ct);
    Task SaveAsync(ConversationState state, CancellationToken ct);
    Task<Guid?> FindRecentUnfinishedAsync(CancellationToken ct);
}

public interface IAgentTool
{
    ToolDescriptor Descriptor { get; }
    string GetTarget(JsonElement arguments);
    Task<ToolReceipt> ExecuteAsync(JsonElement arguments, CancellationToken ct);
}

// Optional tool-owned evidence constraint. The host checks it before approval and dispatch.
// Evidence is the current task journal, never a claim from the model or prior chat.
public interface IToolEvidenceRequirement
{
    string? MissingEvidence(ConversationState state, JsonElement arguments);
}

// Optional local readiness check; never grants permission or dispatches an action.
public interface IToolReadiness
{
    string? UnavailableReason();
}

public sealed record ToolDescriptor(
    string Name, string Description, ToolRisk Risk,
    JsonElement Parameters,
    bool HasSideEffects = false)
{
    // Null means ordinary bounded execution; a tool can impose a stricter
    // per-task dispatch budget for costly or otherwise non-repeatable effects.
    public int? MaximumDispatchAttempts { get; init; }
}

public interface IToolAuthorization
{
    bool IsPreapproved(ToolDescriptor tool, string target);
    string ExplainScope(ToolDescriptor tool, string target);
}

public sealed class AgentException(string message) : Exception(message);
