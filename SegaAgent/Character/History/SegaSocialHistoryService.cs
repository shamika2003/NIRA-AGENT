/*
 * filename: SegaSocialHistoryService.cs
 */

using System.Diagnostics;

namespace SegaAgent.Character.History;

public sealed class SegaSocialHistoryService
{
    // =========================================================
    // CONFIGURATION
    //
    // This is short-term runtime social history.
    //
    // Long-term memory will be a different system.
    // =========================================================

    private const int MaximumEvents =
        200;


    // =========================================================
    // STATE
    // =========================================================

    private readonly object _sync =
        new();


    private readonly List<
        SegaSocialEvent>
        _events =
            new();


    private long _sequence;


    private long _version;


    private DateTimeOffset _updatedAt =
        DateTimeOffset.UtcNow;


    // =========================================================
    // EVENT
    // =========================================================

    public event Action<SegaSocialHistorySnapshot>?
        HistoryChanged;

    // =========================================================
    // EVENT RECORDED
    //
    // Fired once for every new social event.
    //
    // Consumers can observe new events without coupling
    // themselves to the components that originally produced
    // those events.
    // =========================================================

    public event Action<SegaSocialEvent>?
        EventRecorded;


    // =========================================================
    // SNAPSHOT
    // =========================================================

    public SegaSocialHistorySnapshot Current
    {
        get
        {
            lock (_sync)
            {
                return BuildSnapshot();
            }
        }
    }


    // =========================================================
    // RECORD EVENT
    // =========================================================

    public SegaSocialEvent Record(
        SegaSocialEventSource source,
        SegaSocialEventKind kind,
        string eventName,
        string topicKey,
        string content,
        IReadOnlyDictionary<
            string,
            string>? metadata = null)
    {
        eventName =
            NormalizeRequired(
                eventName,
                nameof(eventName));


        topicKey =
            NormalizeRequired(
                topicKey,
                nameof(topicKey));


        content =
            content?.Trim()
            ?? string.Empty;


        SegaSocialEvent socialEvent;

        SegaSocialHistorySnapshot snapshot;


        lock (_sync)
        {
            socialEvent =
                new SegaSocialEvent
                {
                    Id =
                        Guid.NewGuid(),

                    Sequence =
                        ++_sequence,

                    Timestamp =
                        DateTimeOffset.UtcNow,

                    Source =
                        source,

                    Kind =
                        kind,

                    EventName =
                        eventName,

                    TopicKey =
                        topicKey,

                    Content =
                        content,

                    Metadata =
                        CopyMetadata(
                            metadata)
                };


            _events.Add(
                socialEvent);


            TrimHistory();


            _version++;


            _updatedAt =
                socialEvent.Timestamp;


            snapshot =
                BuildSnapshot();
        }


        PublishChanged(
            snapshot);


        PublishEventRecorded(
            socialEvent);


        Debug.WriteLine(
            $"[SocialHistory] " +
            $"#{socialEvent.Sequence} | " +
            $"{socialEvent.Source} | " +
            $"{socialEvent.Kind} | " +
            $"Event='{socialEvent.EventName}' | " +
            $"Topic='{socialEvent.TopicKey}'");


        return socialEvent;
    }


    // =========================================================
    // GET RECENT
    // =========================================================

    public IReadOnlyList<
        SegaSocialEvent>
        GetRecent(
            int maximumCount)
    {
        if (maximumCount <= 0)
        {
            return Array.Empty<
                SegaSocialEvent>();
        }


        lock (_sync)
        {
            int count =
                Math.Min(
                    maximumCount,
                    _events.Count);


            if (count == 0)
            {
                return Array.Empty<
                    SegaSocialEvent>();
            }


            int start =
                _events.Count -
                count;


            return _events
                .GetRange(
                    start,
                    count)
                .ToArray();
        }
    }


    // =========================================================
    // GET EVENTS SINCE
    // =========================================================

    public IReadOnlyList<
        SegaSocialEvent>
        GetSince(
            DateTimeOffset since)
    {
        lock (_sync)
        {
            return _events
                .Where(
                    e =>
                        e.Timestamp >=
                        since)
                .ToArray();
        }
    }


    // =========================================================
    // COUNT RECENT TOPIC OCCURRENCES
    //
    // Future attention logic can use this to understand:
    //
    // "This happened six times recently."
    // =========================================================

    public int CountRecentTopicOccurrences(
        string topicKey,
        TimeSpan window)
    {
        if (string.IsNullOrWhiteSpace(
                topicKey))
        {
            return 0;
        }


        if (window <=
            TimeSpan.Zero)
        {
            return 0;
        }


        DateTimeOffset threshold =
            DateTimeOffset.UtcNow -
            window;


        lock (_sync)
        {
            return _events.Count(
                e =>
                    e.Timestamp >=
                        threshold
                    &&
                    string.Equals(
                        e.TopicKey,
                        topicKey,
                        StringComparison.OrdinalIgnoreCase));
        }
    }


