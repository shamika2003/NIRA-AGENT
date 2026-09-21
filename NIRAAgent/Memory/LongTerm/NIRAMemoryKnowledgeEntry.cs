/*
 * filename: NIRAMemoryKnowledgeEntry.cs
 */

namespace NIRAAgent.Memory.LongTerm;

public sealed record NIRAMemoryKnowledgeEntry
{
    public NIRAMemoryRecord Memory
    {
        get;
        init;
    } =
        null!;


    public NIRAMemoryAssociationProfile Association
    {
        get;
        init;
    } =
        new();
}

