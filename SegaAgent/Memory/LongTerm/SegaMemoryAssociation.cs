/*
 * filename: SegaMemoryAssociation.cs
 */

namespace SegaAgent.Memory.LongTerm;

public enum SegaMemoryAssociationTermKind
{
    RetrievalCue = 0,
    Concept = 1,
    Entity = 2
}


public enum SegaMemoryRelationKind
{
    SharedTopic = 0,
    SharedConcept = 1,
    SharedEntity = 2
}


public sealed record SegaMemoryAssociationLink
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


    public SegaMemoryRelationKind RelationKind
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


internal sealed record SegaStoredMemoryAssociationProfile(
    Guid MemoryId,
    SegaMemoryAssociationProfile Profile,
    SegaAgent.Semantic.SemanticEmbedding RetrievalEmbedding);
