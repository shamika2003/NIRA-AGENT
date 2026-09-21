/*
 * filename: NIRAMemoryAssociationService.cs
 */

using System.Diagnostics;

using Microsoft.Extensions.Hosting;

using NIRAAgent.Semantic;

namespace NIRAAgent.Memory.LongTerm;

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
// Authoritative memory content remains in NIRALongTermMemoryStore.
// =============================================================

public sealed class NIRAMemoryAssociationService
    : IHostedService
{
    private readonly NIRALongTermMemoryStore
        _memoryStore;


    private readonly NIRAMemoryAssociativeIndexStore
        _indexStore;


    private readonly INIRASemanticEncoder
        _encoder;


    private readonly SemaphoreSlim
        _indexingLock =
            new(
                1,
                1);


    public NIRAMemoryAssociationService(
        NIRALongTermMemoryStore memoryStore,
        NIRAMemoryAssociativeIndexStore indexStore,
        INIRASemanticEncoder encoder)
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


        IReadOnlyDictionary<Guid, NIRAStoredMemoryAssociationProfile> profiles =
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


    internal async Task<IReadOnlyDictionary<Guid, NIRAStoredMemoryAssociationProfile>>
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
    // in NIRALongTermMemoryStore.
    //
    // This loads the active memory/profile snapshots once and
    // rebuilds links in O(n^2) comparisons rather than repeatedly
    // re-reading the entire database for every memory.
    // =========================================================

    public async Task<NIRAMemoryAssociationRepairResult> RepairAllAsync(
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


            IReadOnlyList<NIRAStoredMemory> active =
                await _memoryStore.ReadActiveAsync(
                    cancellationToken);


            IReadOnlyDictionary<Guid, NIRAStoredMemoryAssociationProfile> profiles =
                await _indexStore.ReadActiveProfilesAsync(
                    cancellationToken);


            Dictionary<Guid, NIRAMemoryRecord> memories =
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


                IReadOnlyList<NIRAMemoryAssociationLink> links =
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


            return new NIRAMemoryAssociationRepairResult
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
        IReadOnlyList<NIRAMemoryConsolidationResult> results,
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
                NIRAMemoryConsolidationResult result
                in results)
            {
                cancellationToken.ThrowIfCancellationRequested();


                if (
                    result.Action ==
                        NIRAMemoryConsolidationAction.Ignored
                    ||
                    result.Memory ==
                        null)
                {
                    continue;
                }


                NIRAMemoryRecord memory =
                    result.Memory.Normalize();


                NIRAMemoryAssociationProfile proposed =
                    (result.Candidate.Association
                        ?? new NIRAMemoryAssociationProfile())
                    .Normalize();


                NIRAStoredMemoryAssociationProfile? existing =
                    await _indexStore.ReadProfileAsync(
                        memory.Id,
                        cancellationToken);


                NIRAMemoryAssociationProfile merged =
                    existing ==
                        null
                        ? NIRAMemoryAssociationProfile
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
                        NIRAMemoryConsolidationAction.Superseded
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


    internal Task<IReadOnlyList<NIRAMemoryAssociationLink>>
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
        IReadOnlyList<NIRAStoredMemory> active =
            await _memoryStore.ReadActiveAsync(
                cancellationToken);


        IReadOnlyDictionary<Guid, NIRAStoredMemoryAssociationProfile> profiles =
            await _indexStore.ReadActiveProfilesAsync(
                cancellationToken);


        List<Guid> created =
            new();


        foreach (
            NIRAStoredMemory stored
            in active)
        {
            if (profiles.ContainsKey(
                    stored.Memory.Id))
            {
                continue;
            }


            NIRAMemoryAssociationProfile fallback =
                NIRAMemoryAssociationProfile.CreateFallback(
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
        NIRAMemoryRecord memory,
        NIRAMemoryAssociationProfile profile,
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
        IReadOnlyList<NIRAStoredMemory> active =
            await _memoryStore.ReadActiveAsync(
                cancellationToken);


        Dictionary<Guid, NIRAMemoryRecord> memories =
            active.ToDictionary(
                item =>
                    item.Memory.Id,
                item =>
                    item.Memory.Normalize());


        if (!memories.TryGetValue(
                memoryId,
                out NIRAMemoryRecord? sourceMemory))
        {
            await _indexStore.DeleteLinksForMemoryAsync(
                memoryId,
                cancellationToken);


            return;
        }


        IReadOnlyDictionary<Guid, NIRAStoredMemoryAssociationProfile> profiles =
            await _indexStore.ReadActiveProfilesAsync(
                cancellationToken);


        if (!profiles.TryGetValue(
                memoryId,
                out NIRAStoredMemoryAssociationProfile? sourceProfile))
        {
            return;
        }


        List<NIRAMemoryAssociationLink> links =
            new();


        foreach (
            KeyValuePair<Guid, NIRAMemoryRecord> pair
            in memories)
        {
            Guid targetId =
                pair.Key;


            if (targetId ==
                memoryId)
            {
                continue;
            }


            NIRAMemoryRecord targetMemory =
                pair.Value;


            if (!profiles.TryGetValue(
                    targetId,
                    out NIRAStoredMemoryAssociationProfile? targetProfile))
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
                    NIRAMemoryRelationKind.SharedTopic,
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
                    NIRAMemoryRelationKind.SharedConcept,
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
                    NIRAMemoryRelationKind.SharedEntity,
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


    private static IReadOnlyList<NIRAMemoryAssociationLink>
        BuildLinksForMemory(
            Guid memoryId,
            IReadOnlyDictionary<Guid, NIRAMemoryRecord> memories,
            IReadOnlyDictionary<Guid, NIRAStoredMemoryAssociationProfile> profiles)
    {
        if (!memories.TryGetValue(
                memoryId,
                out NIRAMemoryRecord? sourceMemory)
            ||
            !profiles.TryGetValue(
                memoryId,
                out NIRAStoredMemoryAssociationProfile? sourceProfile))
        {
            return Array.Empty<NIRAMemoryAssociationLink>();
        }


        List<NIRAMemoryAssociationLink> links =
            new();


        foreach (KeyValuePair<Guid, NIRAMemoryRecord> pair
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
                    out NIRAStoredMemoryAssociationProfile? targetProfile))
            {
                continue;
            }


            NIRAMemoryRecord targetMemory =
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
                    NIRAMemoryRelationKind.SharedTopic,
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
                    NIRAMemoryRelationKind.SharedConcept,
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
                    NIRAMemoryRelationKind.SharedEntity,
                    entity,
                    0.96);
            }
        }


        return links;
    }


    private static void AddSymmetricLink(
        List<NIRAMemoryAssociationLink> links,
        Guid left,
        Guid right,
        NIRAMemoryRelationKind relation,
        string sharedValue,
        double strength)
    {
        links.Add(
            new NIRAMemoryAssociationLink
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
            new NIRAMemoryAssociationLink
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
