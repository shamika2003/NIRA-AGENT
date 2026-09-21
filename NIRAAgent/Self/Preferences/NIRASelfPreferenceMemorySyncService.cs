/*
 * filename: NIRASelfPreferenceMemorySyncService.cs
 */

using System.Diagnostics;

using Microsoft.Extensions.Hosting;

using NIRAAgent.Memory.LongTerm;

namespace NIRAAgent.Self.Preferences;


// =============================================================
// SELF-PREFERENCE -> LONG-TERM MEMORY MIRROR
//
// NIRASelfPreferenceService remains authoritative.
//
// This service mirrors only ESTABLISHED developed preferences
// into NIRALearnedPreference long-term memory so NIRA can recall
// the history/meaning of her developed tastes through the normal
// memory system.
//
// Long-term memory never overwrites NIRA's authoritative current
// self-preference state.
// =============================================================

public sealed class NIRASelfPreferenceMemorySyncService
    : IHostedService
{
    private const string CanonicalPrefix =
        "NIRA.self.preference.";


    private readonly NIRASelfPreferenceService
        _selfPreferences;


    private readonly NIRALongTermMemoryStore
        _memoryStore;


    private readonly NIRAMemoryConsolidator
        _consolidator;


    private readonly NIRAMemoryAssociationService
        _associations;


    private readonly SemaphoreSlim
        _syncLock =
            new(
                1,
                1);


    public NIRASelfPreferenceMemorySyncService(
        NIRASelfPreferenceService selfPreferences,
        NIRALongTermMemoryStore memoryStore,
        NIRAMemoryConsolidator consolidator,
        NIRAMemoryAssociationService associations)
    {
        _selfPreferences =
            selfPreferences
            ?? throw new ArgumentNullException(
                nameof(selfPreferences));


        _memoryStore =
            memoryStore
            ?? throw new ArgumentNullException(
                nameof(memoryStore));


        _consolidator =
            consolidator
            ?? throw new ArgumentNullException(
                nameof(consolidator));


        _associations =
            associations
            ?? throw new ArgumentNullException(
                nameof(associations));
    }


    // =========================================================
    // STARTUP RECONCILIATION
    //
    // Restart must preserve a consistent relationship between
    // authoritative developed-self state and active memory.
    // =========================================================

    public async Task StartAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<NIRASelfPreferenceState> preferences =
            _selfPreferences.Current;


        int established =
            0;


        int changed =
            0;


        foreach (
            NIRASelfPreferenceState preference
            in preferences)
        {
            cancellationToken.ThrowIfCancellationRequested();


            if (preference.Status ==
                NIRASelfPreferenceStatus.Established)
            {
                established++;
            }


            NIRASelfPreferenceMemorySyncAction action =
                await SynchronizeAsync(
                    preference,
                    sourceObservation: null,
                    cancellationToken);


            if (action is not
                NIRASelfPreferenceMemorySyncAction.NoChange
                and not
                NIRASelfPreferenceMemorySyncAction.NotEstablished)
            {
                changed++;
            }
        }


        Debug.WriteLine(
            $"[SelfPreferenceMemorySync] READY | " +
            $"Preferences={preferences.Count} | " +
            $"Established={established} | " +
            $"Reconciled={changed}");
    }


    public Task StopAsync(
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }


    // =========================================================
    // SYNCHRONIZE ONE AUTHORITATIVE PREFERENCE
    // =========================================================

    public async Task<NIRASelfPreferenceMemorySyncAction>
        SynchronizeAsync(
            NIRASelfPreferenceState preference,
            NIRASelfPreferenceObservation? sourceObservation = null,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            preference);


        NIRASelfPreferenceState normalized =
            preference.Normalize();


        string canonicalKey =
            BuildCanonicalKey(
                normalized.Key);


        await _syncLock.WaitAsync(
            cancellationToken);


        try
        {
            // =================================================
            // NO LONGER ESTABLISHED
            //
            // A stale active learned-preference memory must not
            // survive after authoritative self-state says the
            // preference is no longer established.
            // =================================================

            if (normalized.Status !=
                NIRASelfPreferenceStatus.Established)
            {
                bool archived =
                    await _memoryStore
                        .ArchiveActiveByCanonicalKeyAsync(
                            canonicalKey,
                            cancellationToken);


                if (archived)
                {
                    Debug.WriteLine(
                        $"[SelfPreferenceMemorySync] ARCHIVED | " +
                        $"Key='{normalized.Key}' | " +
                        $"Canonical='{canonicalKey}' | " +
                        $"Reason='Preference is no longer established.'");


                    return
                        NIRASelfPreferenceMemorySyncAction.Archived;
                }


                return
                    NIRASelfPreferenceMemorySyncAction.NotEstablished;
            }


            string content =
                BuildMemoryContent(
                    normalized);


            NIRAStoredMemory? existing =
                await _memoryStore
                    .ReadActiveByCanonicalKeyAsync(
                        canonicalKey,
                        cancellationToken);


            // =================================================
            // NO-CHANGE GUARD
            //
            // Do not reinforce a memory merely because NIRA was
            // restarted or because another observation changed a
            // numeric preference value without changing its
            // durable semantic meaning.
            // =================================================

            if (
                existing !=
                    null
                &&
                existing.Memory.Kind ==
                    NIRAMemoryKind.NIRALearnedPreference
                &&
                string.Equals(
                    existing.Memory.Content,
                    content,
                    StringComparison.Ordinal)
                &&
                string.Equals(
                    existing.Memory.TopicKey,
                    normalized.TopicKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                return
                    NIRASelfPreferenceMemorySyncAction.NoChange;
            }


            NIRAMemoryCandidate candidate =
                BuildCandidate(
                    normalized,
                    content,
                    canonicalKey,
                    sourceObservation);


            IReadOnlyList<NIRAMemoryConsolidationResult> results =
                await _consolidator.ConsolidateAsync(
                    new[]
                    {
                        candidate
                    },
                    cancellationToken);


            await _associations.ApplyConsolidationAsync(
                results,
                cancellationToken);


            NIRAMemoryConsolidationResult? result =
                results.FirstOrDefault();


            if (result ==
                null)
            {
                Debug.WriteLine(
                    $"[SelfPreferenceMemorySync] IGNORED | " +
                    $"Key='{normalized.Key}' | " +
                    $"Reason='Consolidator produced no result.'");


                return
                    NIRASelfPreferenceMemorySyncAction.Ignored;
            }


            NIRASelfPreferenceMemorySyncAction action =
                result.Action switch
                {
                    NIRAMemoryConsolidationAction.Created =>
                        NIRASelfPreferenceMemorySyncAction.Created,

                    NIRAMemoryConsolidationAction.Reinforced =>
                        NIRASelfPreferenceMemorySyncAction.Updated,

                    NIRAMemoryConsolidationAction.Updated =>
                        NIRASelfPreferenceMemorySyncAction.Updated,

                    NIRAMemoryConsolidationAction.Superseded =>
                        NIRASelfPreferenceMemorySyncAction.Superseded,

                    _ =>
                        NIRASelfPreferenceMemorySyncAction.Ignored
                };


            Debug.WriteLine(
                $"[SelfPreferenceMemorySync] {action.ToString().ToUpperInvariant()} | " +
                $"Key='{normalized.Key}' | " +
                $"Canonical='{canonicalKey}' | " +
                $"Affinity={normalized.Affinity:F2} | " +
                $"Confidence={normalized.Confidence:F2} | " +
                $"Observations={normalized.ObservationCount} | " +
                $"Reason='{result.Reason}'");


            return action;
        }
        finally
        {
            _syncLock.Release();
        }
    }


    // =========================================================
    // CANDIDATE
    // =========================================================

    private static NIRAMemoryCandidate BuildCandidate(
        NIRASelfPreferenceState preference,
        string content,
        string canonicalKey,
        NIRASelfPreferenceObservation? sourceObservation)
    {
        double magnitude =
            Math.Abs(
                preference.Affinity);


        double memoryConfidence =
            Math.Clamp(
                0.82
                +
                Math.Max(
                    0.0,
                    preference.Confidence -
                        0.70)
                *
                0.45,
                0.82,
                0.96);


        double importance =
            Math.Clamp(
                0.65
                +
                magnitude *
                    0.18,
                0.65,
                0.88);


        NIRAMemoryProvenance provenance =
            new NIRAMemoryProvenance
            {
                SourceType =
                    NIRAMemorySourceType.NIRAInference,

                SourceEventId =
                    sourceObservation?.SourceEventId,

                SourceEventSequence =
                    sourceObservation?.SourceEventSequence,

                SourceTimestamp =
                    sourceObservation?.SourceTimestamp
                    ?? preference.UpdatedAt,

                SourceExcerpt =
                    BuildSourceExcerpt(
                        sourceObservation,
                        preference)
            }
            .Normalize();


        return new NIRAMemoryCandidate
        {
            Kind =
                NIRAMemoryKind.NIRALearnedPreference,

            Content =
                content,

            CanonicalKey =
                canonicalKey,

            TopicKey =
                preference.TopicKey,

            Importance =
                importance,

            Confidence =
                memoryConfidence,

            EmotionalWeight =
                Math.Clamp(
                    magnitude *
                        0.55,
                    0.10,
                    0.70),

            Association =
                BuildAssociation(
                    preference,
                    content),

            Provenance =
                provenance
        }
        .Normalize();
    }


    // =========================================================
    // DURABLE SEMANTIC MEMORY CONTENT
    //
    // This intentionally describes the stable meaning rather
    // than storing NIRA's exact floating-point state in prose.
    // The authoritative numeric state remains in NIRA-self.db.
    // =========================================================

    private static string BuildMemoryContent(
        NIRASelfPreferenceState preference)
    {
        string direction =
            preference.Affinity switch
            {
                >= 0.65 =>
                    "strongly likes and tends to prefer",

                >= 0.30 =>
                    "likes and tends to prefer",

                > 0.0 =>
                    "has a mild positive preference toward",

                <= -0.65 =>
                    "strongly dislikes and tends to avoid",

                <= -0.30 =>
                    "dislikes and tends to avoid",

                _ =>
                    "has a mild negative preference toward"
            };


        return
            $"Through repeated meaningful experience, NIRA has developed a durable self-preference: " +
            $"she {direction} {preference.Subject}.";
    }


    // =========================================================
    // ASSOCIATIVE PROFILE
    // =========================================================

    private static NIRAMemoryAssociationProfile BuildAssociation(
        NIRASelfPreferenceState preference,
        string content)
    {
        return new NIRAMemoryAssociationProfile
        {
            RetrievalDescription =
                $"NIRA's own learned durable preference about {preference.Subject}. " +
                $"This preference developed gradually through repeated experience. {content}",

            RetrievalCues =
                new[]
                {
                    preference.Subject,
                    preference.Key,
                    "NIRA learned preference",
                    "NIRA self preference",
                    "what NIRA likes",
                    "what NIRA dislikes",
                    "what NIRA prefers"
                },

            Concepts =
                new[]
                {
                    "NIRA self preference",
                    "learned preference",
                    "developed preference",
                    preference.Subject
                }
        }
        .Normalize();
    }


    private static string BuildCanonicalKey(
        string preferenceKey)
    {
        return
            CanonicalPrefix +
            NIRASelfPreferenceState.NormalizeRequiredKey(
                preferenceKey);
    }


    private static string BuildSourceExcerpt(
        NIRASelfPreferenceObservation? observation,
        NIRASelfPreferenceState preference)
    {
        if (!string.IsNullOrWhiteSpace(
                observation?.EvidenceSummary))
        {
            return observation!
                .EvidenceSummary!
                .Trim();
        }


        return
            $"Authoritative developed self-preference synchronized after " +
            $"{preference.ObservationCount} meaningful observations.";
    }
}


public enum NIRASelfPreferenceMemorySyncAction
{
    NotEstablished,

    NoChange,

    Created,

    Updated,

    Superseded,

    Archived,

    Ignored
}

