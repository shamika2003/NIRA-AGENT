/*
 * filename: NIRAMemoryConsolidation.cs
 */

namespace NIRAAgent.Memory.LongTerm;


// =============================================================
// CONSOLIDATION ACTION
// =============================================================

public enum NIRAMemoryConsolidationAction
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

public sealed record NIRAMemoryConsolidationResult
{
    public NIRAMemoryConsolidationAction Action
    {
        get;
        init;
    }


    public NIRAMemoryCandidate Candidate
    {
        get;
        init;
    } =
        null!;


    public NIRAMemoryRecord? Memory
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

