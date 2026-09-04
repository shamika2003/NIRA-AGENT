/*
 * filename: SegaMemoryRecord.cs
 */

namespace SegaAgent.Memory.LongTerm;


// =============================================================
// PROVENANCE
// =============================================================

public sealed record SegaMemoryProvenance
{
    public SegaMemorySourceType SourceType
    {
        get;
        init;
    } =
        SegaMemorySourceType.Unknown;


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


    public string? SourceExcerpt
    {
        get;
        init;
    }


    public SegaMemoryProvenance Normalize()
    {
        string? excerpt =
            NormalizeOptionalText(
                SourceExcerpt);


        return this with
        {
            SourceExcerpt =
                excerpt
        };
    }


    private static string? NormalizeOptionalText(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return null;
        }


        return value.Trim();
    }
}


// =============================================================
// DURABLE MEMORY RECORD
//
// This is the authoritative domain representation of one stored
// long-term memory.
//
// The semantic embedding is intentionally NOT exposed here.
// Embeddings are retrieval infrastructure, not Sega's cognitive
// memory content.
// =============================================================

public sealed record SegaMemoryRecord
{
    public Guid Id
    {
        get;
        init;
    }


    public SegaMemoryKind Kind
    {
        get;
        init;
    }


    public string Content
    {
        get;
        init;
    } =
        string.Empty;


    // =========================================================
    // IDENTITY / CONSOLIDATION KEYS
    //
    // CanonicalKey gives future consolidation a stable identity
    // for mutable facts/preferences.
    //
    // Examples:
    // user.preference.primary_language
    // project.sega.embodiment.current_form
    // =========================================================

    public string? CanonicalKey
    {
        get;
        init;
    }


    public string? TopicKey
    {
        get;
        init;
    }


    // =========================================================
    // MEMORY WEIGHTS
    // =========================================================

    public double Importance
    {
        get;
        init;
    }


    public double Confidence
    {
        get;
        init;
    }


    public double EmotionalWeight
    {
        get;
        init;
    }


    // =========================================================
    // LIFECYCLE
    // =========================================================

    public SegaMemoryStatus Status
    {
        get;
        init;
    } =
        SegaMemoryStatus.Active;


    public Guid? SupersededByMemoryId
    {
        get;
        init;
    }


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


    public DateTimeOffset? LastRecalledAt
    {
        get;
        init;
    }


    public int ReinforcementCount
    {
        get;
        init;
    }


    public int RecallCount
    {
        get;
        init;
    }


    // =========================================================
    // PROVENANCE
    // =========================================================

    public SegaMemoryProvenance Provenance
    {
        get;
        init;
    } =
        new();


    // =========================================================
    // NORMALIZE
    // =========================================================

    public SegaMemoryRecord Normalize()
    {
        if (string.IsNullOrWhiteSpace(
                Content))
        {
            throw new InvalidOperationException(
                "Long-term memory content cannot be empty.");
        }


        return this with
        {
            Content =
                Content.Trim(),

            CanonicalKey =
                NormalizeKey(
                    CanonicalKey),

            TopicKey =
                NormalizeKey(
                    TopicKey),

            Importance =
                Math.Clamp(
                    Importance,
                    0.0,
                    1.0),

            Confidence =
                Math.Clamp(
                    Confidence,
                    0.0,
                    1.0),

            EmotionalWeight =
                Math.Clamp(
                    EmotionalWeight,
                    0.0,
                    1.0),

            ReinforcementCount =
                Math.Max(
                    0,
                    ReinforcementCount),

            RecallCount =
                Math.Max(
                    0,
                    RecallCount),

            Provenance =
                (Provenance ?? new SegaMemoryProvenance())
                    .Normalize()
        };
    }


    private static string? NormalizeKey(
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
