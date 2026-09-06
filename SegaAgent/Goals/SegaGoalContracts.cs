/*
 * filename: SegaGoalContracts.cs
 */

namespace SegaAgent.Goals;


// =============================================================
// PERSISTENT GOAL STATE
//
// A goal is executable/intention state owned by Sega's executive.
// It is not a memory and it is not a commitment.
// =============================================================

public enum SegaGoalStatus
{
    Pending,
    Active,
    Waiting,
    Blocked,
    Completed,
    Cancelled
}


public enum SegaGoalSource
{
    UserRequest,
    Commitment,
    SegaInitiated,
    System
}


public enum SegaGoalProposalAction
{
    Create,
    Activate,
    SetPending,
    SetWaiting,
    SetBlocked,
    RecordProgress,
    Revise,
    Complete,
    Cancel
}


public enum SegaGoalEvidenceSource
{
    CurrentEvent,
    SegaReply,
    CapabilityResult
}


public sealed record SegaGoalState
{
    public Guid Id { get; init; }

    public string Fingerprint { get; init; } = string.Empty;

    public string Objective { get; init; } = string.Empty;

    public SegaGoalStatus Status { get; init; } = SegaGoalStatus.Pending;

    public SegaGoalSource Source { get; init; } = SegaGoalSource.UserRequest;

    public double Priority { get; init; } = 0.50;

    public Guid? LinkedCommitmentId { get; init; }

    public IReadOnlyList<string> CompletionCriteria { get; init; } =
        Array.Empty<string>();

    public IReadOnlyList<Guid> DependsOnGoalIds { get; init; } =
        Array.Empty<Guid>();

    public string? WaitingFor { get; init; }

    public string? Blocker { get; init; }

    public DateTimeOffset? NextWakeAtUtc { get; init; }

    public int EvidenceCount { get; init; }

    public string? LastEvidenceSummary { get; init; }

    public string? LastReason { get; init; }

    public Guid? SourceEventId { get; init; }

    public string? SourceEventName { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public DateTimeOffset? ResolvedAt { get; init; }

    public bool IsResolved =>
        Status is SegaGoalStatus.Completed or SegaGoalStatus.Cancelled;

    public bool IsOpen =>
        !IsResolved;

    public SegaGoalState Normalize()
    {
        if (Id == Guid.Empty)
        {
            throw new InvalidOperationException(
                "A Sega goal requires an ID.");
        }

        if (string.IsNullOrWhiteSpace(Fingerprint))
        {
            throw new InvalidOperationException(
                "A Sega goal requires a fingerprint.");
        }

        if (string.IsNullOrWhiteSpace(Objective))
        {
            throw new InvalidOperationException(
                "A Sega goal requires an objective.");
        }

        string objective = Objective.Trim();

        if (objective.Length > 1200)
        {
            throw new InvalidOperationException(
                "A Sega goal objective is too long.");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;

        return this with
        {
            Fingerprint = Fingerprint.Trim().ToLowerInvariant(),
            Objective = objective,
            Priority = Math.Clamp(Priority, 0.0, 1.0),
            CompletionCriteria = NormalizeStrings(CompletionCriteria, 12, 500),
            DependsOnGoalIds = (DependsOnGoalIds ?? Array.Empty<Guid>())
                .Where(id => id != Guid.Empty)
                .Distinct()
                .Take(24)
                .ToArray(),
            WaitingFor = NormalizeOptional(WaitingFor, 1000),
            Blocker = NormalizeOptional(Blocker, 1000),
            LastEvidenceSummary = NormalizeOptional(LastEvidenceSummary, 1600),
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
                "A Sega goal field is too long.");
        }

        return clean;
    }
}


// =============================================================
// MAIN-COGNITION PROPOSAL
//
// The model may propose. SegaGoalService validates and commits.
// =============================================================

public sealed record SegaGoalProposal
{
    public SegaGoalProposalAction Action { get; init; }

    public string? GoalId { get; init; }

    public string Objective { get; init; } = string.Empty;

    public double? Priority { get; init; }

