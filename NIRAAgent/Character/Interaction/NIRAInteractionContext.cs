/*
 * filename: NIRAInteractionContext.cs
 */

using NIRAAgent.Character.History;
using NIRAAgent.Semantic;

namespace NIRAAgent.Character.Interaction;

public sealed record NIRAInteractionContext
{
    public NIRASocialEvent Event
    {
        get;
        init;
    } = null!;


    public int RecentTopicOccurrences
    {
        get;
        init;
    }


    public NIRASocialEvent?
        PreviousTopicEvent
    {
        get;
        init;
    }


    public TimeSpan?
        TimeSincePreviousTopicEvent
    {
        get;
        init;
    }


    public int RecentNIRAResponsesToTopic
    {
        get;
        init;
    }


    public bool NIRAAlreadyRespondedRecently =>
        RecentNIRAResponsesToTopic >
        0;


    public int RecentUserMessages
    {
        get;
        init;
    }


    public NIRASocialEvent?
        PreviousUserMessage
    {
        get;
        init;
    }


    public TimeSpan?
        TimeSincePreviousUserMessage
    {
        get;
        init;
    }


    public NIRASemanticObservation Semantic
    {
        get;
        init;
    } = null!;


    public double SemanticRecurrence =>
        Semantic.Available
            ? Semantic.RecurrenceStrength
            : 0.0;
}
