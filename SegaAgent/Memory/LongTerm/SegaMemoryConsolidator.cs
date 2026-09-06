/*
 * filename: SegaMemoryConsolidator.cs
 */

using System.Diagnostics;
using System.Text;

using SegaAgent.Semantic;

namespace SegaAgent.Memory.LongTerm;


// =============================================================
// MEMORY CONSOLIDATOR
//
// The responder may PROPOSE memory candidates.
//
// This service owns the application-side decision to:
//
// ignore
// create
// reinforce
// update
// supersede
//
// No LLM call is made here.
//
// Canonical keys are treated as authoritative identities for
// mutable facts/preferences. Semantic similarity is used for
// duplicate detection and non-canonical memories.
// =============================================================

public sealed class SegaMemoryConsolidator
{
    // =========================================================
    // DUPLICATE / UPDATE THRESHOLDS
    // =========================================================

    private const double CanonicalReinforceSimilarity =
        0.92;


    private const double SemanticDuplicateSimilarity =
        0.92;


    private const double SemanticCandidateFloor =
        0.84;


    // =========================================================
    // DEPENDENCIES
    // =========================================================

    private readonly SegaLongTermMemoryStore
        _store;


    private readonly SegaLongTermMemoryService
        _memory;


    private readonly ISegaSemanticEncoder
        _encoder;


    // =========================================================
    // SERIALIZATION
    //
    // AgentCore already serializes AI processing globally, but
    // memory can later be written from more than one subsystem.
    // Keep consolidation atomic at the application layer now.
    // =========================================================

    private readonly SemaphoreSlim
        _consolidationLock =
            new(
                1,
                1);


    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public SegaMemoryConsolidator(
        SegaLongTermMemoryStore store,
        SegaLongTermMemoryService memory,
        ISegaSemanticEncoder encoder)
    {
        _store =
            store
            ?? throw new ArgumentNullException(
                nameof(store));


        _memory =
            memory
            ?? throw new ArgumentNullException(
                nameof(memory));


        _encoder =
            encoder
            ?? throw new ArgumentNullException(
                nameof(encoder));
    }


    // =========================================================
    // ACTIVE COUNT
    // =========================================================

    public Task<long> CountActiveAsync(
        CancellationToken cancellationToken = default)
    {
        return _memory.CountActiveAsync(
            cancellationToken);
    }


    // =========================================================
    // CONSOLIDATE MANY
    // =========================================================

    public async Task<
        IReadOnlyList<SegaMemoryConsolidationResult>>
        ConsolidateAsync(
            IReadOnlyList<SegaMemoryCandidate> candidates,
            CancellationToken cancellationToken = default)
    {
        if (
            candidates ==
                null
            ||
            candidates.Count ==
                0)
        {
            return Array.Empty<
                SegaMemoryConsolidationResult>();
        }


        await _consolidationLock.WaitAsync(
            cancellationToken);


        try
        {
            List<SegaMemoryConsolidationResult> results =
                new(
                    candidates.Count);


            foreach (
                SegaMemoryCandidate candidate
                in candidates)
            {
                cancellationToken
                    .ThrowIfCancellationRequested();


                SegaMemoryConsolidationResult result =
                    await ConsolidateOneAsync(
                        candidate,
                        cancellationToken);


                results.Add(
                    result);


                LogResult(
                    result);
            }


            return results;
        }
        finally
        {
            _consolidationLock.Release();
        }
    }


    // =========================================================
    // CONSOLIDATE ONE
    // =========================================================

