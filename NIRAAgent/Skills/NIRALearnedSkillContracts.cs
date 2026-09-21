/*
 * filename: NIRALearnedSkillContracts.cs
 */

using System.Text;

namespace NIRAAgent.Skills;

public enum NIRALearnedSkillScope
{
    ProjectSpecific,
    Generalized
}

public enum NIRALearnedSkillStatus
{
    Emerging,
    Proven,
    Deprecated,
    Retired
}

public enum NIRALearnedSkillProposalAction
{
    Create,
    Revise,
    Deprecate,
    Retire
}

public enum NIRALearnedSkillApplyAction
{
    Rejected,
    Created,
    Revised,
    Deprecated,
    Retired,
    NoChange
}

public enum NIRALearnedSkillCandidateState
{
    WaitingForEvidence,
    CreateEligible,
    Current,
    CurrentFailureReview,
    StaleWaitingForEvidence,
    RevisionEligible,
    SourceUnavailable,
    SourceInactive
}

public sealed record NIRALearnedSkillFailurePatternProposal
{
    public string Pattern { get; init; } = string.Empty;
    public string EvidenceQuote { get; init; } = string.Empty;
    public int SourceToolVersion { get; init; }

    public NIRALearnedSkillFailurePatternProposal Normalize()
    {
        string pattern = Pattern?.Trim() ?? string.Empty;
        string quote = EvidenceQuote?.Trim() ?? string.Empty;
        if (pattern.Length == 0 || pattern.Length > 700)
            throw new InvalidOperationException("Learned skill failure pattern must be 1-700 characters.");
        if (quote.Length == 0 || quote.Length > 900)
            throw new InvalidOperationException("Learned skill failure evidence quote must be 1-900 characters.");
        if (SourceToolVersion <= 0)
            throw new InvalidOperationException("Learned skill failure pattern requires an exact positive source tool version.");
        return this with
        {
            Pattern = pattern,
            EvidenceQuote = quote,
            SourceToolVersion = SourceToolVersion
        };
    }
}

public sealed record NIRALearnedSkillFailurePatternEvidence
{
    public string Pattern { get; init; } = string.Empty;
    public string EvidenceQuote { get; init; } = string.Empty;
    public int SourceToolVersion { get; init; }
    public DateTimeOffset GroundedAtUtc { get; init; }

    public NIRALearnedSkillFailurePatternEvidence Normalize()
    {
        string pattern = Pattern?.Trim() ?? string.Empty;
        string quote = EvidenceQuote?.Trim() ?? string.Empty;
        if (pattern.Length == 0 || pattern.Length > 700)
            throw new InvalidOperationException("Learned skill failure pattern must be 1-700 characters.");
        if (quote.Length == 0 || quote.Length > 900)
            throw new InvalidOperationException("Learned skill failure evidence quote must be 1-900 characters.");
        return this with
        {
            Pattern = pattern,
            EvidenceQuote = quote,
            SourceToolVersion = Math.Max(1, SourceToolVersion),
            GroundedAtUtc = GroundedAtUtc == default ? DateTimeOffset.UtcNow : GroundedAtUtc
        };
    }
}