    public string? LinkedCommitmentId { get; init; }

    public IReadOnlyList<string> CompletionCriteria { get; init; } =
        Array.Empty<string>();

    public IReadOnlyList<string> DependsOnGoalIds { get; init; } =
        Array.Empty<string>();

    public string? WaitingFor { get; init; }

    public string? Blocker { get; init; }

    public DateTimeOffset? NextWakeAtUtc { get; init; }

    public SegaGoalEvidenceSource EvidenceSource { get; init; } =
        SegaGoalEvidenceSource.CurrentEvent;

    public string EvidenceQuote { get; init; } = string.Empty;

    public string EvidenceSummary { get; init; } = string.Empty;

    public string Reason { get; init; } = string.Empty;

    public double Confidence { get; init; }

    public SegaGoalProposal Normalize()
    {
        string objective = string.IsNullOrWhiteSpace(Objective)
            ? string.Empty
            : Objective.Trim();

        if (objective.Length > 1200)
        {
            throw new InvalidOperationException(
                "Goal proposal objective is too long.");
        }

        string evidenceQuote = EvidenceQuote?.Trim() ?? string.Empty;
        string evidenceSummary = EvidenceSummary?.Trim() ?? string.Empty;
        string reason = Reason?.Trim() ?? string.Empty;

        if (evidenceQuote.Length > 1200 ||
            evidenceSummary.Length > 1600 ||
            reason.Length > 1600)
        {
            throw new InvalidOperationException(
                "Goal proposal evidence/reason is too long.");
        }

        return this with
        {
            GoalId = string.IsNullOrWhiteSpace(GoalId) ? null : GoalId.Trim(),
            Objective = objective,
            Priority = Priority.HasValue
                ? Math.Clamp(Priority.Value, 0.0, 1.0)
                : null,
            LinkedCommitmentId = string.IsNullOrWhiteSpace(LinkedCommitmentId)
                ? null
                : LinkedCommitmentId.Trim(),
            CompletionCriteria = SegaGoalState.NormalizeStrings(
                CompletionCriteria,
                12,
                500),
            DependsOnGoalIds = SegaGoalState.NormalizeStrings(
                DependsOnGoalIds,
                24,
                80),
            WaitingFor = SegaGoalState.NormalizeOptional(WaitingFor, 1000),
            Blocker = SegaGoalState.NormalizeOptional(Blocker, 1000),
            EvidenceQuote = evidenceQuote,
            EvidenceSummary = evidenceSummary,
            Reason = reason,
            Confidence = Math.Clamp(Confidence, 0.0, 1.0)
        };
    }

    public string BuildSignature()
    {
        SegaGoalProposal normalized = Normalize();

        return string.Join(
            '|',
            normalized.Action,
            normalized.GoalId ?? string.Empty,
            normalized.Objective.ToLowerInvariant(),
            normalized.EvidenceQuote.ToLowerInvariant(),
            normalized.NextWakeAtUtc?.ToUniversalTime().ToString("O") ?? string.Empty);
    }
}


public sealed record SegaGoalEvidenceRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid GoalId { get; init; }

    public Guid? SourceEventId { get; init; }

    public string? SourceEventName { get; init; }

    public SegaGoalEvidenceSource Source { get; init; }

    public string Quote { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public string Reason { get; init; } = string.Empty;

    public DateTimeOffset RecordedAt { get; init; } = DateTimeOffset.UtcNow;
}


public enum SegaGoalApplyAction
{
    Rejected,
    Created,
    Transitioned,
    Revised,
    EvidenceRecorded,
    Duplicate,
    NoChange
}


public sealed record SegaGoalApplyResult
{
    public SegaGoalApplyAction Action { get; init; }

    public SegaGoalState? Goal { get; init; }

    public string Reason { get; init; } = string.Empty;

    public bool Changed =>
        Action is SegaGoalApplyAction.Created
            or SegaGoalApplyAction.Transitioned
            or SegaGoalApplyAction.Revised
            or SegaGoalApplyAction.EvidenceRecorded;
}