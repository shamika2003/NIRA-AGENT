/*
 * filename: SegaBranchContracts.cs
 */

namespace SegaAgent.Branches;


// =============================================================
// FIRST-CLASS BRANCH / TASK GRAPH STATE
//
// A branch is executable work owned by one persistent goal.
// It is not a goal, memory or commitment.
// =============================================================

public enum SegaBranchStatus
{
    Pending,

    Active,

    Waiting,

    Blocked,

    Completed,

    Failed,

    Cancelled
}


public enum SegaBranchJoinPolicy
{
    Required,

    Opportunistic,

    Background
}


public enum SegaBranchProposalAction
{
    Create,

    Activate,

    SetPending,

    SetWaiting,

    SetBlocked,

    RecordProgress,

    Revise,

    Complete,

    Fail,

    Cancel
}


public enum SegaBranchEvidenceSource
{
    CurrentEvent,

    SegaReply,

    ExecutivePlan,

    CapabilityResult
}


public sealed record SegaBranchState
{
    public Guid Id { get; init; }

    public Guid GoalId { get; init; }

    public Guid? ParentBranchId { get; init; }

    public string Fingerprint { get; init; } = string.Empty;

    public string Objective { get; init; } = string.Empty;

    public SegaBranchStatus Status { get; init; } = SegaBranchStatus.Pending;

    public SegaBranchJoinPolicy JoinPolicy { get; init; } =
        SegaBranchJoinPolicy.Required;

    public double Priority { get; init; } = 0.50;

    public IReadOnlyList<string> CompletionCriteria { get; init; } =
        Array.Empty<string>();

    public IReadOnlyList<Guid> DependsOnBranchIds { get; init; } =
        Array.Empty<Guid>();

    public string? WaitingFor { get; init; }

    public string? Blocker { get; init; }

    public string? FailureReason { get; init; }

    public string? ResultSummary { get; init; }

    public int EvidenceCount { get; init; }

    public string? LastEvidenceSummary { get; init; }

    public string? LastReason { get; init; }

    public Guid? SourceEventId { get; init; }

    public string? SourceEventName { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public DateTimeOffset? ResolvedAt { get; init; }

    public bool IsResolved =>
        Status is
            SegaBranchStatus.Completed
            or SegaBranchStatus.Failed
            or SegaBranchStatus.Cancelled;

    public bool IsSuccessful =>
        Status == SegaBranchStatus.Completed;

    public bool IsOpen =>
        !IsResolved;

    public SegaBranchState Normalize()
    {
        if (Id == Guid.Empty)
        {
            throw new InvalidOperationException(
                "A Sega branch requires an ID.");
        }

        if (GoalId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "A Sega branch requires a parent goal ID.");
        }

        if (ParentBranchId == Id)
        {
            throw new InvalidOperationException(
                "A Sega branch cannot be its own parent.");
        }

        if (string.IsNullOrWhiteSpace(Fingerprint))
        {
            throw new InvalidOperationException(
                "A Sega branch requires a fingerprint.");
        }

        if (string.IsNullOrWhiteSpace(Objective))
        {
            throw new InvalidOperationException(
                "A Sega branch requires an objective.");
        }

        string objective = Objective.Trim();

        if (objective.Length > 1200)
        {
            throw new InvalidOperationException(
                "A Sega branch objective is too long.");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;

        return this with
        {
            Fingerprint = Fingerprint.Trim().ToLowerInvariant(),
            Objective = objective,
            Priority = Math.Clamp(Priority, 0.0, 1.0),
            CompletionCriteria = NormalizeStrings(
                CompletionCriteria,
                12,
                500),
            DependsOnBranchIds =
                (DependsOnBranchIds ?? Array.Empty<Guid>())
                    .Where(id => id != Guid.Empty && id != Id)
                    .Distinct()
                    .Take(32)
                    .ToArray(),
            WaitingFor = NormalizeOptional(WaitingFor, 1000),
            Blocker = NormalizeOptional(Blocker, 1000),
            FailureReason = NormalizeOptional(FailureReason, 1600),
            ResultSummary = NormalizeOptional(ResultSummary, 2400),
            LastEvidenceSummary = NormalizeOptional(
                LastEvidenceSummary,
                1600),
            LastReason = NormalizeOptional(LastReason, 1600),
            SourceEventName = NormalizeOptional(SourceEventName, 200),
            EvidenceCount = Math.Max(0, EvidenceCount),
            CreatedAt = CreatedAt == default ? now : CreatedAt,
            UpdatedAt = UpdatedAt == default ? now : UpdatedAt
        };
    }

    internal static IReadOnlyList<string> NormalizeStrings(
        IReadOnlyList<string>? values,
        int maximumCount,
        int maximumLength)
    {
        if (values == null || values.Count == 0)
        {
            return Array.Empty<string>();
        }

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Where(value => value.Length <= maximumLength)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maximumCount)
            .ToArray();
    }

    internal static string? NormalizeOptional(
        string? value,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string clean = value.Trim();

        if (clean.Length > maximumLength)
        {
            throw new InvalidOperationException(
                "A Sega branch field is too long.");
        }

        return clean;
    }
}


// =============================================================
// MAIN-COGNITION PROPOSAL
//
// The model proposes branch mutations. SegaBranchService owns
// validation and authoritative persistence.
// =============================================================

public sealed record SegaBranchProposal
{
    public SegaBranchProposalAction Action { get; init; }

    public string? BranchId { get; init; }

    public string? GoalId { get; init; }

    public string? ParentBranchId { get; init; }

