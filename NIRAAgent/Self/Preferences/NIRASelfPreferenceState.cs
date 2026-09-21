/*
 * filename: NIRASelfPreferenceState.cs
 */

namespace NIRAAgent.Self.Preferences;


// =============================================================
// STATUS
// =============================================================

public enum NIRASelfPreferenceStatus
{
    Emerging,

    Established
}


// =============================================================
// APPLY ACTION
// =============================================================

public enum NIRASelfPreferenceApplyAction
{
    Ignored,

    Duplicate,

    Created,

    Updated,

    Established
}


// =============================================================
// PERSISTENT SELF-PREFERENCE
//
// Authoritative current NIRA self-state.
//
// Affinity:
// -1.0 = strong dislike / avoidance
//  0.0 = neutral / unresolved
// +1.0 = strong liking / preference
// =============================================================

public sealed record NIRASelfPreferenceState
{
    public string Key
    {
        get;
        init;
    } =
        string.Empty;


    public string Subject
    {
        get;
        init;
    } =
        string.Empty;


    public string? TopicKey
    {
        get;
        init;
    }


    public double Affinity
    {
        get;
        init;
    }


    public double Confidence
    {
        get;
        init;
    }


    public int ObservationCount
    {
        get;
        init;
    }


    public int ContradictionCount
    {
        get;
        init;
    }


    public NIRASelfPreferenceStatus Status
    {
        get;
        init;
    } =
        NIRASelfPreferenceStatus.Emerging;


    public DateTimeOffset CreatedAt
    {
        get;
        init;
    }


    public DateTimeOffset UpdatedAt
    {
        get;
        init;
    }


    public NIRASelfPreferenceState Normalize()
    {
        if (string.IsNullOrWhiteSpace(
                Key))
        {
            throw new InvalidOperationException(
                "NIRA self-preference requires a key.");
        }


        if (string.IsNullOrWhiteSpace(
                Subject))
        {
            throw new InvalidOperationException(
                "NIRA self-preference requires a subject.");
        }


        return this with
        {
            Key =
                NormalizeRequiredKey(
                    Key),

            Subject =
                Subject.Trim(),

            TopicKey =
                NormalizeOptionalKey(
                    TopicKey),

            Affinity =
                Math.Clamp(
                    Affinity,
                    -1.0,
                    1.0),

            Confidence =
                Math.Clamp(
                    Confidence,
                    0.0,
                    1.0),

            ObservationCount =
                Math.Max(
                    0,
                    ObservationCount),

            ContradictionCount =
                Math.Max(
                    0,
                    ContradictionCount),

            CreatedAt =
                CreatedAt == default
                    ? DateTimeOffset.UtcNow
                    : CreatedAt,

            UpdatedAt =
                UpdatedAt == default
                    ? DateTimeOffset.UtcNow
                    : UpdatedAt
        };
    }


    internal static string NormalizeRequiredKey(
        string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            value);


        return value
            .Trim()
            .ToLowerInvariant();
    }


    internal static string? NormalizeOptionalKey(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return null;
        }


        return value
            .Trim()
            .ToLowerInvariant();
    }
}


// =============================================================
// EXPERIENCE OBSERVATION
//
// One observation is evidence about a possible preference.
// It is deliberately NOT itself durable NIRA self-state.
// =============================================================

public sealed record NIRASelfPreferenceObservation
{
    public string Key
    {
        get;
        init;
    } =
        string.Empty;


    public string Subject
    {
        get;
        init;
    } =
        string.Empty;


    public string? TopicKey
    {
        get;
        init;
    }


    public double Affinity
    {
        get;
        init;
    }


    public double EvidenceStrength
    {
        get;
        init;
    }


    public double Confidence
    {
        get;
        init;
    }


    public Guid? SourceEventId
    {
        get;
        init;
    }


    public long? SourceEventSequence
    {
        get;
        init;
    }


    public DateTimeOffset? SourceTimestamp
    {
        get;
        init;
    }


    public string? EvidenceSummary
    {
        get;
        init;
    }


    public NIRASelfPreferenceObservation Normalize()
    {
        if (string.IsNullOrWhiteSpace(
                Key))
        {
            throw new InvalidOperationException(
                "Self-preference observation requires a key.");
        }


        if (string.IsNullOrWhiteSpace(
                Subject))
        {
            throw new InvalidOperationException(
                "Self-preference observation requires a subject.");
        }


        string? evidence =
            string.IsNullOrWhiteSpace(
                    EvidenceSummary)
                ? null
                : EvidenceSummary.Trim();


        return this with
        {
            Key =
                NIRASelfPreferenceState
                    .NormalizeRequiredKey(
                        Key),

            Subject =
                Subject.Trim(),

            TopicKey =
                NIRASelfPreferenceState
                    .NormalizeOptionalKey(
                        TopicKey),

            Affinity =
                Math.Clamp(
                    Affinity,
                    -1.0,
                    1.0),

            EvidenceStrength =
                Math.Clamp(
                    EvidenceStrength,
                    0.0,
                    1.0),

            Confidence =
                Math.Clamp(
                    Confidence,
                    0.0,
                    1.0),

            EvidenceSummary =
                evidence
        };
    }
}


// =============================================================
// APPLY RESULT
// =============================================================

public sealed record NIRASelfPreferenceApplyResult
{
    public NIRASelfPreferenceApplyAction Action
    {
        get;
        init;
    }


    public NIRASelfPreferenceState? Preference
    {
        get;
        init;
    }


    public string Reason
    {
        get;
        init;
    } =
        string.Empty;
}

