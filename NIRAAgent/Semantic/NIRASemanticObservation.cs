/*
 * filename: NIRASemanticObservation.cs
 */

using NIRAAgent.Character.History;

namespace NIRAAgent.Semantic;

public sealed record NIRASemanticMatch
{
    public NIRASocialEvent Event
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


public sealed record NIRASemanticObservation
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


    public NIRASocialEvent?
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
        NIRASemanticMatch>
        RelatedEvents
    {
        get;
        init;
    } =
        Array.Empty<
            NIRASemanticMatch>();


    public static NIRASemanticObservation None(
        NIRASocialEvent socialEvent)
    {
        return new NIRASemanticObservation
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
