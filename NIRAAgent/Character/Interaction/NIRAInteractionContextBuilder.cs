/*
 * filename: NIRAInteractionContextBuilder.cs
 */

using NIRAAgent.Character.History;
using NIRAAgent.Semantic;

namespace NIRAAgent.Character.Interaction;

public sealed class NIRAInteractionContextBuilder
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


    private readonly NIRASocialHistoryService
        _history;


    private readonly NIRASemanticMemoryService
        _semanticMemory;


    public NIRAInteractionContextBuilder(
        NIRASocialHistoryService history,
        NIRASemanticMemoryService semanticMemory)
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


    public NIRAInteractionContext Build(
        NIRASocialEvent currentEvent)
    {
        ArgumentNullException.ThrowIfNull(
            currentEvent);


        IReadOnlyList<NIRASocialEvent> events =
            _history
                .Current
                .Events;


        DateTimeOffset topicThreshold =
            currentEvent.Timestamp -
            TopicWindow;


        NIRASocialEvent[] topicEvents =
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


        NIRASocialEvent?
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
                        NIRASocialEventSource.NIRA
                    &&
                    e.Kind ==
                        NIRASocialEventKind.NIRAResponse
                    &&
                    string.Equals(
                        e.TopicKey,
                        currentEvent.TopicKey,
                        StringComparison.OrdinalIgnoreCase));


        DateTimeOffset userThreshold =
            currentEvent.Timestamp -
            UserActivityWindow;


        NIRASocialEvent[] userMessages =
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
                            NIRASocialEventSource.User
                        &&
                        e.Kind ==
                            NIRASocialEventKind.UserMessage)
                .OrderBy(
                    e =>
                        e.Sequence)
                .ToArray();


        NIRASocialEvent?
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


        NIRASemanticObservation semantic =
            _semanticMemory.Observe(
                currentEvent);


        return new NIRAInteractionContext
        {
            Event =
                currentEvent,

            RecentTopicOccurrences =
                topicEvents.Length,

            PreviousTopicEvent =
                previousTopicEvent,

            TimeSincePreviousTopicEvent =
                timeSincePreviousTopic,

            RecentNIRAResponsesToTopic =
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
