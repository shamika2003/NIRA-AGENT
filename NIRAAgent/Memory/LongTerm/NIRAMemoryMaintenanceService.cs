/*
 * filename: NIRAMemoryMaintenanceService.cs
 */

using System.Diagnostics;
using System.Text;

using Microsoft.Extensions.Hosting;

namespace NIRAAgent.Memory.LongTerm;


// =============================================================
// MEMORY INTEGRITY / MAINTENANCE
//
// Runs once during application startup after the authoritative
// long-term-memory store and associative index are available.
//
// Maintenance is deliberately conservative:
//
// - corruption blocks writes instead of being "repaired" by a
//   guess
// - uncertain semantic conflicts are never silently rewritten
// - only exact, structurally compatible duplicates are merged
// - only explicitly-marked non-user test artifacts are archived
// - association profiles/links are rebuildable derived state
// =============================================================

public sealed class NIRAMemoryMaintenanceService
    : IHostedService
{
    private readonly NIRALongTermMemoryStore
        _store;


    private readonly NIRAMemoryAssociationService
        _associations;


    public NIRAMemoryMaintenanceReport? LastReport
    {
        get;
        private set;
    }


    public NIRAMemoryMaintenanceService(
        NIRALongTermMemoryStore store,
        NIRAMemoryAssociationService associations)
    {
        _store =
            store
            ?? throw new ArgumentNullException(
                nameof(store));


        _associations =
            associations
            ?? throw new ArgumentNullException(
                nameof(associations));
    }


    public async Task StartAsync(
        CancellationToken cancellationToken)
    {
        NIRAMemoryDatabaseIntegrityResult integrity =
            await _store.CheckIntegrityAsync(
                cancellationToken);


        if (!integrity.IsHealthy)
        {
            Debug.WriteLine(
                $"[MemoryMaintenance] BLOCKED | " +
                $"QuickCheck='{integrity.QuickCheckResult}' | " +
                $"ForeignKeyViolations={integrity.ForeignKeyViolationCount}");


            throw new InvalidOperationException(
                "NIRA long-term memory database failed its integrity check. " +
                "Maintenance writes were stopped so uncertain/corrupt memory is not silently rewritten.");
        }


        IReadOnlyList<NIRAStoredMemory> active =
            await _store.ReadActiveAsync(
                cancellationToken);


        int activeBefore =
            active.Count;


        int canonicalConflictGroups =
            CountCanonicalConflictGroups(
                active);


        int archivedTests =
            await ArchiveObviousTestArtifactsAsync(
                active,
                cancellationToken);


        if (archivedTests >
            0)
        {
            active =
                await _store.ReadActiveAsync(
                    cancellationToken);
        }


        IReadOnlyList<IReadOnlyList<NIRAStoredMemory>> duplicateGroups =
            FindExactDuplicateGroups(
                active);


        int merged =
            await MergeExactDuplicateGroupsAsync(
                duplicateGroups,
                cancellationToken);


        if (merged >
            0)
        {
            active =
                await _store.ReadActiveAsync(
                    cancellationToken);
        }


        int sensitiveActive =
            CountSensitiveActiveMemories(
                active);


        NIRAMemoryAssociationRepairResult associationRepair =
            await _associations.RepairAllAsync(
                cancellationToken);


        NIRAMemoryMaintenanceReport report =
            new()
            {
                CompletedAt =
                    DateTimeOffset.UtcNow,

                Integrity =
                    integrity,

                ActiveBefore =
                    activeBefore,

                ActiveAfter =
                    active.Count,

                CanonicalConflictGroups =
                    canonicalConflictGroups,

                ExactDuplicateGroups =
                    duplicateGroups.Count,

                ExactDuplicatesMerged =
                    merged,

                TestArtifactsArchived =
                    archivedTests,

                SensitiveActiveMemories =
                    sensitiveActive,

                AssociationRepair =
                    associationRepair
            };


        LastReport =
            report;


        Debug.WriteLine(
            $"[MemoryMaintenance] READY | " +
            $"Integrity=ok | " +
            $"Active={report.ActiveAfter} | " +
            $"CanonicalConflicts={report.CanonicalConflictGroups} | " +
            $"DuplicateGroups={report.ExactDuplicateGroups} | " +
            $"DuplicatesMerged={report.ExactDuplicatesMerged} | " +
            $"TestArchived={report.TestArtifactsArchived} | " +
            $"SensitiveActive={report.SensitiveActiveMemories} | " +
            $"IndexRowsPruned={associationRepair.InactiveIndexRowsPruned} | " +
            $"ProfilesBackfilled={associationRepair.ProfilesBackfilled} | " +
            $"MemoriesRelinked={associationRepair.MemoriesRelinked}");
    }


    public Task StopAsync(
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }


    private static int CountCanonicalConflictGroups(
        IReadOnlyList<NIRAStoredMemory> active)
    {
        return active
            .Where(
                item =>
                    !string.IsNullOrWhiteSpace(
                        item.Memory.CanonicalKey))
            .GroupBy(
                item =>
                    item.Memory.CanonicalKey!,
                StringComparer.OrdinalIgnoreCase)
            .Count(
                group =>
                    group
                        .Select(
                            item =>
                                NormalizeComparisonText(
                                    item.Memory.Content))
                        .Distinct(
                            StringComparer.Ordinal)
                        .Skip(
                            1)
                        .Any());
    }


    private static int CountSensitiveActiveMemories(
        IReadOnlyList<NIRAStoredMemory> active)
    {
        return active.Count(
            item =>
                NIRASensitiveMemoryPolicy.ContainsSensitiveSecret(
                    item.Memory.Content)
                ||
                NIRASensitiveMemoryPolicy.ContainsSensitiveSecret(
                    item.Memory.Provenance.SourceExcerpt));
    }


    private async Task<int> ArchiveObviousTestArtifactsAsync(
        IReadOnlyList<NIRAStoredMemory> active,
        CancellationToken cancellationToken)
    {
        int archived =
            0;


        foreach (NIRAStoredMemory stored
                 in active)
        {
            cancellationToken.ThrowIfCancellationRequested();


            NIRAMemoryRecord memory =
                stored.Memory.Normalize();


            if (!IsObviousNonUserTestArtifact(
                    memory))
            {
                continue;
            }


            if (await _store.ArchiveActiveByIdAsync(
                    memory.Id,
                    cancellationToken))
            {
                archived++;


                Debug.WriteLine(
                    $"[MemoryMaintenance] ARCHIVED TEST | " +
                    $"Id={memory.Id} | Kind={memory.Kind}");
            }
        }


        return archived;
    }


    private async Task<int> MergeExactDuplicateGroupsAsync(
        IReadOnlyList<IReadOnlyList<NIRAStoredMemory>> groups,
        CancellationToken cancellationToken)
    {
        int merged =
            0;


        foreach (IReadOnlyList<NIRAStoredMemory> group
                 in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();


            NIRAStoredMemory keeper =
                ChooseKeeper(
                    group);


            foreach (NIRAStoredMemory duplicate
                     in group)
            {
                if (duplicate.Memory.Id ==
                    keeper.Memory.Id)
                {
                    continue;
                }


                if (!MetadataCompatible(
                        keeper.Memory,
                        duplicate.Memory))
                {
                    continue;
                }


                bool changed =
                    await _store.MergeExactDuplicateAsync(
                        keeper.Memory.Id,
                        duplicate.Memory.Id,
                        cancellationToken);


                if (!changed)
                {
                    continue;
                }


                merged++;


                Debug.WriteLine(
                    $"[MemoryMaintenance] MERGED DUPLICATE | " +
                    $"Keeper={keeper.Memory.Id} | " +
                    $"Archived={duplicate.Memory.Id}");
            }
        }


        return merged;
    }


    private static IReadOnlyList<IReadOnlyList<NIRAStoredMemory>>
        FindExactDuplicateGroups(
            IReadOnlyList<NIRAStoredMemory> active)
    {
        return active
            .GroupBy(
                item =>
                    new ExactDuplicateKey(
                        item.Memory.Kind,
                        NormalizeComparisonText(
                            item.Memory.Content)))
            .Where(
                group =>
                    group.Count() >
                    1)
            .Select(
                group =>
                    (IReadOnlyList<NIRAStoredMemory>)
                    group.ToArray())
            .ToArray();
    }


    private static NIRAStoredMemory ChooseKeeper(
        IReadOnlyList<NIRAStoredMemory> group)
    {
        return group
            .OrderByDescending(
                item =>
                    !string.IsNullOrWhiteSpace(
                        item.Memory.CanonicalKey))
            .ThenByDescending(
                item =>
                    !string.IsNullOrWhiteSpace(
                        item.Memory.TopicKey))
            .ThenByDescending(
                item =>
                    SourceAuthority(
                        item.Memory.Provenance.SourceType))
            .ThenByDescending(
                item =>
                    item.Memory.Confidence)
            .ThenByDescending(
                item =>
                    item.Memory.ReinforcementCount)
            .ThenBy(
                item =>
                    item.Memory.CreatedAt)
            .First();
    }


    private static bool MetadataCompatible(
        NIRAMemoryRecord left,
        NIRAMemoryRecord right)
    {
        bool canonicalCompatible =
            string.IsNullOrWhiteSpace(
                left.CanonicalKey)
            ||
            string.IsNullOrWhiteSpace(
                right.CanonicalKey)
            ||
            string.Equals(
                left.CanonicalKey,
                right.CanonicalKey,
                StringComparison.OrdinalIgnoreCase);


        bool topicCompatible =
            string.IsNullOrWhiteSpace(
                left.TopicKey)
            ||
            string.IsNullOrWhiteSpace(
                right.TopicKey)
            ||
            string.Equals(
                left.TopicKey,
                right.TopicKey,
                StringComparison.OrdinalIgnoreCase);


        return canonicalCompatible &&
            topicCompatible;
    }


    private static bool IsObviousNonUserTestArtifact(
        NIRAMemoryRecord memory)
    {
        if (
            memory.Provenance.SourceType ==
                NIRAMemorySourceType.UserExplicit
            ||
            memory.Kind ==
                NIRAMemoryKind.UserFact
            ||
            memory.Kind ==
                NIRAMemoryKind.UserPreference)
        {
            return false;
        }


        if (HasTestKeyPrefix(
                memory.CanonicalKey)
            ||
            HasTestKeyPrefix(
                memory.TopicKey))
        {
            return true;
        }


        string content =
            memory.Content.TrimStart();


        return
            content.StartsWith(
                "[TEST]",
                StringComparison.OrdinalIgnoreCase)
            ||
            content.StartsWith(
                "TEST MEMORY:",
                StringComparison.OrdinalIgnoreCase)
            ||
            content.StartsWith(
                "MEMORY TEST:",
                StringComparison.OrdinalIgnoreCase)
            ||
            content.StartsWith(
                "TEST_ONLY:",
                StringComparison.OrdinalIgnoreCase);
    }


    private static bool HasTestKeyPrefix(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return false;
        }


        string key =
            value.Trim();


        return
            key.StartsWith(
                "test.",
                StringComparison.OrdinalIgnoreCase)
            ||
            key.StartsWith(
                "debug.",
                StringComparison.OrdinalIgnoreCase)
            ||
            key.StartsWith(
                "memory.test.",
                StringComparison.OrdinalIgnoreCase)
            ||
            key.StartsWith(
                "NIRA.test.",
                StringComparison.OrdinalIgnoreCase);
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


    private static string NormalizeComparisonText(
        string value)
    {
        StringBuilder builder =
            new();


        bool pendingSpace =
            false;


        foreach (char character
                 in value.Trim())
        {
            if (char.IsWhiteSpace(
                    character))
            {
                pendingSpace =
                    builder.Length >
                    0;


                continue;
            }


            if (pendingSpace)
            {
                builder.Append(
                    ' ');


                pendingSpace =
                    false;
            }


            builder.Append(
                char.ToLowerInvariant(
                    character));
        }


        return builder.ToString();
    }


    private readonly record struct ExactDuplicateKey(
        NIRAMemoryKind Kind,
        string Content);
}

