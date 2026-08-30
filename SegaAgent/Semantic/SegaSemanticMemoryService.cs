/*
 * filename: SegaSemanticMemoryService.cs
 */

using System.Diagnostics;

using SegaAgent.Character.History;

namespace SegaAgent.Semantic;

public sealed class SegaSemanticMemoryService
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


    private readonly ISegaSemanticEncoder
        _encoder;


    private readonly object _sync =
        new();


    private readonly List<
        SemanticMemoryEntry>
        _entries =
            new();


    public SegaSemanticMemoryService(
        ISegaSemanticEncoder encoder)
    {
        _encoder =
            encoder
            ?? throw new ArgumentNullException(
                nameof(encoder));
    }


    public SegaSemanticObservation Observe(
        SegaSocialEvent socialEvent)
    {
        ArgumentNullException.ThrowIfNull(
            socialEvent);


        if (!ShouldEncode(
                socialEvent))
        {
            return SegaSemanticObservation.None(
                socialEvent);
        }


        SemanticEmbedding currentEmbedding =
            _encoder.Encode(
                socialEvent.Content);


        lock (_sync)
        {
            TrimExpired(
                socialEvent.Timestamp);


            List<SegaSemanticMatch> matches =
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
                    SegaSemanticSimilarity
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
                    new SegaSemanticMatch
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


            SegaSemanticMatch[]
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


            SegaSemanticMatch?
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


            return new SegaSemanticObservation
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
        SegaSocialEvent socialEvent)
    {
        if (string.IsNullOrWhiteSpace(
                socialEvent.Content))
        {
            return false;
        }


        return socialEvent.Kind switch
        {
            SegaSocialEventKind.UserMessage =>
                true,

            SegaSocialEventKind.SegaResponse =>
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
            SegaSocialEvent Event,
            SemanticEmbedding Embedding);
}