public sealed record NIRALearnedSkillEvidenceRecord
{
    public Guid EvidenceId { get; init; }
    public Guid SkillId { get; init; }
    public Guid SourceToolId { get; init; }
    public int SourceToolVersion { get; init; }
    public bool Succeeded { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string FailureReason { get; init; } = string.Empty;
    public DateTimeOffset ObservedAtUtc { get; init; }
}

public sealed record NIRALearnedSkillReuseRecord
{
    public Guid ReuseId { get; init; }
    public Guid SkillId { get; init; }
    public Guid SourceToolId { get; init; }
    public int SourceToolVersion { get; init; }
    public bool Succeeded { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string FailureReason { get; init; } = string.Empty;
    public DateTimeOffset ReusedAtUtc { get; init; }
}

public sealed record NIRALearnedSkillDefinition
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int Version { get; init; } = 1;
    public NIRALearnedSkillScope Scope { get; init; } = NIRALearnedSkillScope.ProjectSpecific;
    public NIRALearnedSkillStatus Status { get; init; } = NIRALearnedSkillStatus.Emerging;
    public Guid SourceToolId { get; init; }
    public int SourceToolVersion { get; init; }
    public IReadOnlyList<string> Preconditions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ExpectedOutcomes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> VerificationMethods { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> KnownFailurePatterns { get; init; } = Array.Empty<string>();
    public IReadOnlyList<NIRALearnedSkillFailurePatternEvidence> FailurePatternEvidence { get; init; } = Array.Empty<NIRALearnedSkillFailurePatternEvidence>();
    public double Confidence { get; init; }
    public string LearningReason { get; init; } = string.Empty;
    public DateTimeOffset LastSemanticReviewAtUtc { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }

    public NIRALearnedSkillDefinition Normalize()
    {
        string name = NormalizeRequired(Name, 120, "Learned skill name");
        string description = NormalizeRequired(Description, 2000, "Learned skill description");
        string reason = NormalizeRequired(LearningReason, 1800, "Learned skill reason");
        DateTimeOffset now = DateTimeOffset.UtcNow;

        return this with
        {
            Name = name,
            Description = description,
            Version = Math.Max(1, Version),
            SourceToolVersion = Math.Max(1, SourceToolVersion),
            Preconditions = NormalizeList(Preconditions, 16, 700),
            ExpectedOutcomes = NormalizeList(ExpectedOutcomes, 16, 700),
            VerificationMethods = NormalizeList(VerificationMethods, 16, 700),
            FailurePatternEvidence = NormalizeFailurePatternEvidence(FailurePatternEvidence),
            KnownFailurePatterns = NormalizeList(
                (KnownFailurePatterns ?? Array.Empty<string>())
                    .Concat((FailurePatternEvidence ?? Array.Empty<NIRALearnedSkillFailurePatternEvidence>())
                        .Where(value => value != null)
                        .Select(value => value.Pattern)),
                16,
                700),
            Confidence = Math.Clamp(Confidence, 0.0, 1.0),
            LearningReason = reason,
            LastSemanticReviewAtUtc = LastSemanticReviewAtUtc == default
                ? (UpdatedAtUtc == default ? now : UpdatedAtUtc)
                : LastSemanticReviewAtUtc,
            CreatedAtUtc = CreatedAtUtc == default ? now : CreatedAtUtc,
            UpdatedAtUtc = UpdatedAtUtc == default ? now : UpdatedAtUtc
        };
    }

    private static IReadOnlyList<NIRALearnedSkillFailurePatternEvidence> NormalizeFailurePatternEvidence(
        IReadOnlyList<NIRALearnedSkillFailurePatternEvidence>? values)
    {
        List<NIRALearnedSkillFailurePatternEvidence> result = new();
        foreach (NIRALearnedSkillFailurePatternEvidence value in values ?? Array.Empty<NIRALearnedSkillFailurePatternEvidence>())
        {
            if (value == null) continue;
            NIRALearnedSkillFailurePatternEvidence normalized = value.Normalize();
            if (result.Any(existing =>
                    string.Equals(existing.Pattern, normalized.Pattern, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(existing.EvidenceQuote, normalized.EvidenceQuote, StringComparison.OrdinalIgnoreCase) &&
                    existing.SourceToolVersion == normalized.SourceToolVersion))
                continue;
            result.Add(normalized);
            if (result.Count >= 16) break;
        }
        return result;
    }

    private static string NormalizeRequired(string? value, int maximumLength, string label)
    {
        string clean = value?.Trim() ?? string.Empty;
        if (clean.Length == 0) throw new InvalidOperationException($"{label} is required.");
        if (clean.Length > maximumLength) throw new InvalidOperationException($"{label} is too long.");
        return clean;
    }

    private static IReadOnlyList<string> NormalizeList(
        IEnumerable<string>? values,
        int maximumItems,
        int maximumLength)
    {
        return (values ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Where(value => value.Length <= maximumLength)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maximumItems)
            .ToArray();
    }
}

public sealed record NIRALearnedSkillRecord
{
    public required NIRALearnedSkillDefinition Definition { get; init; }
    public int SourceExecutionCount { get; init; }
    public int SourceSuccessCount { get; init; }
    public int SourceFailureCount { get; init; }
    public string LastEvidenceSummary { get; init; } = string.Empty;
    public DateTimeOffset? LastObservedAtUtc { get; init; }

    public double SourceReliability => SourceExecutionCount <= 0
        ? 0.0
        : Math.Clamp((double)SourceSuccessCount / SourceExecutionCount, 0.0, 1.0);
}

public sealed record NIRALearnedSkillFormationProposal
{
    public NIRALearnedSkillProposalAction Action { get; init; }
    public string? SkillId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public NIRALearnedSkillScope Scope { get; init; } = NIRALearnedSkillScope.ProjectSpecific;
    public string? SourceToolId { get; init; }
    public int SourceToolVersion { get; init; }
    public IReadOnlyList<string> Preconditions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ExpectedOutcomes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> VerificationMethods { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> KnownFailurePatterns { get; init; } = Array.Empty<string>();
    public IReadOnlyList<NIRALearnedSkillFailurePatternProposal> FailurePatterns { get; init; } = Array.Empty<NIRALearnedSkillFailurePatternProposal>();
    public string Reason { get; init; } = string.Empty;
    public double Confidence { get; init; }

    public NIRALearnedSkillFormationProposal Normalize()
    {
        string? skillId = string.IsNullOrWhiteSpace(SkillId) ? null : SkillId.Trim();
        string? toolId = string.IsNullOrWhiteSpace(SourceToolId) ? null : SourceToolId.Trim();
        string reason = Reason?.Trim() ?? string.Empty;
        if (reason.Length > 1800) throw new InvalidOperationException("Learned skill proposal reason is too long.");

        return this with
        {
            SkillId = skillId,
            Name = Name?.Trim() ?? string.Empty,
            Description = Description?.Trim() ?? string.Empty,
            SourceToolId = toolId,
            SourceToolVersion = Math.Max(0, SourceToolVersion),
            Preconditions = NormalizeList(Preconditions),
            ExpectedOutcomes = NormalizeList(ExpectedOutcomes),
            VerificationMethods = NormalizeList(VerificationMethods),
            KnownFailurePatterns = NormalizeList(KnownFailurePatterns),
            FailurePatterns = NormalizeFailurePatterns(FailurePatterns),
            Reason = reason,
            Confidence = Math.Clamp(Confidence, 0.0, 1.0)
        };
    }

    public string BuildSignature()
    {
        NIRALearnedSkillFormationProposal normalized = Normalize();
        StringBuilder text = new();
        text.Append(normalized.Action).Append('|')
            .Append(normalized.SkillId ?? string.Empty).Append('|')
            .Append(normalized.Name.ToLowerInvariant()).Append('|')
            .Append(normalized.SourceToolId ?? string.Empty).Append('|')
            .Append(normalized.SourceToolVersion).Append('|')
            .Append(string.Join(";", normalized.FailurePatterns.Select(value =>
                $"{value.SourceToolVersion}:{value.Pattern.ToLowerInvariant()}:{value.EvidenceQuote.ToLowerInvariant()}"))).Append('|')
            .Append(normalized.Reason.ToLowerInvariant());
        return text.ToString();
    }

    private static IReadOnlyList<NIRALearnedSkillFailurePatternProposal> NormalizeFailurePatterns(
        IReadOnlyList<NIRALearnedSkillFailurePatternProposal>? values) =>
        (values ?? Array.Empty<NIRALearnedSkillFailurePatternProposal>())
            .Where(value => value != null)
            .Select(value => value.Normalize())
            .GroupBy(value => $"{value.SourceToolVersion}|{value.Pattern}|{value.EvidenceQuote}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(16)
            .ToArray();

    private static IReadOnlyList<string> NormalizeList(IReadOnlyList<string>? values) =>
        (values ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(16)
            .ToArray();
}

public sealed record NIRALearnedSkillMutationResult
{
    public NIRALearnedSkillApplyAction Action { get; init; }
    public NIRALearnedSkillDefinition? Skill { get; init; }
    public string Reason { get; init; } = string.Empty;
    public bool Changed => Action is NIRALearnedSkillApplyAction.Created
        or NIRALearnedSkillApplyAction.Revised
        or NIRALearnedSkillApplyAction.Deprecated
        or NIRALearnedSkillApplyAction.Retired;
}

