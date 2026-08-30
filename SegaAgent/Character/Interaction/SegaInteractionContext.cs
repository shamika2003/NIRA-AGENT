/*
 * filename: SegaInteractionContext.cs
 */

using SegaAgent.Character.History;
using SegaAgent.Semantic;

namespace SegaAgent.Character.Interaction;

public sealed record SegaInteractionContext
{
    public SegaSocialEvent Event
    {
        get;
        init;
    } = null!;


    public int RecentTopicOccurrences
    {
        get;
        init;
    }


    public SegaSocialEvent?
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


    public int RecentSegaResponsesToTopic
    {
        get;
        init;
    }


    public bool SegaAlreadyRespondedRecently =>
        RecentSegaResponsesToTopic >
        0;


    public int RecentUserMessages
    {
        get;
        init;
    }


    public SegaSocialEvent?
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


    public SegaSemanticObservation Semantic
    {
        get;
        init;
    } = null!;


    public double SemanticRecurrence =>
        Semantic.Available
            ? Semantic.RecurrenceStrength
            : 0.0;
}