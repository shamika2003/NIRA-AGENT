/*
 * filename: SegaMemoryEvidence.cs
 */

namespace SegaAgent.Memory.LongTerm;


// =============================================================
// MEMORY EVIDENCE
//
// Durable memories can be supported by more than one source
// event over time.
//
// The first source remains on SegaMemoryRecord.Provenance for
// convenient access. Every create/reinforcement/update adds an
// immutable evidence row so provenance is never overwritten.
// =============================================================

public sealed record SegaMemoryEvidence
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


    public SegaMemoryProvenance Provenance
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


    public SegaMemoryEvidence Normalize()
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
                (Provenance ?? new SegaMemoryProvenance())
                    .Normalize(),

            RecordedAt =
                RecordedAt == default
                    ? DateTimeOffset.UtcNow
                    : RecordedAt
        };
    }
}
