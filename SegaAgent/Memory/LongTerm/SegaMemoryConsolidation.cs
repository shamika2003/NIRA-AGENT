/*
 * filename: SegaMemoryConsolidation.cs
 */

namespace SegaAgent.Memory.LongTerm;


// =============================================================
// CONSOLIDATION ACTION
// =============================================================

public enum SegaMemoryConsolidationAction
{
    Ignored,

    Created,

    Reinforced,

    Updated,

    Superseded
}


// =============================================================
// CONSOLIDATION RESULT
// =============================================================

public sealed record SegaMemoryConsolidationResult
{
    public SegaMemoryConsolidationAction Action
    {
        get;
        init;
    }


    public SegaMemoryCandidate Candidate
    {
        get;
        init;
    } =
        null!;


    public SegaMemoryRecord? Memory
    {
        get;
        init;
    }


    public Guid? PreviousMemoryId
    {
        get;
        init;
    }


    public double Similarity
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
