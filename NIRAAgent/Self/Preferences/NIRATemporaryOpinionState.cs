/*
 * filename: NIRATemporaryOpinionState.cs
 */

namespace NIRAAgent.Self.Preferences;


// =============================================================
// TEMPORARY OPINION STATE
//
// This represents NIRA's current, experience-sensitive opinion
// about a subject.
//
// It is intentionally different from a durable learned
// preference:
//
// - it may become strong after one meaningful experience,
// - it naturally decays toward neutral,
// - it may temporarily disagree with NIRA's durable preference,
// - it does not become durable merely because it exists.
//
// BaseAffinity is the opinion strength at LastExperiencedAt.
// ResolveSnapshot applies time decay without mutating the stored
// record, so no polling loop is required.
// =============================================================

public sealed record NIRATemporaryOpinionState
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


    public double BaseAffinity
    {
        get;
        init;
    }


    public double Confidence
    {
        get;
        init;
    }


    public double HalfLifeHours
    {
        get;
        init;
    }


    public int ExperienceCount
    {
        get;
        init;
    }


    public DateTimeOffset CreatedAt
    {
        get;
        init;
    }


    public DateTimeOffset LastExperiencedAt
    {
        get;
        init;
    }


    public NIRATemporaryOpinionState Normalize()
    {
        if (string.IsNullOrWhiteSpace(
                Key))
        {
            throw new InvalidOperationException(
                "NIRA temporary opinion requires a key.");
        }


        if (string.IsNullOrWhiteSpace(
                Subject))
        {
            throw new InvalidOperationException(
                "NIRA temporary opinion requires a subject.");
        }


        DateTimeOffset now =
            DateTimeOffset.UtcNow;


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

            BaseAffinity =
                Math.Clamp(
                    BaseAffinity,
                    -1.0,
                    1.0),

            Confidence =
                Math.Clamp(
                    Confidence,
                    0.0,
                    1.0),

            HalfLifeHours =
                Math.Clamp(
                    HalfLifeHours,
                    2.0,
                    96.0),

            ExperienceCount =
                Math.Max(
                    1,
                    ExperienceCount),

            CreatedAt =
                CreatedAt == default
                    ? now
                    : CreatedAt,

            LastExperiencedAt =
                LastExperiencedAt == default
                    ? now
                    : LastExperiencedAt
        };
    }


    // =========================================================
    // RESOLVE CURRENT EFFECTIVE OPINION
    // =========================================================

    public NIRATemporaryOpinionSnapshot ResolveSnapshot(
        DateTimeOffset now)
    {
        NIRATemporaryOpinionState normalized =
            Normalize();


        if (now <
            normalized.LastExperiencedAt)
        {
            now =
                normalized.LastExperiencedAt;
        }


        double elapsedHours =
            Math.Max(
                0.0,
                (
                    now -
                    normalized.LastExperiencedAt
                )
                .TotalHours);


        double decayFactor =
            Math.Pow(
                0.5,
                elapsedHours /
                    normalized.HalfLifeHours);


        double effectiveAffinity =
            normalized.BaseAffinity
            *
            decayFactor;


        // Confidence decays more slowly than momentary affinity.
        // NIRA may still remember that the opinion had a real
        // basis even after the immediate feeling has softened.
        double confidenceDecay =
            Math.Sqrt(
                decayFactor);


        double effectiveConfidence =
            normalized.Confidence
            *
            confidenceDecay;


        return new NIRATemporaryOpinionSnapshot(
            normalized.Key,
            normalized.Subject,
            normalized.TopicKey,
            Math.Clamp(
                effectiveAffinity,
                -1.0,
                1.0),
            Math.Clamp(
                effectiveConfidence,
                0.0,
                1.0),
            normalized.ExperienceCount,
            normalized.HalfLifeHours,
            normalized.LastExperiencedAt,
            now);
    }
}


// =============================================================
// EFFECTIVE SNAPSHOT
// =============================================================

public readonly record struct NIRATemporaryOpinionSnapshot(
    string Key,
    string Subject,
    string? TopicKey,
    double Affinity,
    double Confidence,
    int ExperienceCount,
    double HalfLifeHours,
    DateTimeOffset LastExperiencedAt,
    DateTimeOffset ResolvedAt)
{
    public bool IsActive =>
        Math.Abs(
            Affinity) >=
            0.08
        &&
        Confidence >=
            0.30;
}

