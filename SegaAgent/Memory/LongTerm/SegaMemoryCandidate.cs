/*
 * filename: SegaMemoryCandidate.cs
 */

namespace SegaAgent.Memory.LongTerm;


// =============================================================
// MEMORY CANDIDATE
//
// A candidate is NOT automatically trusted or persisted merely
// because a model proposed it.
//
// In the next memory stage, the responder will produce candidates
// and the consolidation layer will decide whether to:
//
// create
// reinforce
// update
// supersede
// ignore
//
// Step 1 exposes this type now so the storage/retrieval substrate
// does not need to be redesigned later.
// =============================================================

public sealed record SegaMemoryCandidate
{
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


    public double Importance
    {
        get;
        init;
    } =
        0.50;


    public double Confidence
    {
        get;
        init;
    } =
        0.50;


    public double EmotionalWeight
    {
        get;
        init;
    }


    // =========================================================
    // ASSOCIATIVE RETRIEVAL PROFILE
    //
    // This does not change the authoritative proposition stored
    // in Content. It gives future recall more semantic entry
    // points into the same memory.
    // =========================================================

    public SegaMemoryAssociationProfile Association
    {
        get;
        init;
    } =
        new();


    public SegaMemoryProvenance Provenance
    {
        get;
        init;
    } =
        new();


    public SegaMemoryCandidate Normalize()
    {
        if (string.IsNullOrWhiteSpace(
                Content))
        {
            throw new InvalidOperationException(
                "Memory candidate content cannot be empty.");
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

            Association =
                (Association ?? new SegaMemoryAssociationProfile())
                    .Normalize(),

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
