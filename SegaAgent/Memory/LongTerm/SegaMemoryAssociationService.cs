/*
 * filename: SegaMemoryAssociationService.cs
 */

using System.Diagnostics;

using Microsoft.Extensions.Hosting;

using SegaAgent.Semantic;

namespace SegaAgent.Memory.LongTerm;

// =============================================================
// ASSOCIATIVE MEMORY SERVICE
//
// Owns non-authoritative retrieval structure around durable
// memories:
//
// - retrieval descriptions
// - retrieval cues
// - concepts
// - entities
// - one-hop relationships through shared topic/concept/entity
//
// Authoritative memory content remains in SegaLongTermMemoryStore.
// =============================================================

public sealed class SegaMemoryAssociationService
    : IHostedService
{
    private readonly SegaLongTermMemoryStore
        _memoryStore;


    private readonly SegaMemoryAssociativeIndexStore
        _indexStore;


    private readonly ISegaSemanticEncoder
        _encoder;


    private readonly SemaphoreSlim
        _indexingLock =
            new(
                1,
                1);


    public SegaMemoryAssociationService(
        SegaLongTermMemoryStore memoryStore,
        SegaMemoryAssociativeIndexStore indexStore,
        ISegaSemanticEncoder encoder)
    {
        _memoryStore =
            memoryStore
            ?? throw new ArgumentNullException(
                nameof(memoryStore));


        _indexStore =
            indexStore
            ?? throw new ArgumentNullException(
                nameof(indexStore));


        _encoder =
            encoder
            ?? throw new ArgumentNullException(
                nameof(encoder));
    }


    public async Task StartAsync(
        CancellationToken cancellationToken)
    {
        await _indexStore.InitializeAsync(
            cancellationToken);


        IReadOnlyList<Guid> backfilled =
            await BackfillMissingProfilesAsync(
                cancellationToken);


        foreach (Guid memoryId
                 in backfilled)
        {
            await RebuildLinksForMemoryAsync(
                memoryId,
                cancellationToken);
        }


        IReadOnlyDictionary<Guid, SegaStoredMemoryAssociationProfile> profiles =
            await _indexStore.ReadActiveProfilesAsync(
                cancellationToken);


        Debug.WriteLine(
            $"[MemoryAssociation] READY | Profiles={profiles.Count}");
    }


    public Task StopAsync(
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }


    internal async Task<IReadOnlyDictionary<Guid, SegaStoredMemoryAssociationProfile>>
        ReadActiveProfilesAsync(
            CancellationToken cancellationToken = default)
    {
        await _indexStore.InitializeAsync(
            cancellationToken);


        return await _indexStore.ReadActiveProfilesAsync(
            cancellationToken);
    }


    // =========================================================
    // REPAIR ALL DERIVED ASSOCIATION STATE
    //
    // Association profiles/links are rebuildable retrieval
    // infrastructure. Durable memory content remains authoritative
    // in SegaLongTermMemoryStore.
    //
    // This loads the active memory/profile snapshots once and
    // rebuilds links in O(n^2) comparisons rather than repeatedly
    // re-reading the entire database for every memory.
    // =========================================================

    public async Task<SegaMemoryAssociationRepairResult> RepairAllAsync(
        CancellationToken cancellationToken = default)
    {
        await _indexingLock.WaitAsync(
            cancellationToken);


        try
        {
            await _indexStore.InitializeAsync(
                cancellationToken);


            int pruned =
                await _indexStore.PruneInactiveAsync(
                    cancellationToken);


            IReadOnlyList<Guid> backfilled =
                await BackfillMissingProfilesAsync(
                    cancellationToken);


            IReadOnlyList<SegaStoredMemory> active =
                await _memoryStore.ReadActiveAsync(
                    cancellationToken);


            IReadOnlyDictionary<Guid, SegaStoredMemoryAssociationProfile> profiles =
                await _indexStore.ReadActiveProfilesAsync(
                    cancellationToken);


            Dictionary<Guid, SegaMemoryRecord> memories =
                active.ToDictionary(
                    item =>
                        item.Memory.Id,
                    item =>
                        item.Memory.Normalize());


            int relinked =
                0;


            foreach (Guid memoryId
                     in memories.Keys)
            {
                cancellationToken.ThrowIfCancellationRequested();


                IReadOnlyList<SegaMemoryAssociationLink> links =
                    BuildLinksForMemory(
                        memoryId,
                        memories,
                        profiles);


                await _indexStore.ReplaceLinksForMemoryAsync(
                    memoryId,
                    links,
                    cancellationToken);


                relinked++;
            }


            Debug.WriteLine(
                $"[MemoryAssociation] REPAIR | " +
                $"Active={memories.Count} | " +
                $"Pruned={pruned} | " +
                $"Backfilled={backfilled.Count} | " +
                $"Relinked={relinked}");


            return new SegaMemoryAssociationRepairResult
            {
                ActiveMemoryCount =
                    memories.Count,

                InactiveIndexRowsPruned =
                    pruned,

                ProfilesBackfilled =
                    backfilled.Count,

                MemoriesRelinked =
                    relinked
            };
        }
        finally
        {
            _indexingLock.Release();
        }
    }


    public async Task ApplyConsolidationAsync(
        IReadOnlyList<SegaMemoryConsolidationResult> results,
        CancellationToken cancellationToken = default)
    {
        if (
            results ==
                null
            ||
            results.Count ==
                0)
        {
            return;
        }


        await _indexingLock.WaitAsync(
            cancellationToken);


        try
        {
            foreach (
                SegaMemoryConsolidationResult result
                in results)
            {
                cancellationToken.ThrowIfCancellationRequested();


                if (
                    result.Action ==
                        SegaMemoryConsolidationAction.Ignored
                    ||
                    result.Memory ==
                        null)
                {
                    continue;
                }


                SegaMemoryRecord memory =
                    result.Memory.Normalize();


                SegaMemoryAssociationProfile proposed =
                    (result.Candidate.Association
                        ?? new SegaMemoryAssociationProfile())
                    .Normalize();


                SegaStoredMemoryAssociationProfile? existing =
                    await _indexStore.ReadProfileAsync(
                        memory.Id,
                        cancellationToken);


                SegaMemoryAssociationProfile merged =
                    existing ==
                        null
                        ? SegaMemoryAssociationProfile
                            .CreateFallback(
                                memory)
                            .Merge(
                                proposed)
                        : existing.Profile
                            .Merge(
                                proposed);


                await UpsertProfileAsync(
                    memory,
                    merged,
                    cancellationToken);


                if (
                    result.Action ==
                        SegaMemoryConsolidationAction.Superseded
                    &&
                    result.PreviousMemoryId.HasValue)
                {
                    await _indexStore.DeleteLinksForMemoryAsync(
                        result.PreviousMemoryId.Value,
                        cancellationToken);
                }


                await RebuildLinksForMemoryAsync(
                    memory.Id,
                    cancellationToken);
            }
        }
        finally
        {
            _indexingLock.Release();
        }
    }


    internal Task<IReadOnlyList<SegaMemoryAssociationLink>>
        ReadRelatedAsync(
            IReadOnlyCollection<Guid> seedMemoryIds,
            int maximumResults,
            CancellationToken cancellationToken = default)
    {
        return _indexStore.ReadRelatedAsync(
            seedMemoryIds,
            maximumResults,
            cancellationToken);
    }


    private async Task<IReadOnlyList<Guid>> BackfillMissingProfilesAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SegaStoredMemory> active =
            await _memoryStore.ReadActiveAsync(
                cancellationToken);


        IReadOnlyDictionary<Guid, SegaStoredMemoryAssociationProfile> profiles =
            await _indexStore.ReadActiveProfilesAsync(
                cancellationToken);


        List<Guid> created =
            new();


        foreach (
            SegaStoredMemory stored
            in active)
        {
            if (profiles.ContainsKey(
                    stored.Memory.Id))
            {
                continue;
            }


            SegaMemoryAssociationProfile fallback =
                SegaMemoryAssociationProfile.CreateFallback(
                    stored.Memory);


            await UpsertProfileAsync(
                stored.Memory,
                fallback,
                cancellationToken);


            created.Add(
                stored.Memory.Id);
        }


        if (created.Count >
            0)
        {
            Debug.WriteLine(
                $"[MemoryAssociation] BACKFILL | Created={created.Count}");
        }


        return created;
    }


    private async Task UpsertProfileAsync(
        SegaMemoryRecord memory,
        SegaMemoryAssociationProfile profile,
        CancellationToken cancellationToken)
    {
        string retrievalText =
            profile.BuildRetrievalText(
                memory);


        SemanticEmbedding retrievalEmbedding =
            _encoder.Encode(
                retrievalText);


        await _indexStore.UpsertProfileAsync(
            memory.Id,
            profile,
            retrievalEmbedding,
            cancellationToken);


        Debug.WriteLine(
            $"[MemoryAssociation] INDEXED | " +
            $"Memory={memory.Id} | " +
            $"Cues={profile.RetrievalCues.Count} | " +
            $"Concepts={profile.Concepts.Count} | " +
            $"Entities={profile.Entities.Count}");
    }


    private async Task RebuildLinksForMemoryAsync(
        Guid memoryId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SegaStoredMemory> active =
            await _memoryStore.ReadActiveAsync(
                cancellationToken);


        Dictionary<Guid, SegaMemoryRecord> memories =
            active.ToDictionary(
                item =>
                    item.Memory.Id,
                item =>
                    item.Memory.Normalize());


        if (!memories.TryGetValue(
                memoryId,
                out SegaMemoryRecord? sourceMemory))
        {
            await _indexStore.DeleteLinksForMemoryAsync(
                memoryId,
                cancellationToken);


            return;
        }


        IReadOnlyDictionary<Guid, SegaStoredMemoryAssociationProfile> profiles =
            await _indexStore.ReadActiveProfilesAsync(
                cancellationToken);


        if (!profiles.TryGetValue(
                memoryId,
                out SegaStoredMemoryAssociationProfile? sourceProfile))
        {
            return;
        }


        List<SegaMemoryAssociationLink> links =
            new();


        foreach (
            KeyValuePair<Guid, SegaMemoryRecord> pair
            in memories)
        {
            Guid targetId =
                pair.Key;


            if (targetId ==
                memoryId)
            {
                continue;
            }


            SegaMemoryRecord targetMemory =
                pair.Value;


            if (!profiles.TryGetValue(
                    targetId,
                    out SegaStoredMemoryAssociationProfile? targetProfile))
            {
                continue;
            }


            if (
                !string.IsNullOrWhiteSpace(
                    sourceMemory.TopicKey)
                &&
                !string.IsNullOrWhiteSpace(
                    targetMemory.TopicKey)
                &&
                string.Equals(
                    sourceMemory.TopicKey,
                    targetMemory.TopicKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                AddSymmetricLink(
                    links,
                    memoryId,
                    targetId,
                    SegaMemoryRelationKind.SharedTopic,
                    sourceMemory.TopicKey!,
                    0.84);
            }


            foreach (
                string concept
                in Intersect(
                    sourceProfile.Profile.Concepts,
                    targetProfile.Profile.Concepts))
            {
                AddSymmetricLink(
                    links,
                    memoryId,
                    targetId,
                    SegaMemoryRelationKind.SharedConcept,
                    concept,
                    0.88);
            }


            foreach (
                string entity
                in Intersect(
                    sourceProfile.Profile.Entities,
                    targetProfile.Profile.Entities))
            {
                AddSymmetricLink(
                    links,
                    memoryId,
                    targetId,
                    SegaMemoryRelationKind.SharedEntity,
                    entity,
                    0.96);
            }
        }


        await _indexStore.ReplaceLinksForMemoryAsync(
            memoryId,
            links,
            cancellationToken);


        int outgoing =
            links.Count(
                link =>
                    link.SourceMemoryId ==
                    memoryId);


        Debug.WriteLine(
            $"[MemoryAssociation] LINKS | " +
            $"Memory={memoryId} | Outgoing={outgoing}");
    }


    private static IReadOnlyList<SegaMemoryAssociationLink>
        BuildLinksForMemory(
            Guid memoryId,
            IReadOnlyDictionary<Guid, SegaMemoryRecord> memories,
            IReadOnlyDictionary<Guid, SegaStoredMemoryAssociationProfile> profiles)
    {
        if (!memories.TryGetValue(
                memoryId,
                out SegaMemoryRecord? sourceMemory)
            ||
            !profiles.TryGetValue(
                memoryId,
                out SegaStoredMemoryAssociationProfile? sourceProfile))
        {
            return Array.Empty<SegaMemoryAssociationLink>();
        }


        List<SegaMemoryAssociationLink> links =
            new();


        foreach (KeyValuePair<Guid, SegaMemoryRecord> pair
                 in memories)
        {
            Guid targetId =
                pair.Key;


            if (targetId ==
                memoryId)
            {
                continue;
            }


            if (!profiles.TryGetValue(
                    targetId,
                    out SegaStoredMemoryAssociationProfile? targetProfile))
            {
                continue;
            }


            SegaMemoryRecord targetMemory =
                pair.Value;


            if (
                !string.IsNullOrWhiteSpace(
                    sourceMemory.TopicKey)
                &&
                !string.IsNullOrWhiteSpace(
                    targetMemory.TopicKey)
                &&
                string.Equals(
                    sourceMemory.TopicKey,
                    targetMemory.TopicKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                AddSymmetricLink(
                    links,
                    memoryId,
                    targetId,
                    SegaMemoryRelationKind.SharedTopic,
                    sourceMemory.TopicKey!,
                    0.84);
            }


            foreach (string concept
                     in Intersect(
                         sourceProfile.Profile.Concepts,
                         targetProfile.Profile.Concepts))
            {
                AddSymmetricLink(
                    links,
                    memoryId,
                    targetId,
                    SegaMemoryRelationKind.SharedConcept,
                    concept,
                    0.88);
            }


            foreach (string entity
                     in Intersect(
                         sourceProfile.Profile.Entities,
                         targetProfile.Profile.Entities))
            {
                AddSymmetricLink(
                    links,
                    memoryId,
                    targetId,
                    SegaMemoryRelationKind.SharedEntity,
                    entity,
                    0.96);
            }
        }


        return links;
    }


    private static void AddSymmetricLink(
        List<SegaMemoryAssociationLink> links,
        Guid left,
        Guid right,
        SegaMemoryRelationKind relation,
        string sharedValue,
        double strength)
    {
        links.Add(
            new SegaMemoryAssociationLink
            {
                SourceMemoryId =
                    left,

                TargetMemoryId =
                    right,

                RelationKind =
                    relation,

                SharedValue =
                    sharedValue,

                Strength =
                    strength
            });


        links.Add(
            new SegaMemoryAssociationLink
            {
                SourceMemoryId =
                    right,

                TargetMemoryId =
                    left,

                RelationKind =
                    relation,

                SharedValue =
                    sharedValue,

                Strength =
                    strength
            });
    }


    private static IEnumerable<string> Intersect(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right)
    {
        if (
            left.Count ==
                0
            ||
            right.Count ==
                0)
        {
            return Array.Empty<string>();
        }


        HashSet<string> rightSet =
            new(
                right,
                StringComparer.OrdinalIgnoreCase);


        return left
            .Where(
                rightSet.Contains)
            .Distinct(
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}