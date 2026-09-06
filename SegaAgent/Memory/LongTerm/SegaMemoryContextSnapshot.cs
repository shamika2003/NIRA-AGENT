/*
 * filename: SegaMemoryContextSnapshot.cs
 */

namespace SegaAgent.Memory.LongTerm;

public enum SegaMemoryContextMode
{
    Full,

    Map
}


// =============================================================
// MEMORY CONTEXT SNAPSHOT
//
// Full:
//   Every active durable memory fits safely inside the current
//   cognition budget and is supplied directly.
//
// Map:
//   The complete memory set is too large for the current
//   cognition budget. Cognition receives a compact navigation
//   map and may explicitly request deeper memory searches.
//
// This is a context-budget decision, not a relevance decision.
// =============================================================

public sealed record SegaMemoryContextSnapshot
{
    public SegaMemoryContextMode Mode
    {
        get;
        init;
    }


    public int ActiveCount
    {
        get;
        init;
    }


    public int CharacterBudget
    {
        get;
        init;
    }


    public int EstimatedFullCharacters
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
}