    private async Task<SegaMemoryConsolidationResult>
        ConsolidateOneAsync(
            SegaMemoryCandidate candidate,
            CancellationToken cancellationToken)
    {
        SegaMemoryCandidate normalized =
            candidate.Normalize();


        string? rejection =
            ValidateCandidate(
                normalized);


        if (rejection !=
            null)
        {
            return Ignore(
                normalized,
                rejection);
        }


        SemanticEmbedding candidateEmbedding =
            _encoder.Encode(
                normalized.Content);


        // =====================================================
        // CANONICAL IDENTITY PATH
        //
        // A canonical key represents one currently-authoritative
        // fact/preference slot.
        //
        // Example:
        // user.fact.name
        // =====================================================

        if (!string.IsNullOrWhiteSpace(
                normalized.CanonicalKey))
        {
            SegaStoredMemory? canonical =
                await _store.ReadActiveByCanonicalKeyAsync(
                    normalized.CanonicalKey,
                    cancellationToken);


            if (canonical !=
                null)
            {
                return await ConsolidateCanonicalAsync(
                    canonical,
                    normalized,
                    candidateEmbedding,
                    cancellationToken);
            }
        }


        // =====================================================
        // SEMANTIC DUPLICATE PATH
        //
        // Non-canonical memories such as shared experiences can
        // still be proposed repeatedly with slightly different
        // wording. Reinforce rather than duplicating them.
        // =====================================================

        SegaStoredMemory? semanticMatch =
            await FindSemanticDuplicateAsync(
                normalized,
                candidateEmbedding,
                cancellationToken);


        if (semanticMatch !=
            null)
        {
            double similarity =
                SegaSemanticSimilarity.Cosine(
                    candidateEmbedding,
                    semanticMatch.Embedding);


            if (
                string.IsNullOrWhiteSpace(
                    semanticMatch.Memory.CanonicalKey)
                &&
                !string.IsNullOrWhiteSpace(
                    normalized.CanonicalKey))
            {
                return await UpdateAsync(
                    semanticMatch,
                    normalized,
                    candidateEmbedding,
                    similarity,
                    cancellationToken);
            }


            return await ReinforceAsync(
                semanticMatch,
                normalized,
                similarity,
                "Semantic duplicate of an active durable memory.",
                cancellationToken);
        }


        // =====================================================
        // CREATE
        // =====================================================

        SegaMemoryRecord created =
            await _memory.CreateMemoryAsync(
                normalized,
                candidateEmbedding,
                cancellationToken);


        return new SegaMemoryConsolidationResult
        {
            Action =
                SegaMemoryConsolidationAction.Created,

            Candidate =
                normalized,

            Memory =
                created,

            Similarity =
                0.0,

            Reason =
                "No active canonical or semantic duplicate exists."
        };
    }


    // =========================================================
    // CANONICAL CONSOLIDATION
    // =========================================================

    private async Task<SegaMemoryConsolidationResult>
        ConsolidateCanonicalAsync(
            SegaStoredMemory existing,
            SegaMemoryCandidate candidate,
            SemanticEmbedding candidateEmbedding,
            CancellationToken cancellationToken)
    {
        double similarity =
            SegaSemanticSimilarity.Cosine(
                candidateEmbedding,
                existing.Embedding);


        if (EquivalentText(
                existing.Memory.Content,
                candidate.Content))
        {
            return await ReinforceAsync(
                existing,
                candidate,
                1.0,
                "Same canonical fact was stated again.",
                cancellationToken);
        }


        if (IsSafeEnrichment(
                existing.Memory.Content,
                candidate.Content,
                existing.Memory.Confidence,
                candidate.Confidence))
        {
            return await UpdateAsync(
                existing,
                candidate,
                candidateEmbedding,
                similarity,
                cancellationToken);
        }


        if (similarity >=
            CanonicalReinforceSimilarity)
        {
            return await ReinforceAsync(
                existing,
                candidate,
                similarity,
                "Same canonical memory was proposed with equivalent meaning.",
                cancellationToken);
        }


        if (!CanSupersede(
                existing.Memory,
                candidate))
        {
            return Ignore(
                candidate,
                "Candidate conflicts with an active canonical memory but lacks enough authority to replace it.",
                existing.Memory,
                similarity);
        }


        return await SupersedeAsync(
            existing,
            candidate,
            candidateEmbedding,
            similarity,
            cancellationToken);
    }


