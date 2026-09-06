/*
 * filename: SegaMemorySearchResult.cs
 */

namespace SegaAgent.Memory.LongTerm;

public sealed record SegaMemorySearchResult
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


    // Raw similarity to authoritative memory Content.
    public double Similarity
    {
        get;
        init;
    }


    // Raw similarity to the memory's associative retrieval text.
    public double RetrievalSimilarity
    {
        get;
        init;
    }


    // Lexical affinity across content + associative profile.
    public double LexicalAffinity
    {
        get;
        init;
    }


    public double MetadataAffinity
    {
        get;
        init;
    }


    // Affinity to requested concepts/entities.
    public double AssociationAffinity
    {
        get;
        init;
    }


    public double Salience
    {
        get;
        init;
    }


    // Candidate-generation ranking score only.
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


    public IReadOnlyList<string> AssociationReasons
    {
        get;
        init;
    } =
        Array.Empty<string>();


    public bool IsAssociationExpansion =>
        AssociationReasons.Count >
        0;
}
