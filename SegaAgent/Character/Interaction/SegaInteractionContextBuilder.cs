/*
 * filename: SegaInteractionContextBuilder.cs
 */

using SegaAgent.Character.History;
using SegaAgent.Semantic;

namespace SegaAgent.Character.Interaction;

public sealed class SegaInteractionContextBuilder
{
    private static readonly TimeSpan
        TopicWindow =
            TimeSpan.FromMinutes(
                10);


    private static readonly TimeSpan
        ResponseWindow =
            TimeSpan.FromMinutes(
                15);


    private static readonly TimeSpan
        UserActivityWindow =
            TimeSpan.FromMinutes(
                10);


    private readonly SegaSocialHistoryService
        _history;


    private readonly SegaSemanticMemoryService
        _semanticMemory;


    public SegaInteractionContextBuilder(
        SegaSocialHistoryService history,
        SegaSemanticMemoryService semanticMemory)
    {
        _history =
            history
            ?? throw new ArgumentNullException(
                nameof(history));


        _semanticMemory =
            semanticMemory
            ?? throw new ArgumentNullException(
                nameof(semanticMemory));
    }


    public SegaInteractionContext Build(
        SegaSocialEvent currentEvent)
    {
        ArgumentNullException.ThrowIfNull(
            currentEvent);


        IReadOnlyList<SegaSocialEvent> events =
            _history
                .Current
                .Events;


        DateTimeOffset topicThreshold =
            currentEvent.Timestamp -
            TopicWindow;


        SegaSocialEvent[] topicEvents =
            events
                .Where(
                    e =>
                        e.Sequence <=
                            currentEvent.Sequence
                        &&
                        e.Timestamp >=
                            topicThreshold
                        &&
                        e.Source ==
                            currentEvent.Source
                        &&
                        e.Kind ==
                            currentEvent.Kind
                        &&
                        string.Equals(
                            e.TopicKey,
                            currentEvent.TopicKey,
                            StringComparison.OrdinalIgnoreCase))
                .OrderBy(
                    e =>
                        e.Sequence)
                .ToArray();


        SegaSocialEvent?
            previousTopicEvent =
                topicEvents
                    .Where(
                        e =>
                            e.Sequence <
                            currentEvent.Sequence)
                    .LastOrDefault();


        TimeSpan?
            timeSincePreviousTopic =
                previousTopicEvent ==
                null
                    ? null
                    : currentEvent.Timestamp -
                      previousTopicEvent.Timestamp;


        DateTimeOffset responseThreshold =
            currentEvent.Timestamp -
            ResponseWindow;


        int recentResponses =
            events.Count(
                e =>
                    e.Sequence <
                        currentEvent.Sequence
                    &&
                    e.Timestamp >=
                        responseThreshold
                    &&
                    e.Source ==
                        SegaSocialEventSource.Sega
                    &&
                    e.Kind ==
                        SegaSocialEventKind.SegaResponse
                    &&
                    string.Equals(
                        e.TopicKey,
                        currentEvent.TopicKey,
                        StringComparison.OrdinalIgnoreCase));


        DateTimeOffset userThreshold =
            currentEvent.Timestamp -
            UserActivityWindow;


        SegaSocialEvent[] userMessages =
            events
                .Where(
                    e =>
                        e.Sequence <=
                            currentEvent.Sequence
                        &&
                        e.Timestamp >=
                            userThreshold
                        &&
                        e.Source ==
                            SegaSocialEventSource.User
                        &&
                        e.Kind ==
                            SegaSocialEventKind.UserMessage)
                .OrderBy(
                    e =>
                        e.Sequence)
                .ToArray();


        SegaSocialEvent?
            previousUserMessage =
                userMessages
                    .Where(
                        e =>
                            e.Sequence <
                            currentEvent.Sequence)
                    .LastOrDefault();


        TimeSpan?
            timeSincePreviousUserMessage =
                previousUserMessage ==
                null
                    ? null
                    : currentEvent.Timestamp -
                      previousUserMessage.Timestamp;


        SegaSemanticObservation semantic =
            _semanticMemory.Observe(
                currentEvent);


        return new SegaInteractionContext
        {
            Event =
                currentEvent,

            RecentTopicOccurrences =
                topicEvents.Length,

            PreviousTopicEvent =
                previousTopicEvent,

            TimeSincePreviousTopicEvent =
                timeSincePreviousTopic,

            RecentSegaResponsesToTopic =
                recentResponses,

            RecentUserMessages =
                userMessages.Length,

            PreviousUserMessage =
                previousUserMessage,

            TimeSincePreviousUserMessage =
                timeSincePreviousUserMessage,

            Semantic =
                semantic
        };
    }
}