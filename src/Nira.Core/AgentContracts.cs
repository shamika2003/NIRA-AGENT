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

public sealed record ModelToolCall(string Id, string Name, string ArgumentsJson);

public sealed record ChatEntry(
    string Role,
    string? Content = null,
    string? ToolCallId = null,
    IReadOnlyList<ModelToolCall>? ToolCalls = null);

public sealed record ModelReply(string? Content, IReadOnlyList<ModelToolCall> ToolCalls);

public sealed class AgentOperation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ToolName { get; set; } = "";
    public string ArgumentsJson { get; set; } = "{}";
    public OperationStatus Status { get; set; }
    public string? ResultJson { get; set; }
    public string? ErrorCode { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedUtc { get; set; }
}

public sealed class PlanStep
{
    public string Description { get; set; } = "";
    public string SuccessCriterion { get; set; } = "";
    public string Status { get; set; } = "pending";
    public string? EvidenceId { get; set; }
}

public sealed class ConversationState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long Revision { get; set; }
    public ConversationStatus Status { get; set; } = ConversationStatus.New;
    public string Objective { get; set; } = "";
    public List<ChatEntry> Messages { get; set; } = [];
    public List<AgentOperation> Operations { get; set; } = [];
    public List<PlanStep> Plan { get; set; } = [];
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
    public static ToolReceipt Ok(string message, object? payload = null, bool changedState = false) =>
        new(true, message, JsonSerializer.SerializeToElement(payload ?? new { }), changedState);

    public static ToolReceipt Fail(string errorCode, string message, object? detail = null) =>
        new(false, message, JsonSerializer.SerializeToElement(detail ?? new { }), false, errorCode);
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

public sealed record ToolDescriptor(
    string Name, string Description, ToolRisk Risk,
    JsonElement Parameters,
    bool HasSideEffects = false);

public interface IToolAuthorization
{
    bool IsPreapproved(ToolDescriptor tool, string target);
    string ExplainScope(ToolDescriptor tool, string target);
}

public sealed class AgentException(string message) : Exception(message);
