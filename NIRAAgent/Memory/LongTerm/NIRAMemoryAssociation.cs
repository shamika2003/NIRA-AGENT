/*
 * filename: NIRAMemoryAssociation.cs
 */

namespace NIRAAgent.Memory.LongTerm;

public enum NIRAMemoryAssociationTermKind
{
    RetrievalCue = 0,
    Concept = 1,
    Entity = 2
}


public enum NIRAMemoryRelationKind
{
    SharedTopic = 0,
    SharedConcept = 1,
    SharedEntity = 2
}


public sealed record NIRAMemoryAssociationLink
{
    public Guid SourceMemoryId
    {
        get;
        init;
    }


    public Guid TargetMemoryId
    {
        get;
        init;
    }


    public NIRAMemoryRelationKind RelationKind
    {
        get;
        init;
    }


    public string SharedValue
    {
        get;
        init;
    } =
        string.Empty;


    public double Strength
    {
        get;
        init;
    }
}


internal sealed record NIRAStoredMemoryAssociationProfile(
    Guid MemoryId,
    NIRAMemoryAssociationProfile Profile,
    NIRAAgent.Semantic.SemanticEmbedding RetrievalEmbedding);

