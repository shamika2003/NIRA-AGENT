/*
 * filename: NIRASemanticMemoryService.cs
 */

using System.Diagnostics;

using NIRAAgent.Character.History;

namespace NIRAAgent.Semantic;

public sealed class NIRASemanticMemoryService
{
    private const int MaximumEntries =
        160;


    private const int MaximumRelatedEvents =
        6;


    private static readonly TimeSpan
        MemoryWindow =
            TimeSpan.FromMinutes(
                30);


    private static readonly TimeSpan
        RecencyDecay =
            TimeSpan.FromMinutes(
                8);


    private readonly INIRASemanticEncoder
        _encoder;


    private readonly object _sync =
        new();


    private readonly List<
        SemanticMemoryEntry>
        _entries =
            new();


    public NIRASemanticMemoryService(
        INIRASemanticEncoder encoder)
    {
        _encoder =
            encoder
            ?? throw new ArgumentNullException(
                nameof(encoder));
    }


    public NIRASemanticObservation Observe(
        NIRASocialEvent socialEvent)
    {
        ArgumentNullException.ThrowIfNull(
            socialEvent);


        if (!ShouldEncode(
                socialEvent))
        {
            return NIRASemanticObservation.None(
                socialEvent);
        }


        SemanticEmbedding currentEmbedding =
            _encoder.Encode(
                socialEvent.Content);


        lock (_sync)
        {
            TrimExpired(
                socialEvent.Timestamp);


            List<NIRASemanticMatch> matches =
                new();


            double recurrenceMass =
                0.0;


            foreach (
                SemanticMemoryEntry entry
                in _entries)
            {
                if (
                    entry.Event.Source !=
                        socialEvent.Source
                    ||
                    entry.Event.Kind !=
                        socialEvent.Kind)
                {
                    continue;
                }


                TimeSpan age =
                    socialEvent.Timestamp -
                    entry.Event.Timestamp;


                if (age <
                    TimeSpan.Zero)
                {
                    age =
                        TimeSpan.Zero;
                }


                double similarity =
                    NIRASemanticSimilarity
                        .Cosine(
                            currentEmbedding,
                            entry.Embedding);


                double positive =
                    Math.Max(
                        0.0,
                        similarity);


                /*
                 * Sixth-power weighting strongly suppresses
                 * weak topical overlap while keeping the
                 * measurement continuous.
                 *
                 * There is no hard "same message" threshold.
                 */

                double semanticWeight =
                    Math.Pow(
                        positive,
                        6.0);


                double recencyWeight =
                    Math.Exp(
                        -age.TotalSeconds /
                        Math.Max(
                            1.0,
                            RecencyDecay
                                .TotalSeconds));


                recurrenceMass +=
                    semanticWeight *
                    recencyWeight;


                matches.Add(
                    new NIRASemanticMatch
                    {
                        Event =
                            entry.Event,

                        Similarity =
                            similarity,

                        RecencyWeight =
                            recencyWeight,

                        Age =
                            age
                    });
            }


            NIRASemanticMatch[]
                strongest =
                    matches
                        .OrderByDescending(
                            match =>
                                match.Similarity)
                        .ThenBy(
                            match =>
                                match.Age)
                        .Take(
                            MaximumRelatedEvents)
                        .ToArray();


            NIRASemanticMatch?
                closest =
                    strongest
                        .FirstOrDefault();


            double recurrence =
                1.0 -
                Math.Exp(
                    -recurrenceMass);


            recurrence =
                Math.Clamp(
                    recurrence,
                    0.0,
                    1.0);


            _entries.Add(
                new SemanticMemoryEntry(
                    socialEvent,
                    currentEmbedding));


            TrimMaximum();


            Debug.WriteLine(
                $"[SemanticMemory] " +
                $"Event=#{socialEvent.Sequence} | " +
                $"Closest=" +
                $"{closest?.Similarity ?? 0.0:F3} | " +
                $"Recurrence={recurrence:F3}");


            return new NIRASemanticObservation
            {
                EventId =
                    socialEvent.Id,

                EventSequence =
                    socialEvent.Sequence,

                Available =
                    true,

                ClosestEvent =
                    closest?
                        .Event,

                ClosestSimilarity =
                    closest?
                        .Similarity
                    ?? 0.0,

                TimeSinceClosestEvent =
                    closest?
                        .Age,

                RecurrenceStrength =
                    recurrence,

                RelatedEvents =
                    strongest
            };
        }
    }


    private static bool ShouldEncode(
        NIRASocialEvent socialEvent)
    {
        if (string.IsNullOrWhiteSpace(
                socialEvent.Content))
        {
            return false;
        }


        return socialEvent.Kind switch
        {
            NIRASocialEventKind.UserMessage =>
                true,

            NIRASocialEventKind.NIRAResponse =>
                true,

            _ =>
                false
        };
    }


    private void TrimExpired(
        DateTimeOffset now)
    {
        DateTimeOffset threshold =
            now -
            MemoryWindow;


        _entries.RemoveAll(
            entry =>
                entry.Event.Timestamp <
                threshold);
    }


    private void TrimMaximum()
    {
        int excess =
            _entries.Count -
            MaximumEntries;


        if (excess <=
            0)
        {
            return;
        }


        _entries.RemoveRange(
            0,
            excess);
    }


    private sealed record
        SemanticMemoryEntry(
            NIRASocialEvent Event,
            SemanticEmbedding Embedding);
}