    // =========================================================
    // FIND SEMANTIC DUPLICATE
    // =========================================================

    private async Task<SegaStoredMemory?>
        FindSemanticDuplicateAsync(
            SegaMemoryCandidate candidate,
            SemanticEmbedding candidateEmbedding,
            CancellationToken cancellationToken)
    {
        IReadOnlyList<SegaStoredMemory> active =
            await _store.ReadActiveAsync(
                cancellationToken);


        SegaStoredMemory? best =
            null;


        double bestSimilarity =
            double.MinValue;


        foreach (
            SegaStoredMemory stored
            in active)
        {
            if (stored.Memory.Kind !=
                candidate.Kind)
            {
                continue;
            }


            if (
                !string.IsNullOrWhiteSpace(
                    candidate.CanonicalKey)
                &&
                !string.IsNullOrWhiteSpace(
                    stored.Memory.CanonicalKey)
                &&
                !string.Equals(
                    candidate.CanonicalKey,
                    stored.Memory.CanonicalKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }


            if (
                !string.IsNullOrWhiteSpace(
                    candidate.TopicKey)
                &&
                !string.IsNullOrWhiteSpace(
                    stored.Memory.TopicKey)
                &&
                !string.Equals(
                    candidate.TopicKey,
                    stored.Memory.TopicKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }


            double similarity =
                SegaSemanticSimilarity.Cosine(
                    candidateEmbedding,
                    stored.Embedding);


            if (similarity <
                SemanticCandidateFloor)
            {
                continue;
            }


            if (similarity >
                bestSimilarity)
            {
                best =
                    stored;


                bestSimilarity =
                    similarity;
            }
        }


        return bestSimilarity >=
                SemanticDuplicateSimilarity
            ? best
            : null;
    }


    // =========================================================
    // REINFORCE
    // =========================================================

    private async Task<SegaMemoryConsolidationResult>
        ReinforceAsync(
            SegaStoredMemory existing,
            SegaMemoryCandidate candidate,
            double similarity,
            string reason,
            CancellationToken cancellationToken)
    {
        DateTimeOffset now =
            DateTimeOffset.UtcNow;


        SegaMemoryRecord current =
            existing.Memory;


        double strengthenedConfidence =
            Math.Clamp(
                Math.Max(
                    current.Confidence,
                    candidate.Confidence)
                +
                (
                    1.0 -
                    Math.Max(
                        current.Confidence,
                        candidate.Confidence)
                )
                *
                0.025,
                0.0,
                1.0);


        SegaMemoryRecord reinforced =
            current with
            {
                Importance =
                    Math.Max(
                        current.Importance,
                        candidate.Importance),

                Confidence =
                    strengthenedConfidence,

                EmotionalWeight =
                    Math.Max(
                        current.EmotionalWeight,
                        candidate.EmotionalWeight),

                UpdatedAt =
                    now,

                ReinforcementCount =
                    current.ReinforcementCount +
                    1
            };


        await _store.UpdateAsync(
            reinforced,
            existing.Embedding,
            candidate.Provenance,
            cancellationToken);


        return new SegaMemoryConsolidationResult
        {
            Action =
                SegaMemoryConsolidationAction.Reinforced,

            Candidate =
                candidate,

            Memory =
                reinforced,

            PreviousMemoryId =
                current.Id,

            Similarity =
                similarity,

            Reason =
                reason
        };
    }


    // =========================================================
    // UPDATE
    //
    // Update is deliberately conservative. It is only used when
    // a candidate clearly enriches the same canonical statement,
    // not when a mutable fact has changed value.
    // =========================================================

    private async Task<SegaMemoryConsolidationResult>
        UpdateAsync(
            SegaStoredMemory existing,
            SegaMemoryCandidate candidate,
            SemanticEmbedding candidateEmbedding,
            double similarity,
            CancellationToken cancellationToken)
    {
        SegaMemoryRecord current =
            existing.Memory;


        SegaMemoryRecord updated =
            current with
            {
                Content =
                    candidate.Content,

                CanonicalKey =
                    candidate.CanonicalKey
                    ?? current.CanonicalKey,

                TopicKey =
                    candidate.TopicKey
                    ?? current.TopicKey,

                Importance =
                    Math.Max(
                        current.Importance,
                        candidate.Importance),

                Confidence =
                    Math.Max(
                        current.Confidence,
                        candidate.Confidence),

                EmotionalWeight =
                    Math.Max(
                        current.EmotionalWeight,
                        candidate.EmotionalWeight),

                UpdatedAt =
                    DateTimeOffset.UtcNow,

                ReinforcementCount =
                    current.ReinforcementCount +
                    1
            };


        await _store.UpdateAsync(
            updated,
            candidateEmbedding,
            candidate.Provenance,
            cancellationToken);


        return new SegaMemoryConsolidationResult
        {
            Action =
                SegaMemoryConsolidationAction.Updated,

            Candidate =
                candidate,

            Memory =
                updated,

            PreviousMemoryId =
                current.Id,

            Similarity =
                similarity,

            Reason =
                "Candidate safely enriches the same canonical memory."
        };
    }


    // =========================================================
    // SUPERSEDE
    // =========================================================

    private async Task<SegaMemoryConsolidationResult>
        SupersedeAsync(
            SegaStoredMemory existing,
            SegaMemoryCandidate candidate,
            SemanticEmbedding candidateEmbedding,
            double similarity,
            CancellationToken cancellationToken)
    {
        DateTimeOffset now =
            DateTimeOffset.UtcNow;


        SegaMemoryRecord replacement =
            new SegaMemoryRecord
            {
                Id =
                    Guid.NewGuid(),

                Kind =
                    candidate.Kind,

                Content =
                    candidate.Content,

                CanonicalKey =
                    candidate.CanonicalKey,

                TopicKey =
                    candidate.TopicKey
                    ?? existing.Memory.TopicKey,

                Importance =
                    candidate.Importance,

                Confidence =
                    candidate.Confidence,

                EmotionalWeight =
                    candidate.EmotionalWeight,

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
                    candidate.Provenance
            }
            .Normalize();


        await _store.SupersedeAndInsertAsync(
            existing.Memory.Id,
            replacement,
            candidateEmbedding,
            candidate.Provenance,
            cancellationToken);


        return new SegaMemoryConsolidationResult
        {
            Action =
                SegaMemoryConsolidationAction.Superseded,

            Candidate =
                candidate,

            Memory =
                replacement,

            PreviousMemoryId =
                existing.Memory.Id,

            Similarity =
                similarity,

            Reason =
                "Trusted new evidence changed the value of an existing canonical memory."
        };
    }


    // =========================================================
    // VALIDATION
    // =========================================================

    private static string? ValidateCandidate(
        SegaMemoryCandidate candidate)
    {
        if (SegaSensitiveMemoryPolicy.ShouldBlockDurableStorage(
                candidate,
                out string sensitiveReason))
        {
            return sensitiveReason;
        }


        if (candidate.Provenance.SourceType ==
            SegaMemorySourceType.Unknown)
        {
            return "Candidate has no authoritative source type.";
        }


        if (
            candidate.Provenance.SourceType ==
                SegaMemorySourceType.UserExplicit
            &&
            !candidate.Provenance.SourceEventId.HasValue)
        {
            return "Explicit-user memory has no grounded source event.";
        }


        double minimumConfidence =
            candidate.Provenance.SourceType switch
            {
                SegaMemorySourceType.UserExplicit =>
                    0.70,

                SegaMemorySourceType.SharedExperience =>
                    0.72,

                SegaMemorySourceType.SegaInference =>
                    0.82,

                SegaMemorySourceType.SystemDerived =>
                    0.85,

                SegaMemorySourceType.Imported =>
                    0.80,

                _ =>
                    1.01
            };


        if (candidate.Confidence <
            minimumConfidence)
        {
            return
                $"Confidence {candidate.Confidence:F2} is below " +
                $"the {minimumConfidence:F2} source threshold.";
        }


        double minimumImportance =
            candidate.Kind switch
            {
                SegaMemoryKind.UserFact =>
                    0.45,

                SegaMemoryKind.UserPreference =>
                    0.45,

                SegaMemoryKind.ProjectKnowledge =>
                    0.50,

                SegaMemoryKind.SharedExperience =>
                    0.55,

                SegaMemoryKind.ImportantEvent =>
                    0.60,

                SegaMemoryKind.SegaLearnedPreference =>
                    0.65,

                _ =>
                    1.01
            };


        bool emotionallyImportant =
            candidate.Kind ==
                SegaMemoryKind.ImportantEvent
            &&
            candidate.EmotionalWeight >=
                0.68;


        if (
            candidate.Importance <
                minimumImportance
            &&
            !emotionallyImportant)
        {
            return
                $"Importance {candidate.Importance:F2} is below " +
                $"the {minimumImportance:F2} kind threshold.";
        }


        if (
            candidate.Kind ==
                SegaMemoryKind.SegaLearnedPreference
            &&
            candidate.Provenance.SourceType !=
                SegaMemorySourceType.SegaInference)
        {
            return
                "SegaLearnedPreference requires SegaInference provenance.";
        }


        if (
            (
                candidate.Kind ==
                    SegaMemoryKind.UserFact
                ||
                candidate.Kind ==
                    SegaMemoryKind.UserPreference
            )
            &&
            candidate.Provenance.SourceType !=
                SegaMemorySourceType.UserExplicit
            &&
            candidate.Provenance.SourceType !=
                SegaMemorySourceType.Imported)
        {
            return
                "User facts/preferences require explicit user or imported evidence.";
        }


        return null;
    }


    // =========================================================
    // SUPERSEDE AUTHORITY
    // =========================================================

    private static bool CanSupersede(
        SegaMemoryRecord existing,
        SegaMemoryCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(
                candidate.CanonicalKey))
        {
            return false;
        }


        if (candidate.Confidence <
            0.82)
        {
            return false;
        }


        // User facts/preferences may only be replaced by user or
        // imported authority. A derived inference must never
        // silently change what the user explicitly established.
        if (
            existing.Kind ==
                SegaMemoryKind.UserFact
            ||
            existing.Kind ==
                SegaMemoryKind.UserPreference)
        {
            return
                candidate.Provenance.SourceType ==
                    SegaMemorySourceType.UserExplicit
                ||
                candidate.Provenance.SourceType ==
                    SegaMemorySourceType.Imported;
        }


        // Developed self-preference state is mirrored from Sega's
        // authoritative self-preference subsystem. Only grounded
        // Sega inference may replace that memory mirror.
        if (existing.Kind ==
            SegaMemoryKind.SegaLearnedPreference)
        {
            return
                candidate.Provenance.SourceType ==
                    SegaMemorySourceType.SegaInference
                &&
                candidate.Confidence +
                    0.05 >=
                    existing.Confidence;
        }


        int existingAuthority =
            SourceAuthority(
                existing.Provenance.SourceType);


        int candidateAuthority =
            SourceAuthority(
                candidate.Provenance.SourceType);


        if (candidateAuthority ==
            0)
        {
            return false;
        }


        // Stronger existing evidence cannot be displaced by a
        // weaker source merely because the new wording differs.
        if (candidateAuthority <
            existingAuthority)
        {
            return false;
        }


        // At equal authority, require roughly comparable
        // confidence. Higher-authority evidence may supersede even
        // when its numeric confidence is slightly lower.
        if (
            candidateAuthority ==
                existingAuthority
            &&
            candidate.Confidence +
                0.03 <
                existing.Confidence)
        {
            return false;
        }


        return true;
    }


    private static int SourceAuthority(
        SegaMemorySourceType source)
    {
        return source switch
        {
            SegaMemorySourceType.UserExplicit =>
                5,

            SegaMemorySourceType.Imported =>
                4,

            SegaMemorySourceType.SharedExperience =>
                3,

            SegaMemorySourceType.SystemDerived =>
                2,

            SegaMemorySourceType.SegaInference =>
                1,

            _ =>
                0
        };
    }


    // =========================================================
    // SAFE ENRICHMENT
    // =========================================================

    private static bool IsSafeEnrichment(
        string existing,
        string candidate,
        double existingConfidence,
        double candidateConfidence)
    {
        if (candidateConfidence +
            0.05 <
            existingConfidence)
        {
            return false;
        }


        string oldText =
            NormalizeComparisonText(
                existing);


        string newText =
            NormalizeComparisonText(
                candidate);


        if (newText.Length <=
            oldText.Length +
            12)
        {
            return false;
        }


        return newText.Contains(
            oldText,
            StringComparison.Ordinal);
    }


    // =========================================================
    // TEXT EQUIVALENCE
    // =========================================================

    private static bool EquivalentText(
        string left,
        string right)
    {
        return string.Equals(
            NormalizeComparisonText(
                left),
            NormalizeComparisonText(
                right),
            StringComparison.Ordinal);
    }


    private static string NormalizeComparisonText(
        string value)
    {
        StringBuilder builder =
            new(
                value.Length);


        bool previousSpace =
            false;


        foreach (char character
                 in value)
        {
            if (char.IsLetterOrDigit(
                    character))
            {
                builder.Append(
                    char.ToLowerInvariant(
                        character));


                previousSpace =
                    false;


                continue;
            }


            if (
                char.IsWhiteSpace(
                    character)
                &&
                !previousSpace
                &&
                builder.Length >
                    0)
            {
                builder.Append(
                    ' ');


                previousSpace =
                    true;
            }
        }


        return builder
            .ToString()
            .Trim();
    }


    // =========================================================
    // IGNORE
    // =========================================================

    private static SegaMemoryConsolidationResult Ignore(
        SegaMemoryCandidate candidate,
        string reason,
        SegaMemoryRecord? existing = null,
        double similarity = 0.0)
    {
        return new SegaMemoryConsolidationResult
        {
            Action =
                SegaMemoryConsolidationAction.Ignored,

            Candidate =
                candidate,

            Memory =
                existing,

            PreviousMemoryId =
                existing?.Id,

            Similarity =
                similarity,

            Reason =
                reason
        };
    }


    // =========================================================
    // LOGGING
    // =========================================================

    private static void LogResult(
        SegaMemoryConsolidationResult result)
    {
        string id =
            result.Memory?.Id.ToString()
            ?? "-";


        string previous =
            result.PreviousMemoryId?.ToString()
            ?? "-";


        Debug.WriteLine(
            $"[MemoryConsolidator] {result.Action.ToString().ToUpperInvariant()} | " +
            $"Kind={result.Candidate.Kind} | " +
            $"Canonical='{result.Candidate.CanonicalKey ?? "-"}' | " +
            $"Similarity={result.Similarity:F3} | " +
            $"Memory={id} | " +
            $"Previous={previous} | " +
            $"Reason='{result.Reason}' | " +
            $"Content='{TrimForLog(result.Candidate.Content)}'");
    }


    private static string TrimForLog(
        string value)
    {
        const int maximumLength =
            140;


        return value.Length <=
                maximumLength
            ? value
            : value[
                ..maximumLength]
                + "...";
    }
}