/*
 * filename: SegaSelfPreferenceMemorySyncService.cs
 */

using System.Diagnostics;

using Microsoft.Extensions.Hosting;

using SegaAgent.Memory.LongTerm;

namespace SegaAgent.Self.Preferences;


// =============================================================
// SELF-PREFERENCE -> LONG-TERM MEMORY MIRROR
//
// SegaSelfPreferenceService remains authoritative.
//
// This service mirrors only ESTABLISHED developed preferences
// into SegaLearnedPreference long-term memory so Sega can recall
// the history/meaning of her developed tastes through the normal
// memory system.
//
// Long-term memory never overwrites Sega's authoritative current
// self-preference state.
// =============================================================

public sealed class SegaSelfPreferenceMemorySyncService
    : IHostedService
{
    private const string CanonicalPrefix =
        "sega.self.preference.";


    private readonly SegaSelfPreferenceService
        _selfPreferences;


    private readonly SegaLongTermMemoryStore
        _memoryStore;


    private readonly SegaMemoryConsolidator
        _consolidator;


    private readonly SegaMemoryAssociationService
        _associations;


    private readonly SemaphoreSlim
        _syncLock =
            new(
                1,
                1);


    public SegaSelfPreferenceMemorySyncService(
        SegaSelfPreferenceService selfPreferences,
        SegaLongTermMemoryStore memoryStore,
        SegaMemoryConsolidator consolidator,
        SegaMemoryAssociationService associations)
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
        IReadOnlyList<SegaSelfPreferenceState> preferences =
            _selfPreferences.Current;


        int established =
            0;


        int changed =
            0;


        foreach (
            SegaSelfPreferenceState preference
            in preferences)
        {
            cancellationToken.ThrowIfCancellationRequested();


            if (preference.Status ==
                SegaSelfPreferenceStatus.Established)
            {
                established++;
            }


            SegaSelfPreferenceMemorySyncAction action =
                await SynchronizeAsync(
                    preference,
                    sourceObservation: null,
                    cancellationToken);


            if (action is not
                SegaSelfPreferenceMemorySyncAction.NoChange
                and not
                SegaSelfPreferenceMemorySyncAction.NotEstablished)
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

    public async Task<SegaSelfPreferenceMemorySyncAction>
        SynchronizeAsync(
            SegaSelfPreferenceState preference,
            SegaSelfPreferenceObservation? sourceObservation = null,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            preference);


        SegaSelfPreferenceState normalized =
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
                SegaSelfPreferenceStatus.Established)
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
                        SegaSelfPreferenceMemorySyncAction.Archived;
                }


                return
                    SegaSelfPreferenceMemorySyncAction.NotEstablished;
            }


            string content =
                BuildMemoryContent(
                    normalized);


            SegaStoredMemory? existing =
                await _memoryStore
                    .ReadActiveByCanonicalKeyAsync(
                        canonicalKey,
                        cancellationToken);


            // =================================================
            // NO-CHANGE GUARD
            //
            // Do not reinforce a memory merely because Sega was
            // restarted or because another observation changed a
            // numeric preference value without changing its
            // durable semantic meaning.
            // =================================================

            if (
                existing !=
                    null
                &&
                existing.Memory.Kind ==
                    SegaMemoryKind.SegaLearnedPreference
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
                    SegaSelfPreferenceMemorySyncAction.NoChange;
            }


            SegaMemoryCandidate candidate =
                BuildCandidate(
                    normalized,
                    content,
                    canonicalKey,
                    sourceObservation);


            IReadOnlyList<SegaMemoryConsolidationResult> results =
                await _consolidator.ConsolidateAsync(
                    new[]
                    {
                        candidate
                    },
                    cancellationToken);


            await _associations.ApplyConsolidationAsync(
                results,
                cancellationToken);


            SegaMemoryConsolidationResult? result =
                results.FirstOrDefault();


            if (result ==
                null)
            {
                Debug.WriteLine(
                    $"[SelfPreferenceMemorySync] IGNORED | " +
                    $"Key='{normalized.Key}' | " +
                    $"Reason='Consolidator produced no result.'");


                return
                    SegaSelfPreferenceMemorySyncAction.Ignored;
            }


            SegaSelfPreferenceMemorySyncAction action =
                result.Action switch
                {
                    SegaMemoryConsolidationAction.Created =>
                        SegaSelfPreferenceMemorySyncAction.Created,

                    SegaMemoryConsolidationAction.Reinforced =>
                        SegaSelfPreferenceMemorySyncAction.Updated,

                    SegaMemoryConsolidationAction.Updated =>
                        SegaSelfPreferenceMemorySyncAction.Updated,

                    SegaMemoryConsolidationAction.Superseded =>
                        SegaSelfPreferenceMemorySyncAction.Superseded,

                    _ =>
                        SegaSelfPreferenceMemorySyncAction.Ignored
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

    private static SegaMemoryCandidate BuildCandidate(
        SegaSelfPreferenceState preference,
        string content,
        string canonicalKey,
        SegaSelfPreferenceObservation? sourceObservation)
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


        SegaMemoryProvenance provenance =
            new SegaMemoryProvenance
            {
                SourceType =
                    SegaMemorySourceType.SegaInference,

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


        return new SegaMemoryCandidate
        {
            Kind =
                SegaMemoryKind.SegaLearnedPreference,

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
    // than storing Sega's exact floating-point state in prose.
    // The authoritative numeric state remains in sega-self.db.
    // =========================================================

    private static string BuildMemoryContent(
        SegaSelfPreferenceState preference)
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
            $"Through repeated meaningful experience, Sega has developed a durable self-preference: " +
            $"she {direction} {preference.Subject}.";
    }


    // =========================================================
    // ASSOCIATIVE PROFILE
    // =========================================================

    private static SegaMemoryAssociationProfile BuildAssociation(
        SegaSelfPreferenceState preference,
        string content)
    {
        return new SegaMemoryAssociationProfile
        {
            RetrievalDescription =
                $"Sega's own learned durable preference about {preference.Subject}. " +
                $"This preference developed gradually through repeated experience. {content}",

            RetrievalCues =
                new[]
                {
                    preference.Subject,
                    preference.Key,
                    "Sega learned preference",
                    "Sega self preference",
                    "what Sega likes",
                    "what Sega dislikes",
                    "what Sega prefers"
                },

            Concepts =
                new[]
                {
                    "Sega self preference",
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
            SegaSelfPreferenceState.NormalizeRequiredKey(
                preferenceKey);
    }


    private static string BuildSourceExcerpt(
        SegaSelfPreferenceObservation? observation,
        SegaSelfPreferenceState preference)
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


public enum SegaSelfPreferenceMemorySyncAction
{
    NotEstablished,

    NoChange,

    Created,

    Updated,

    Superseded,

    Archived,

    Ignored
}
