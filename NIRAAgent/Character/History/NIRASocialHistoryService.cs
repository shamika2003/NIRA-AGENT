/*
 * filename: NIRASocialHistoryService.cs
 */

using System.Diagnostics;

namespace NIRAAgent.Character.History;

public sealed class NIRASocialHistoryService
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
        NIRASocialEvent>
        _events =
            new();


    private long _sequence;


    private long _version;


    private DateTimeOffset _updatedAt =
        DateTimeOffset.UtcNow;


    // =========================================================
    // EVENT
    // =========================================================

    public event Action<NIRASocialHistorySnapshot>?
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

    public event Action<NIRASocialEvent>?
        EventRecorded;


    // =========================================================
    // SNAPSHOT
    // =========================================================

    public NIRASocialHistorySnapshot Current
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

    public NIRASocialEvent Record(
        NIRASocialEventSource source,
        NIRASocialEventKind kind,
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


        NIRASocialEvent socialEvent;

        NIRASocialHistorySnapshot snapshot;


        lock (_sync)
        {
            socialEvent =
                new NIRASocialEvent
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
        NIRASocialEvent>
        GetRecent(
            int maximumCount)
    {
        if (maximumCount <= 0)
        {
            return Array.Empty<
                NIRASocialEvent>();
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
                    NIRASocialEvent>();
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
        NIRASocialEvent>
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
        NIRASocialEventKind kind,
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

    public NIRASocialEvent?
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
                NIRASocialEvent socialEvent =
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

    public NIRASocialEvent?
        GetLatest(
            NIRASocialEventKind kind)
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
        NIRASocialHistorySnapshot snapshot;


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

    private NIRASocialHistorySnapshot
        BuildSnapshot()
    {
        return new NIRASocialHistorySnapshot
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
        NIRASocialEvent socialEvent)
    {
        Action<NIRASocialEvent>?
            handlers =
                EventRecorded;


        if (handlers ==
            null)
        {
            return;
        }


        foreach (
            Action<NIRASocialEvent> handler
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
        NIRASocialHistorySnapshot snapshot)
    {
        Action<NIRASocialHistorySnapshot>?
            handlers =
                HistoryChanged;


        if (handlers ==
            null)
        {
            return;
        }


        foreach (
            Action<NIRASocialHistorySnapshot>
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
