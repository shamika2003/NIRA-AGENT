/*
 * filename: NIRAMemoryEvidence.cs
 */

namespace NIRAAgent.Memory.LongTerm;


// =============================================================
// MEMORY EVIDENCE
//
// Durable memories can be supported by more than one source
// event over time.
//
// The first source remains on NIRAMemoryRecord.Provenance for
// convenient access. Every create/reinforcement/update adds an
// immutable evidence row so provenance is never overwritten.
// =============================================================

public sealed record NIRAMemoryEvidence
{
    public Guid Id
    {
        get;
        init;
    } =
        Guid.NewGuid();


    public Guid MemoryId
    {
        get;
        init;
    }


    public NIRAMemoryProvenance Provenance
    {
        get;
        init;
    } =
        new();


    public DateTimeOffset RecordedAt
    {
        get;
        init;
    } =
        DateTimeOffset.UtcNow;


    public NIRAMemoryEvidence Normalize()
    {
        if (MemoryId ==
            Guid.Empty)
        {
            throw new InvalidOperationException(
                "Memory evidence requires a memory ID.");
        }


        return this with
        {
            Provenance =
                (Provenance ?? new NIRAMemoryProvenance())
                    .Normalize(),

            RecordedAt =
                RecordedAt == default
                    ? DateTimeOffset.UtcNow
                    : RecordedAt
        };
    }
}