    // =========================================================
    // HAS RECENT EVENT
    // =========================================================

    public bool HasRecentEvent(
        SegaSocialEventKind kind,
        string topicKey,
        TimeSpan window)
    {
        if (string.IsNullOrWhiteSpace(
                topicKey))
        {
            return false;
        }


        if (window <=
            TimeSpan.Zero)
        {
            return false;
        }


        DateTimeOffset threshold =
            DateTimeOffset.UtcNow -
            window;


        lock (_sync)
        {
            return _events.Any(
                e =>
                    e.Timestamp >=
                        threshold
                    &&
                    e.Kind ==
                        kind
                    &&
                    string.Equals(
                        e.TopicKey,
                        topicKey,
                        StringComparison.OrdinalIgnoreCase));
        }
    }


    // =========================================================
    // GET LATEST TOPIC EVENT
    // =========================================================

    public SegaSocialEvent?
        GetLatestForTopic(
            string topicKey)
    {
        if (string.IsNullOrWhiteSpace(
                topicKey))
        {
            return null;
        }


        lock (_sync)
        {
            for (
                int i =
                    _events.Count - 1;

                i >= 0;

                i--)
            {
                SegaSocialEvent socialEvent =
                    _events[i];


                if (string.Equals(
                        socialEvent.TopicKey,
                        topicKey,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return socialEvent;
                }
            }
        }


        return null;
    }


    // =========================================================
    // GET LATEST BY KIND
    // =========================================================

    public SegaSocialEvent?
        GetLatest(
            SegaSocialEventKind kind)
    {
        lock (_sync)
        {
            for (
                int i =
                    _events.Count - 1;

                i >= 0;

                i--)
            {
                if (_events[i].Kind ==
                    kind)
                {
                    return _events[i];
                }
            }
        }


        return null;
    }


    // =========================================================
    // CLEAR
    //
    // Runtime history only.
    //
    // This does not reset:
    //
    // relationship
    // mood
    // future long-term memory
    // =========================================================

    public void Clear()
    {
        SegaSocialHistorySnapshot snapshot;


        lock (_sync)
        {
            if (_events.Count ==
                0)
            {
                return;
            }


            _events.Clear();


            _version++;


            _updatedAt =
                DateTimeOffset.UtcNow;


            snapshot =
                BuildSnapshot();
        }


        PublishChanged(
            snapshot);
    }


    // =========================================================
    // TRIM
    // =========================================================

    private void TrimHistory()
    {
        int excess =
            _events.Count -
            MaximumEvents;


        if (excess <= 0)
        {
            return;
        }


        _events.RemoveRange(
            0,
            excess);
    }


    // =========================================================
    // SNAPSHOT
    // =========================================================

    private SegaSocialHistorySnapshot
        BuildSnapshot()
    {
        return new SegaSocialHistorySnapshot
        {
            Version =
                _version,

            UpdatedAt =
                _updatedAt,

            Events =
                _events.ToArray()
        };
    }


    // =========================================================
    // METADATA COPY
    // =========================================================

    private static IReadOnlyDictionary<
        string,
        string>
        CopyMetadata(
            IReadOnlyDictionary<
                string,
                string>? metadata)
    {
        if (metadata ==
            null)
        {
            return new Dictionary<
                string,
                string>();
        }


        return new Dictionary<
            string,
            string>(
                metadata,
                StringComparer.OrdinalIgnoreCase);
    }


    // =========================================================
    // NORMALIZE REQUIRED
    // =========================================================

    private static string NormalizeRequired(
        string value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            throw new ArgumentException(
                "Value cannot be empty.",
                parameterName);
        }


        return value.Trim();
    }

    // =========================================================
    // PUBLISH EVENT RECORDED
    // =========================================================

    private void PublishEventRecorded(
        SegaSocialEvent socialEvent)
    {
        Action<SegaSocialEvent>?
            handlers =
                EventRecorded;


        if (handlers ==
            null)
        {
            return;
        }


        foreach (
            Action<SegaSocialEvent> handler
            in handlers.GetInvocationList())
        {
            try
            {
                handler(
                    socialEvent);
            }
            catch
            {
                /*
                 * An observer must never be able to break
                 * social-history recording.
                 */
            }
        }
    }


    // =========================================================
    // PUBLISH
    // =========================================================

    private void PublishChanged(
        SegaSocialHistorySnapshot snapshot)
    {
        Action<SegaSocialHistorySnapshot>?
            handlers =
                HistoryChanged;


        if (handlers ==
            null)
        {
            return;
        }


        foreach (
            Action<SegaSocialHistorySnapshot>
                handler
            in handlers.GetInvocationList())
        {
            try
            {
                handler(
                    snapshot);
            }
            catch
            {
                /*
                 * Social-history consumers must not be able
                 * to break the history service.
                 */
            }
        }
    }
}