    public string Objective { get; init; } = string.Empty;

    public SegaBranchJoinPolicy? JoinPolicy { get; init; }

    public double? Priority { get; init; }

    public IReadOnlyList<string> CompletionCriteria { get; init; } =
        Array.Empty<string>();

    public IReadOnlyList<string> DependsOnBranchIds { get; init; } =
        Array.Empty<string>();

    public string? WaitingFor { get; init; }

    public string? Blocker { get; init; }

    public string? FailureReason { get; init; }

    public string? ResultSummary { get; init; }

    public SegaBranchEvidenceSource EvidenceSource { get; init; } =
        SegaBranchEvidenceSource.CurrentEvent;

    public string EvidenceQuote { get; init; } = string.Empty;

    public string EvidenceSummary { get; init; } = string.Empty;

    public string Reason { get; init; } = string.Empty;

    public double Confidence { get; init; }

    public SegaBranchProposal Normalize()
    {
        string objective = string.IsNullOrWhiteSpace(Objective)
            ? string.Empty
            : Objective.Trim();

        if (objective.Length > 1200)
        {
            throw new InvalidOperationException(
                "Branch proposal objective is too long.");
        }

        string evidenceQuote =
            EvidenceQuote?.Trim() ?? string.Empty;

        string evidenceSummary =
            EvidenceSummary?.Trim() ?? string.Empty;

        string reason =
            Reason?.Trim() ?? string.Empty;

        if (evidenceQuote.Length > 1200 ||
            evidenceSummary.Length > 1600 ||
            reason.Length > 1600)
        {
            throw new InvalidOperationException(
                "Branch proposal evidence/reason is too long.");
        }

        return this with
        {
            BranchId = NormalizeId(BranchId),
            GoalId = NormalizeId(GoalId),
            ParentBranchId = NormalizeId(ParentBranchId),
            Objective = objective,
            Priority = Priority.HasValue
                ? Math.Clamp(Priority.Value, 0.0, 1.0)
                : null,
            CompletionCriteria = SegaBranchState.NormalizeStrings(
                CompletionCriteria,
                12,
                500),
            DependsOnBranchIds = SegaBranchState.NormalizeStrings(
                DependsOnBranchIds,
                32,
                80),
            WaitingFor = SegaBranchState.NormalizeOptional(
                WaitingFor,
                1000),
            Blocker = SegaBranchState.NormalizeOptional(
                Blocker,
                1000),
            FailureReason = SegaBranchState.NormalizeOptional(
                FailureReason,
                1600),
            ResultSummary = SegaBranchState.NormalizeOptional(
                ResultSummary,
                2400),
            EvidenceQuote = evidenceQuote,
            EvidenceSummary = evidenceSummary,
            Reason = reason,
            Confidence = Math.Clamp(Confidence, 0.0, 1.0)
        };
    }

    public string BuildSignature()
    {
        SegaBranchProposal normalized = Normalize();

        return string.Join(
            '|',
            normalized.Action,
            normalized.BranchId ?? string.Empty,
            normalized.GoalId ?? string.Empty,
            normalized.ParentBranchId ?? string.Empty,
            normalized.Objective.ToLowerInvariant(),
            normalized.JoinPolicy?.ToString() ?? string.Empty,
            normalized.Priority?.ToString("F4") ?? string.Empty,
            string.Join("~", normalized.CompletionCriteria.Select(value => value.ToLowerInvariant())),
            string.Join("~", normalized.DependsOnBranchIds.Select(value => value.ToLowerInvariant())),
            normalized.WaitingFor?.ToLowerInvariant() ?? string.Empty,
            normalized.Blocker?.ToLowerInvariant() ?? string.Empty,
            normalized.FailureReason?.ToLowerInvariant() ?? string.Empty,
            normalized.ResultSummary?.ToLowerInvariant() ?? string.Empty,
            normalized.EvidenceSource,
            normalized.EvidenceQuote.ToLowerInvariant());
    }

    private static string? NormalizeId(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}


public sealed record SegaBranchEvidenceRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid BranchId { get; init; }

    public Guid GoalId { get; init; }

    public Guid? SourceEventId { get; init; }

    public string? SourceEventName { get; init; }

    public SegaBranchEvidenceSource Source { get; init; }

    public string Quote { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public string Reason { get; init; } = string.Empty;

    public DateTimeOffset RecordedAt { get; init; } =
        DateTimeOffset.UtcNow;
}


public enum SegaBranchApplyAction
{
    Rejected,

    Created,

    Transitioned,

    Revised,

    EvidenceRecorded,

    Duplicate,

    NoChange
}


public sealed record SegaBranchApplyResult
{
    public SegaBranchApplyAction Action { get; init; }

    public SegaBranchState? Branch { get; init; }

    public string Reason { get; init; } = string.Empty;

    public bool Changed =>
        Action is
            SegaBranchApplyAction.Created
            or SegaBranchApplyAction.Transitioned
            or SegaBranchApplyAction.Revised
            or SegaBranchApplyAction.EvidenceRecorded;
}


public sealed record SegaBranchResultEvent
{
    public Guid BranchId { get; init; }

    public Guid GoalId { get; init; }

    public Guid? ParentBranchId { get; init; }

    public SegaBranchStatus Status { get; init; }

    public SegaBranchJoinPolicy JoinPolicy { get; init; }

    public string Objective { get; init; } = string.Empty;

    public string? ResultSummary { get; init; }

    public string? FailureReason { get; init; }

    public DateTimeOffset ResolvedAt { get; init; }
}
