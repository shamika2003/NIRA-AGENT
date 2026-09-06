/*
 * filename: SegaLongTermMemoryService.cs
 */

using System.Diagnostics;
using System.Text;

using Microsoft.Extensions.Hosting;

using SegaAgent.Semantic;

namespace SegaAgent.Memory.LongTerm;

public sealed class SegaLongTermMemoryService
    : IHostedService
{
    private const int MaximumAllowedResults =
        20;


    private static readonly TimeSpan RetrievalRecencyHalfLife =
        TimeSpan.FromDays(
            180);


    private static readonly HashSet<string> StopWords =
        new(
            new[]
            {
                "a", "an", "and", "are", "as", "at", "be", "been", "but", "by",
                "can", "could", "did", "do", "does", "for", "from", "had", "has",
                "have", "he", "her", "hers", "him", "his", "how", "i", "if", "in",
                "into", "is", "it", "its", "me", "my", "of", "on", "or", "our",
                "ours", "she", "that", "the", "their", "them", "they", "this", "to",
                "us", "was", "we", "were", "what", "when", "where", "which", "who",
                "why", "will", "with", "would", "you", "your", "yours"
            },
            StringComparer.OrdinalIgnoreCase);


    private readonly SegaLongTermMemoryStore
        _store;


    private readonly ISegaSemanticEncoder
        _encoder;


    private readonly SegaMemoryAssociationService
        _associations;


    public SegaLongTermMemoryService(
        SegaLongTermMemoryStore store,
        ISegaSemanticEncoder encoder,
        SegaMemoryAssociationService associations)
    {
        _store =
            store
            ?? throw new ArgumentNullException(
                nameof(store));


        _encoder =
            encoder
            ?? throw new ArgumentNullException(
                nameof(encoder));


        _associations =
            associations
            ?? throw new ArgumentNullException(
                nameof(associations));
    }


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


    public Task StopAsync(
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }


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


        if (SegaSensitiveMemoryPolicy.ShouldBlockDurableStorage(
                normalized,
                out string sensitiveReason))
        {
            throw new InvalidOperationException(
                $"Durable memory rejected by sensitive-memory policy: {sensitiveReason}");
        }


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


    public async Task<IReadOnlyList<SegaMemoryRecord>>
        ReadActiveMemoriesAsync(
            CancellationToken cancellationToken = default)
    {
        await _store.InitializeAsync(
            cancellationToken);


        IReadOnlyList<SegaStoredMemory> stored =
            await _store.ReadActiveAsync(
                cancellationToken);


        return stored
            .Select(
                item =>
                    item.Memory)
            .OrderByDescending(
                memory =>
                    memory.Importance)
            .ThenByDescending(
                memory =>
                    memory.UpdatedAt)
            .ToArray();
    }


    public Task<IReadOnlyList<SegaMemorySearchResult>> SearchCandidatesAsync(
        string query,
        int maximumResults = MaximumAllowedResults,
        CancellationToken cancellationToken = default)
    {
        return SearchCandidatesAsync(
            new SegaMemorySearchRequest
            {
                Query =
                    query,

                MaximumResults =
                    maximumResults
            },
            cancellationToken);
    }


    // =========================================================
    // ASSOCIATIVE HIGH-RECALL SEARCH
    //
    // No absolute semantic cutoff exists here.
    //
    // Content similarity and associative retrieval similarity are
    // independent signals. Concepts/entities/links provide extra
    // entry paths. Main cognition still decides relevance.
    // =========================================================

    public async Task<IReadOnlyList<SegaMemorySearchResult>> SearchCandidatesAsync(
        SegaMemorySearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            request);


        SegaMemorySearchRequest normalized =
            request.Normalize();


        if (!normalized.HasSearchCriteria)
        {
            return Array.Empty<SegaMemorySearchResult>();
        }


        await _store.InitializeAsync(
            cancellationToken);


        SemanticEmbedding? queryEmbedding =
            string.IsNullOrWhiteSpace(
                normalized.Query)
                ? null
                : _encoder.Encode(
                    normalized.Query);


        HashSet<string> queryTerms =
            ExtractMeaningfulTerms(
                normalized.Query);


        IReadOnlyList<SegaStoredMemory> stored =
            await _store.ReadActiveAsync(
                cancellationToken);


        if (stored.Count ==
            0)
        {
            return Array.Empty<SegaMemorySearchResult>();
        }


        IReadOnlyDictionary<Guid, SegaStoredMemoryAssociationProfile> profiles =
            await _associations.ReadActiveProfilesAsync(
                cancellationToken);


        HashSet<SegaMemoryKind> kindFilter =
            new(
                normalized.Kinds);


        HashSet<string> canonicalFilter =
            new(
                normalized.CanonicalKeys,
                StringComparer.OrdinalIgnoreCase);


        string[] topicFilter =
            normalized.TopicKeys.ToArray();


        DateTimeOffset now =
            DateTimeOffset.UtcNow;


        Dictionary<Guid, SegaMemorySearchResult> candidateById =
            new();


        foreach (
            SegaStoredMemory item
            in stored)
        {
            cancellationToken.ThrowIfCancellationRequested();


            SegaMemoryRecord memory =
                item.Memory.Normalize();


            if (
                kindFilter.Count >
                    0
                &&
                !kindFilter.Contains(
                    memory.Kind))
            {
                continue;
            }


            if (
                canonicalFilter.Count >
                    0
                &&
                (
                    string.IsNullOrWhiteSpace(
                        memory.CanonicalKey)
                    ||
                    !canonicalFilter.Contains(
                        memory.CanonicalKey)
                ))
            {
                continue;
            }


            if (
                topicFilter.Length >
                    0
                &&
                !MatchesAnyTopic(
                    memory.TopicKey,
                    topicFilter))
            {
                continue;
            }


            SegaStoredMemoryAssociationProfile? storedProfile =
                profiles.TryGetValue(
                    memory.Id,
                    out SegaStoredMemoryAssociationProfile? profileValue)
                    ? profileValue
                    : null;


            SegaMemoryAssociationProfile profile =
                storedProfile?.Profile
                ?? SegaMemoryAssociationProfile.CreateFallback(
                    memory);


            double contentSimilarity =
                queryEmbedding ==
                    null
                    ? 0.0
                    : SegaSemanticSimilarity.Cosine(
                        queryEmbedding,
                        item.Embedding);


            double retrievalSimilarity =
                queryEmbedding ==
                    null
                    ||
                    storedProfile ==
                        null
                    ? 0.0
                    : SegaSemanticSimilarity.Cosine(
                        queryEmbedding,
                        storedProfile.RetrievalEmbedding);


            double lexicalAffinity =
                CalculateLexicalAffinity(
                    queryTerms,
                    BuildLexicalSearchText(
                        memory,
                        profile));


            double metadataAffinity =
                CalculateMetadataAffinity(
                    queryTerms,
                    memory,
                    canonicalFilter,
                    topicFilter);


            double associationAffinity =
                CalculateAssociationAffinity(
                    queryTerms,
                    normalized.Concepts,
                    normalized.Entities,
                    profile);


            TimeSpan age =
                now -
                memory.UpdatedAt;


            if (age <
                TimeSpan.Zero)
            {
                age =
                    TimeSpan.Zero;
            }


            double salience =
                CalculateSalience(
                    memory,
                    age);


            double score =
                CalculateCandidateScore(
                    contentSimilarity,
                    retrievalSimilarity,
                    lexicalAffinity,
                    metadataAffinity,
                    associationAffinity,
                    salience,
                    hasQuery:
                        queryEmbedding !=
                        null,
                    hasExplicitStructure:
                        canonicalFilter.Count >
                            0
                        ||
                        topicFilter.Length >
                            0
                        ||
                        kindFilter.Count >
                            0
                        ||
                        normalized.Concepts.Count >
                            0
                        ||
                        normalized.Entities.Count >
                            0);


            candidateById[memory.Id] =
                new SegaMemorySearchResult
                {
                    Memory =
                        memory,

                    Association =
                        profile,

                    Similarity =
                        contentSimilarity,

                    RetrievalSimilarity =
                        retrievalSimilarity,

                    LexicalAffinity =
                        lexicalAffinity,

                    MetadataAffinity =
                        metadataAffinity,

                    AssociationAffinity =
                        associationAffinity,

                    Salience =
                        salience,

                    Score =
                        score,

                    Age =
                        age
                };
        }


        int directLimit =
            normalized.ExpandAssociations
                ? Math.Max(
                    1,
                    (int)Math.Ceiling(
                        normalized.MaximumResults *
                        0.67))
                : normalized.MaximumResults;


        List<SegaMemorySearchResult> direct =
            candidateById.Values
                .OrderByDescending(
                    result =>
                        result.Score)
                .ThenByDescending(
                    result =>
                        Math.Max(
                            result.Similarity,
                            result.RetrievalSimilarity))
                .ThenByDescending(
                    result =>
                        result.Memory.UpdatedAt)
                .Take(
                    directLimit)
                .ToList();


        if (
            normalized.ExpandAssociations
            &&
            direct.Count >
                0
            &&
            direct.Count <
                normalized.MaximumResults)
        {
            int seedCount =
                Math.Min(
                    4,
                    direct.Count);


            Guid[] seedIds =
                direct
                    .Take(
                        seedCount)
                    .Select(
                        result =>
                            result.Memory.Id)
                    .ToArray();


            IReadOnlyList<SegaMemoryAssociationLink> links =
                await _associations.ReadRelatedAsync(
                    seedIds,
                    normalized.MaximumResults *
                        4,
                    cancellationToken);


            Dictionary<Guid, List<string>> reasonsByTarget =
                new();


            foreach (
                SegaMemoryAssociationLink link
                in links)
            {
                if (!candidateById.ContainsKey(
                        link.TargetMemoryId))
                {
                    continue;
                }


                if (!reasonsByTarget.TryGetValue(
                        link.TargetMemoryId,
                        out List<string>? reasons))
                {
                    reasons =
                        new List<string>();


                    reasonsByTarget[
                        link.TargetMemoryId] =
                            reasons;
                }


                string reason =
                    $"{link.RelationKind}: {link.SharedValue}";


                if (!reasons.Contains(
                        reason,
                        StringComparer.OrdinalIgnoreCase))
                {
                    reasons.Add(
                        reason);
                }
            }


            HashSet<Guid> alreadySelected =
                new(
                    direct.Select(
                        result =>
                            result.Memory.Id));


            foreach (
                KeyValuePair<Guid, List<string>> pair
                in reasonsByTarget
                    .OrderByDescending(
                        pair =>
                            candidateById[pair.Key].Salience))
            {
                if (direct.Count >=
                    normalized.MaximumResults)
                {
                    break;
                }


                if (!alreadySelected.Add(
                        pair.Key))
                {
                    continue;
                }


                SegaMemorySearchResult candidate =
                    candidateById[pair.Key];


                direct.Add(
                    candidate with
                    {
                        AssociationReasons =
                            pair.Value.ToArray()
                    });
            }
        }


        SegaMemorySearchResult[] selected =
            direct
                .Take(
                    normalized.MaximumResults)
                .ToArray();


        Debug.WriteLine(
            $"[LongTermMemory] CANDIDATE SEARCH | " +
            $"Query='{TrimForLog(normalized.Query)}' | " +
            $"Kinds={normalized.Kinds.Count} | " +
            $"Canonical={normalized.CanonicalKeys.Count} | " +
            $"Topics={normalized.TopicKeys.Count} | " +
            $"Concepts={normalized.Concepts.Count} | " +
            $"Entities={normalized.Entities.Count} | " +
            $"Expand={normalized.ExpandAssociations} | " +
            $"Scanned={stored.Count} | " +
            $"Eligible={candidateById.Count} | " +
            $"Returned={selected.Length}");


        return selected;
    }


    // =========================================================
    // RECORD SURFACED RECALLS
    //
    // Search ranking itself is not a recall. The executive calls
    // this only for candidate memories actually surfaced back to
    // main cognition as new evidence.
    // =========================================================

    public async Task RecordSurfacedRecallsAsync(
        IReadOnlyCollection<Guid> memoryIds,
        CancellationToken cancellationToken = default)
    {
        if (
            memoryIds ==
                null
            ||
            memoryIds.Count ==
                0)
        {
            return;
        }


        Guid[] distinctIds =
            memoryIds
                .Where(
                    id =>
                        id !=
                        Guid.Empty)
                .Distinct()
                .ToArray();


        if (distinctIds.Length ==
            0)
        {
            return;
        }


        await _store.RecordRecallsAsync(
            distinctIds,
            DateTimeOffset.UtcNow,
            cancellationToken);


        Debug.WriteLine(
            $"[LongTermMemory] RECORDED RECALLS | " +
            $"Count={distinctIds.Length}");
    }


    public Task<long> CountActiveAsync(
        CancellationToken cancellationToken = default)
    {
        return _store.CountActiveAsync(
            cancellationToken);
    }


    private static string BuildLexicalSearchText(
        SegaMemoryRecord memory,
        SegaMemoryAssociationProfile profile)
    {
        StringBuilder builder =
            new();


        builder.Append(
            memory.Content);


        builder.Append(' ');


        if (!string.IsNullOrWhiteSpace(
                memory.CanonicalKey))
        {
            builder.Append(
                HumanizeKey(
                    memory.CanonicalKey));


            builder.Append(' ');
        }


        if (!string.IsNullOrWhiteSpace(
                memory.TopicKey))
        {
            builder.Append(
                HumanizeKey(
                    memory.TopicKey));


            builder.Append(' ');
        }


        if (!string.IsNullOrWhiteSpace(
                profile.RetrievalDescription))
        {
            builder.Append(
                profile.RetrievalDescription);


            builder.Append(' ');
        }


        builder.Append(
            string.Join(
                ' ',
                profile.RetrievalCues));


        builder.Append(' ');


        builder.Append(
            string.Join(
                ' ',
                profile.Concepts));


        builder.Append(' ');


        builder.Append(
            string.Join(
                ' ',
                profile.Entities));


        return builder.ToString();
    }


    private static double CalculateLexicalAffinity(
        HashSet<string> queryTerms,
        string content)
    {
        if (queryTerms.Count ==
            0)
        {
            return 0.0;
        }


        HashSet<string> contentTerms =
            ExtractMeaningfulTerms(
                content);


        if (contentTerms.Count ==
            0)
        {
            return 0.0;
        }


        int overlap =
            queryTerms.Count(
                contentTerms.Contains);


        if (overlap ==
            0)
        {
            return 0.0;
        }


        double queryCoverage =
            overlap /
            (double)queryTerms.Count;


        double contentCoverage =
            overlap /
            (double)contentTerms.Count;


        return Math.Clamp(
            queryCoverage *
                0.82
            +
            contentCoverage *
                0.18,
            0.0,
            1.0);
    }


    private static double CalculateAssociationAffinity(
        HashSet<string> queryTerms,
        IReadOnlyList<string> requestedConcepts,
        IReadOnlyList<string> requestedEntities,
        SegaMemoryAssociationProfile profile)
    {
        HashSet<string> concepts =
            new(
                profile.Concepts,
                StringComparer.OrdinalIgnoreCase);


        HashSet<string> entities =
            new(
                profile.Entities,
                StringComparer.OrdinalIgnoreCase);


        double explicitConcept =
            requestedConcepts.Count ==
                0
                ? 0.0
                : requestedConcepts.Count(
                    concepts.Contains)
                    /
                    (double)requestedConcepts.Count;


        double explicitEntity =
            requestedEntities.Count ==
                0
                ? 0.0
                : requestedEntities.Count(
                    entities.Contains)
                    /
                    (double)requestedEntities.Count;


        string associativeText =
            string.Join(
                ' ',
                profile.Concepts
                    .Concat(
                        profile.Entities)
                    .Concat(
                        profile.RetrievalCues));


        double inferred =
            CalculateLexicalAffinity(
                queryTerms,
                associativeText);


        return Math.Clamp(
            Math.Max(
                inferred,
                Math.Max(
                    explicitConcept,
                    explicitEntity)),
            0.0,
            1.0);
    }


    private static double CalculateMetadataAffinity(
        HashSet<string> queryTerms,
        SegaMemoryRecord memory,
        HashSet<string> canonicalFilter,
        IReadOnlyList<string> topicFilter)
    {
        double explicitAffinity =
            0.0;


        if (
            canonicalFilter.Count >
                0
            &&
            !string.IsNullOrWhiteSpace(
                memory.CanonicalKey)
            &&
            canonicalFilter.Contains(
                memory.CanonicalKey))
        {
            explicitAffinity =
                1.0;
        }


        if (
            topicFilter.Count >
                0
            &&
            MatchesAnyTopic(
                memory.TopicKey,
                topicFilter))
        {
            explicitAffinity =
                Math.Max(
                    explicitAffinity,
                    0.95);
        }


        if (queryTerms.Count ==
            0)
        {
            return explicitAffinity;
        }


        StringBuilder metadataText =
            new();


        if (!string.IsNullOrWhiteSpace(
                memory.CanonicalKey))
        {
            metadataText.Append(
                HumanizeKey(
                    memory.CanonicalKey));


            metadataText.Append(' ');
        }


        if (!string.IsNullOrWhiteSpace(
                memory.TopicKey))
        {
            metadataText.Append(
                HumanizeKey(
                    memory.TopicKey));
        }


        HashSet<string> metadataTerms =
            ExtractMeaningfulTerms(
                metadataText.ToString());


        if (metadataTerms.Count ==
            0)
        {
            return explicitAffinity;
        }


        int overlap =
            queryTerms.Count(
                metadataTerms.Contains);


        if (overlap ==
            0)
        {
            return explicitAffinity;
        }


        double queryCoverage =
            overlap /
            (double)queryTerms.Count;


        double metadataCoverage =
            overlap /
            (double)metadataTerms.Count;


        double inferred =
            Math.Clamp(
                queryCoverage *
                    0.78
                +
                metadataCoverage *
                    0.22,
                0.0,
                1.0);


        return Math.Max(
            explicitAffinity,
            inferred);
    }


    private static double CalculateSalience(
        SegaMemoryRecord memory,
        TimeSpan age)
    {
        double reinforcement =
            Math.Min(
                1.0,
                Math.Log(
                    1.0 +
                    memory.ReinforcementCount)
                /
                4.0);


        double recallUse =
            Math.Min(
                1.0,
                Math.Log(
                    1.0 +
                    memory.RecallCount)
                /
                5.0);


        double recency =
            Math.Exp(
                -Math.Log(
                    2.0)
                *
                age.TotalSeconds
                /
                Math.Max(
                    1.0,
                    RetrievalRecencyHalfLife.TotalSeconds));


        double effectiveConfidence =
            SegaMemoryConfidencePolicy.CalculateEffectiveConfidence(
                memory,
                age);


        return Math.Clamp(
            memory.Importance *
                0.35
            +
            effectiveConfidence *
                0.22
            +
            memory.EmotionalWeight *
                0.10
            +
            reinforcement *
                0.13
            +
            recallUse *
                0.08
            +
            recency *
                0.12,
            0.0,
            1.0);
    }


    private static double CalculateCandidateScore(
        double contentSimilarity,
        double retrievalSimilarity,
        double lexicalAffinity,
        double metadataAffinity,
        double associationAffinity,
        double salience,
        bool hasQuery,
        bool hasExplicitStructure)
    {
        double semantic =
            Math.Max(
                Math.Clamp(
                    contentSimilarity,
                    0.0,
                    1.0),
                Math.Clamp(
                    retrievalSimilarity,
                    0.0,
                    1.0));


        if (!hasQuery)
        {
            return Math.Clamp(
                metadataAffinity *
                    0.38
                +
                associationAffinity *
                    0.38
                +
                salience *
                    0.24,
                0.0,
                1.0);
        }


        double semanticWeight =
            hasExplicitStructure
                ? 0.38
                : 0.46;


        double lexicalWeight =
            0.18;


        double metadataWeight =
            hasExplicitStructure
                ? 0.18
                : 0.12;


        double associationWeight =
            hasExplicitStructure
                ? 0.18
                : 0.16;


        double salienceWeight =
            1.0
            -
            semanticWeight
            -
            lexicalWeight
            -
            metadataWeight
            -
            associationWeight;


        return Math.Clamp(
            semantic *
                semanticWeight
            +
            lexicalAffinity *
                lexicalWeight
            +
            metadataAffinity *
                metadataWeight
            +
            associationAffinity *
                associationWeight
            +
            salience *
                salienceWeight,
            0.0,
            1.0);
    }


    private static bool MatchesAnyTopic(
        string? memoryTopic,
        IReadOnlyList<string> requestedTopics)
    {
        if (string.IsNullOrWhiteSpace(
                memoryTopic))
        {
            return false;
        }


        foreach (
            string requested
            in requestedTopics)
        {
            if (TopicMatches(
                    memoryTopic,
                    requested))
            {
                return true;
            }
        }


        return false;
    }


    private static bool TopicMatches(
        string memoryTopic,
        string requestedTopic)
    {
        if (string.Equals(
                memoryTopic,
                requestedTopic,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }


        if (!memoryTopic.StartsWith(
                requestedTopic,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }


        if (memoryTopic.Length ==
            requestedTopic.Length)
        {
            return true;
        }


        char boundary =
            memoryTopic[
                requestedTopic.Length];


        return boundary is
            '.'
            or ':'
            or '/'
            or '-';
    }


    private static HashSet<string> ExtractMeaningfulTerms(
        string value)
    {
        HashSet<string> terms =
            new(
                StringComparer.OrdinalIgnoreCase);


        if (string.IsNullOrWhiteSpace(
                value))
        {
            return terms;
        }


        StringBuilder token =
            new();


        void FlushToken()
        {
            if (token.Length ==
                0)
            {
                return;
            }


            string term =
                token.ToString()
                    .ToLowerInvariant();


            token.Clear();


            if (
                term.Length <
                    2
                ||
                StopWords.Contains(
                    term))
            {
                return;
            }


            terms.Add(
                term);
        }


        foreach (
            char character
            in value)
        {
            if (char.IsLetterOrDigit(
                    character))
            {
                token.Append(
                    char.ToLowerInvariant(
                        character));
            }
            else
            {
                FlushToken();
            }
        }


        FlushToken();


        return terms;
    }


    private static string HumanizeKey(
        string value)
    {
        return value
            .Replace('.', ' ')
            .Replace('_', ' ')
            .Replace('-', ' ')
            .Replace(':', ' ')
            .Replace('/', ' ');
    }


    private static string TrimForLog(
        string value)
    {
        string normalized =
            (value ?? string.Empty)
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim();


        const int maximumLength =
            80;


        return normalized.Length <=
                maximumLength
            ? normalized
            : normalized[..maximumLength]
                +
                "...";
    }
}