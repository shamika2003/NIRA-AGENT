/*
 * filename: NIRAMemoryConsolidator.cs
 */

using System.Diagnostics;
using System.Text;

using NIRAAgent.Semantic;

namespace NIRAAgent.Memory.LongTerm;


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

public sealed class NIRAMemoryConsolidator
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

    private readonly NIRALongTermMemoryStore
        _store;


    private readonly NIRALongTermMemoryService
        _memory;


    private readonly INIRASemanticEncoder
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

    public NIRAMemoryConsolidator(
        NIRALongTermMemoryStore store,
        NIRALongTermMemoryService memory,
        INIRASemanticEncoder encoder)
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
        IReadOnlyList<NIRAMemoryConsolidationResult>>
        ConsolidateAsync(
            IReadOnlyList<NIRAMemoryCandidate> candidates,
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
                NIRAMemoryConsolidationResult>();
        }


        await _consolidationLock.WaitAsync(
            cancellationToken);


        try
        {
            List<NIRAMemoryConsolidationResult> results =
                new(
                    candidates.Count);


            foreach (
                NIRAMemoryCandidate candidate
                in candidates)
            {
                cancellationToken
                    .ThrowIfCancellationRequested();


                NIRAMemoryConsolidationResult result =
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

    private async Task<NIRAMemoryConsolidationResult>
        ConsolidateOneAsync(
            NIRAMemoryCandidate candidate,
            CancellationToken cancellationToken)
    {
        NIRAMemoryCandidate normalized =
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
            NIRAStoredMemory? canonical =
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

        NIRAStoredMemory? semanticMatch =
            await FindSemanticDuplicateAsync(
                normalized,
                candidateEmbedding,
                cancellationToken);


        if (semanticMatch !=
            null)
        {
            double similarity =
                NIRASemanticSimilarity.Cosine(
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

        NIRAMemoryRecord created =
            await _memory.CreateMemoryAsync(
                normalized,
                candidateEmbedding,
                cancellationToken);


        return new NIRAMemoryConsolidationResult
        {
            Action =
                NIRAMemoryConsolidationAction.Created,

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

    private async Task<NIRAMemoryConsolidationResult>
        ConsolidateCanonicalAsync(
            NIRAStoredMemory existing,
            NIRAMemoryCandidate candidate,
            SemanticEmbedding candidateEmbedding,
            CancellationToken cancellationToken)
    {
        double similarity =
            NIRASemanticSimilarity.Cosine(
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

    private async Task<NIRAStoredMemory?>
        FindSemanticDuplicateAsync(
            NIRAMemoryCandidate candidate,
            SemanticEmbedding candidateEmbedding,
            CancellationToken cancellationToken)
    {
        IReadOnlyList<NIRAStoredMemory> active =
            await _store.ReadActiveAsync(
                cancellationToken);


        NIRAStoredMemory? best =
            null;


        double bestSimilarity =
            double.MinValue;


        foreach (
            NIRAStoredMemory stored
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
                NIRASemanticSimilarity.Cosine(
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

    private async Task<NIRAMemoryConsolidationResult>
        ReinforceAsync(
            NIRAStoredMemory existing,
            NIRAMemoryCandidate candidate,
            double similarity,
            string reason,
            CancellationToken cancellationToken)
    {
        DateTimeOffset now =
            DateTimeOffset.UtcNow;


        NIRAMemoryRecord current =
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


        NIRAMemoryRecord reinforced =
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


        return new NIRAMemoryConsolidationResult
        {
            Action =
                NIRAMemoryConsolidationAction.Reinforced,

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

    private async Task<NIRAMemoryConsolidationResult>
        UpdateAsync(
            NIRAStoredMemory existing,
            NIRAMemoryCandidate candidate,
            SemanticEmbedding candidateEmbedding,
            double similarity,
            CancellationToken cancellationToken)
    {
        NIRAMemoryRecord current =
            existing.Memory;


        NIRAMemoryRecord updated =
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


        return new NIRAMemoryConsolidationResult
        {
            Action =
                NIRAMemoryConsolidationAction.Updated,

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

    private async Task<NIRAMemoryConsolidationResult>
        SupersedeAsync(
            NIRAStoredMemory existing,
            NIRAMemoryCandidate candidate,
            SemanticEmbedding candidateEmbedding,
            double similarity,
            CancellationToken cancellationToken)
    {
        DateTimeOffset now =
            DateTimeOffset.UtcNow;


        NIRAMemoryRecord replacement =
            new NIRAMemoryRecord
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
                    NIRAMemoryStatus.Active,

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


        return new NIRAMemoryConsolidationResult
        {
            Action =
                NIRAMemoryConsolidationAction.Superseded,

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
        NIRAMemoryCandidate candidate)
    {
        if (NIRASensitiveMemoryPolicy.ShouldBlockDurableStorage(
                candidate,
                out string sensitiveReason))
        {
            return sensitiveReason;
        }


        if (candidate.Provenance.SourceType ==
            NIRAMemorySourceType.Unknown)
        {
            return "Candidate has no authoritative source type.";
        }


        if (
            candidate.Provenance.SourceType ==
                NIRAMemorySourceType.UserExplicit
            &&
            !candidate.Provenance.SourceEventId.HasValue)
        {
            return "Explicit-user memory has no grounded source event.";
        }


        double minimumConfidence =
            candidate.Provenance.SourceType switch
            {
                NIRAMemorySourceType.UserExplicit =>
                    0.70,

                NIRAMemorySourceType.SharedExperience =>
                    0.72,

                NIRAMemorySourceType.NIRAInference =>
                    0.82,

                NIRAMemorySourceType.SystemDerived =>
                    0.85,

                NIRAMemorySourceType.Imported =>
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
                NIRAMemoryKind.UserFact =>
                    0.45,

                NIRAMemoryKind.UserPreference =>
                    0.45,

                NIRAMemoryKind.ProjectKnowledge =>
                    0.50,

                NIRAMemoryKind.SharedExperience =>
                    0.55,

                NIRAMemoryKind.ImportantEvent =>
                    0.60,

                NIRAMemoryKind.NIRALearnedPreference =>
                    0.65,

                _ =>
                    1.01
            };


        bool emotionallyImportant =
            candidate.Kind ==
                NIRAMemoryKind.ImportantEvent
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
                NIRAMemoryKind.NIRALearnedPreference
            &&
            candidate.Provenance.SourceType !=
                NIRAMemorySourceType.NIRAInference)
        {
            return
                "NIRALearnedPreference requires NIRAInference provenance.";
        }


        if (
            (
                candidate.Kind ==
                    NIRAMemoryKind.UserFact
                ||
                candidate.Kind ==
                    NIRAMemoryKind.UserPreference
            )
            &&
            candidate.Provenance.SourceType !=
                NIRAMemorySourceType.UserExplicit
            &&
            candidate.Provenance.SourceType !=
                NIRAMemorySourceType.Imported)
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
        NIRAMemoryRecord existing,
        NIRAMemoryCandidate candidate)
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
                NIRAMemoryKind.UserFact
            ||
            existing.Kind ==
                NIRAMemoryKind.UserPreference)
        {
            return
                candidate.Provenance.SourceType ==
                    NIRAMemorySourceType.UserExplicit
                ||
                candidate.Provenance.SourceType ==
                    NIRAMemorySourceType.Imported;
        }


        // Developed self-preference state is mirrored from NIRA's
        // authoritative self-preference subsystem. Only grounded
        // NIRA inference may replace that memory mirror.
        if (existing.Kind ==
            NIRAMemoryKind.NIRALearnedPreference)
        {
            return
                candidate.Provenance.SourceType ==
                    NIRAMemorySourceType.NIRAInference
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
        NIRAMemorySourceType source)
    {
        return source switch
        {
            NIRAMemorySourceType.UserExplicit =>
                5,

            NIRAMemorySourceType.Imported =>
                4,

            NIRAMemorySourceType.SharedExperience =>
                3,

            NIRAMemorySourceType.SystemDerived =>
                2,

            NIRAMemorySourceType.NIRAInference =>
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

    private static NIRAMemoryConsolidationResult Ignore(
        NIRAMemoryCandidate candidate,
        string reason,
        NIRAMemoryRecord? existing = null,
        double similarity = 0.0)
    {
        return new NIRAMemoryConsolidationResult
        {
            Action =
                NIRAMemoryConsolidationAction.Ignored,

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
        NIRAMemoryConsolidationResult result)
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
