/*
 * filename: SegaLongTermMemoryService.cs
 */

using System.Diagnostics;

using Microsoft.Extensions.Hosting;

using SegaAgent.Semantic;

namespace SegaAgent.Memory.LongTerm;

public sealed class SegaLongTermMemoryService
    : IHostedService
{
    // =========================================================
    // RETRIEVAL CONFIGURATION
    // =========================================================

    private const int DefaultMaximumResults =
        6;


    private const int MaximumAllowedResults =
        20;


    private const double MinimumSemanticSimilarity =
        0.30;


    private static readonly TimeSpan
        RetrievalRecencyHalfLife =
            TimeSpan.FromDays(
                180);


    // =========================================================
    // DEPENDENCIES
    // =========================================================

    private readonly SegaLongTermMemoryStore
        _store;


    private readonly ISegaSemanticEncoder
        _encoder;


    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public SegaLongTermMemoryService(
        SegaLongTermMemoryStore store,
        ISegaSemanticEncoder encoder)
    {
        _store =
            store
            ?? throw new ArgumentNullException(
                nameof(store));


        _encoder =
            encoder
            ?? throw new ArgumentNullException(
                nameof(encoder));
    }


    // =========================================================
    // HOST START
    // =========================================================

    public async Task StartAsync(
        CancellationToken cancellationToken)
    {
        await _store.InitializeAsync(
            cancellationToken);


        long activeCount =
            await _store.CountActiveAsync(
                cancellationToken);


        Debug.WriteLine(
            $"[LongTermMemory] READY | " +
            $"Active={activeCount} | " +
            $"Database='{_store.DatabasePath}'");
    }


    // =========================================================
    // HOST STOP
    // =========================================================

    public Task StopAsync(
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }


    // =========================================================
    // CREATE DURABLE MEMORY
    //
    // IMPORTANT:
    //
    // This is an explicit substrate operation.
    //
    // AgentResponder does NOT call this directly.
    //
    // SegaMemoryConsolidator decides whether a grounded
    // candidate is trustworthy/important enough to reach this
    // method.
    // =========================================================

    public async Task<SegaMemoryRecord> CreateMemoryAsync(
        SegaMemoryCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            candidate);


        SegaMemoryCandidate normalized =
            candidate.Normalize();


        SemanticEmbedding embedding =
            _encoder.Encode(
                normalized.Content);


        return await CreateMemoryAsync(
            normalized,
            embedding,
            cancellationToken);
    }


    // =========================================================
    // CREATE WITH PRECOMPUTED EMBEDDING
    //
    // Consolidation already needs the candidate embedding for
    // duplicate detection. Reuse it instead of encoding twice.
    // =========================================================

    internal async Task<SegaMemoryRecord> CreateMemoryAsync(
        SegaMemoryCandidate candidate,
        SemanticEmbedding embedding,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            candidate);


        ArgumentNullException.ThrowIfNull(
            embedding);


        SegaMemoryCandidate normalized =
            candidate.Normalize();


        DateTimeOffset now =
            DateTimeOffset.UtcNow;


        SegaMemoryRecord memory =
            new SegaMemoryRecord
            {
                Id =
                    Guid.NewGuid(),

                Kind =
                    normalized.Kind,

                Content =
                    normalized.Content,

                CanonicalKey =
                    normalized.CanonicalKey,

                TopicKey =
                    normalized.TopicKey,

                Importance =
                    normalized.Importance,

                Confidence =
                    normalized.Confidence,

                EmotionalWeight =
                    normalized.EmotionalWeight,

                Status =
                    SegaMemoryStatus.Active,

                CreatedAt =
                    now,

                UpdatedAt =
                    now,

                ReinforcementCount =
                    1,

                RecallCount =
                    0,

                Provenance =
                    normalized.Provenance
            }
            .Normalize();


        await _store.InsertAsync(
            memory,
            embedding,
            cancellationToken);


        Debug.WriteLine(
            $"[LongTermMemory] STORED | " +
            $"Id={memory.Id} | " +
            $"Kind={memory.Kind} | " +
            $"Canonical='{memory.CanonicalKey ?? "-"}'");


        return memory;
    }


    // =========================================================
    // RECALL
    //
    // Hybrid ranking:
    //
    // semantic relevance
    // importance
    // confidence
    // emotional weight
    // reinforcement
    // long-term recency
    //
    // The semantic encoder is local MiniLM. No cloud/LLM call
    // is made here.
    // =========================================================

    public async Task<IReadOnlyList<SegaMemoryRecall>> RecallAsync(
        string query,
        int maximumResults = DefaultMaximumResults,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(
                query))
        {
            return Array.Empty<
                SegaMemoryRecall>();
        }


        maximumResults =
            Math.Clamp(
                maximumResults,
                1,
                MaximumAllowedResults);


        await _store.InitializeAsync(
            cancellationToken);


        SemanticEmbedding queryEmbedding =
            _encoder.Encode(
                query.Trim());


        IReadOnlyList<SegaStoredMemory> stored =
            await _store.ReadActiveAsync(
                cancellationToken);


        if (stored.Count ==
            0)
        {
            return Array.Empty<
                SegaMemoryRecall>();
        }


        DateTimeOffset now =
            DateTimeOffset.UtcNow;


        List<SegaMemoryRecall> ranked =
            new(
                stored.Count);


        foreach (SegaStoredMemory storedMemory
                 in stored)
        {
            cancellationToken
                .ThrowIfCancellationRequested();


            double similarity =
                SegaSemanticSimilarity.Cosine(
                    queryEmbedding,
                    storedMemory.Embedding);


            if (similarity <
                MinimumSemanticSimilarity)
            {
                continue;
            }


            SegaMemoryRecord memory =
                storedMemory.Memory;


            TimeSpan age =
                now -
                memory.UpdatedAt;


            if (age <
                TimeSpan.Zero)
            {
                age =
                    TimeSpan.Zero;
            }


            double score =
                CalculateRecallScore(
                    similarity,
                    memory,
                    age);


            ranked.Add(
                new SegaMemoryRecall
                {
                    Memory =
                        memory,

                    Similarity =
                        similarity,

                    Score =
                        score,

                    Age =
                        age
                });
        }


        SegaMemoryRecall[] selected =
            ranked
                .OrderByDescending(
                    recall =>
                        recall.Score)
                .ThenByDescending(
                    recall =>
                        recall.Similarity)
                .ThenByDescending(
                    recall =>
                        recall.Memory.UpdatedAt)
                .Take(
                    maximumResults)
                .ToArray();


        if (selected.Length >
            0)
        {
            await _store.RecordRecallsAsync(
                selected
                    .Select(
                        recall =>
                            recall.Memory.Id)
                    .ToArray(),
                now,
                cancellationToken);
        }


        Debug.WriteLine(
            $"[LongTermMemory] RECALL | " +
            $"Query='{TrimForLog(query)}' | " +
            $"Scanned={stored.Count} | " +
            $"Returned={selected.Length}");


        return selected;
    }


    // =========================================================
    // COUNT
    // =========================================================

    public Task<long> CountActiveAsync(
        CancellationToken cancellationToken = default)
    {
        return _store.CountActiveAsync(
            cancellationToken);
    }


    // =========================================================
    // SCORE
    // =========================================================

    private static double CalculateRecallScore(
        double similarity,
        SegaMemoryRecord memory,
        TimeSpan age)
    {
        double semantic =
            Math.Pow(
                Math.Clamp(
                    similarity,
                    0.0,
                    1.0),
                3.0);


        double importance =
            0.78 +
            memory.Importance *
                0.22;


        double confidence =
            0.80 +
            memory.Confidence *
                0.20;


        double emotional =
            0.95 +
            memory.EmotionalWeight *
                0.05;


        double reinforcement =
            1.0 +
            Math.Min(
                0.12,
                Math.Log(
                    1.0 +
                    memory.ReinforcementCount)
                *
                0.035);


        double recencyRaw =
            Math.Exp(
                -Math.Log(
                    2.0)
                *
                age.TotalSeconds
                /
                Math.Max(
                    1.0,
                    RetrievalRecencyHalfLife
                        .TotalSeconds));


        double recency =
            0.90 +
            recencyRaw *
                0.10;


        return semantic
            * importance
            * confidence
            * emotional
            * reinforcement
            * recency;
    }


    // =========================================================
    // LOGGING
    // =========================================================

    private static string TrimForLog(
        string value)
    {
        string normalized =
            value
                .Replace(
                    '\r',
                    ' ')
                .Replace(
                    '\n',
                    ' ')
                .Trim();


        const int maximumLength =
            80;


        if (normalized.Length <=
            maximumLength)
        {
            return normalized;
        }


        return normalized[
            ..maximumLength]
            + "...";
    }
}
