/*
 * filename: SegaMemoryKnowledgeEntry.cs
 */

namespace SegaAgent.Memory.LongTerm;

public sealed record SegaMemoryKnowledgeEntry
{
    public SegaMemoryRecord Memory
    {
        get;
        init;
    } =
        null!;


    public SegaMemoryAssociationProfile Association
    {
        get;
        init;
    } =
        new();
}
