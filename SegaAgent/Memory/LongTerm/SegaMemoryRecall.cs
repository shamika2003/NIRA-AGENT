/*
 * filename: SegaMemoryRecall.cs
 */

namespace SegaAgent.Memory.LongTerm;


// =============================================================
// MEMORY RECALL
//
// Similarity is pure semantic similarity.
//
// Score is the final retrieval rank after combining semantic
// relevance with durable-memory importance/confidence/recency.
// =============================================================

public sealed record SegaMemoryRecall
{
    public SegaMemoryRecord Memory
    {
        get;
        init;
    } =
        null!;


    public double Similarity
    {
        get;
        init;
    }


    public double Score
    {
        get;
        init;
    }


    public TimeSpan Age
    {
        get;
        init;
    }
}
