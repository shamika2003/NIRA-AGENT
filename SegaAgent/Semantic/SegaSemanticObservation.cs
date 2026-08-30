/*
 * filename: SegaSemanticObservation.cs
 */

using SegaAgent.Character.History;

namespace SegaAgent.Semantic;

public sealed record SegaSemanticMatch
{
    public SegaSocialEvent Event
    {
        get;
        init;
    } = null!;


    public double Similarity
    {
        get;
        init;
    }


    public double RecencyWeight
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


public sealed record SegaSemanticObservation
{
    public Guid EventId
    {
        get;
        init;
    }


    public long EventSequence
    {
        get;
        init;
    }


    public bool Available
    {
        get;
        init;
    }


    public SegaSocialEvent?
        ClosestEvent
    {
        get;
        init;
    }


    public double ClosestSimilarity
    {
        get;
        init;
    }


    public TimeSpan?
        TimeSinceClosestEvent
    {
        get;
        init;
    }


    public double RecurrenceStrength
    {
        get;
        init;
    }


    public IReadOnlyList<
        SegaSemanticMatch>
        RelatedEvents
    {
        get;
        init;
    } =
        Array.Empty<
            SegaSemanticMatch>();


    public static SegaSemanticObservation None(
        SegaSocialEvent socialEvent)
    {
        return new SegaSemanticObservation
        {
            EventId =
                socialEvent.Id,

            EventSequence =
                socialEvent.Sequence,

            Available =
                false
        };
    }
}