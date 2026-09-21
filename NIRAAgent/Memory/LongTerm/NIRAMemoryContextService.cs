/*
 * filename: NIRAMemoryContextService.cs
 */

using System.Diagnostics;

namespace NIRAAgent.Memory.LongTerm;

public sealed class NIRAMemoryContextService
{
    private readonly NIRALongTermMemoryService
        _longTermMemory;


    private readonly NIRAMemoryAssociationService
        _associations;


    public NIRAMemoryContextService(
        NIRALongTermMemoryService longTermMemory,
        NIRAMemoryAssociationService associations)
    {
        _longTermMemory =
            longTermMemory
            ?? throw new ArgumentNullException(
                nameof(longTermMemory));


        _associations =
            associations
            ?? throw new ArgumentNullException(
                nameof(associations));
    }


    public async Task<NIRAMemoryContextSnapshot> BuildAsync(
        int characterBudget,
        CancellationToken cancellationToken = default)
    {
        characterBudget =
            Math.Max(
                1200,
                characterBudget);


        IReadOnlyList<NIRAMemoryRecord> memories =
            await _longTermMemory.ReadActiveMemoriesAsync(
                cancellationToken);


        if (memories.Count ==
            0)
        {
            return new NIRAMemoryContextSnapshot
            {
                Mode =
                    NIRAMemoryContextMode.Full,

                ActiveCount =
                    0,

                CharacterBudget =
                    characterBudget,

                EstimatedFullCharacters =
                    0,

                Content =
                    "No active long-term memory is currently stored."
            };
        }


        IReadOnlyDictionary<Guid, NIRAStoredMemoryAssociationProfile> profiles =
            await _associations.ReadActiveProfilesAsync(
                cancellationToken);


        NIRAMemoryKnowledgeEntry[] entries =
            memories
                .Select(
                    memory =>
                    {
                        NIRAMemoryAssociationProfile association =
                            profiles.TryGetValue(
                                memory.Id,
                                out NIRAStoredMemoryAssociationProfile? stored)
                                ? stored.Profile
                                : NIRAMemoryAssociationProfile.CreateFallback(
                                    memory);


                        return new NIRAMemoryKnowledgeEntry
                        {
                            Memory =
                                memory,

                            Association =
                                association
                        };
                    })
                .ToArray();


        int estimatedFullCharacters =
            NIRACognitionMemoryFormatter.EstimateFullCharacters(
                entries);


        NIRAMemoryContextSnapshot result;


        if (estimatedFullCharacters <=
            characterBudget)
        {
            string full =
                NIRACognitionMemoryFormatter.FormatFull(
                    entries);


            if (full.Length <=
                characterBudget)
            {
                result =
                    new NIRAMemoryContextSnapshot
                    {
                        Mode =
                            NIRAMemoryContextMode.Full,

                        ActiveCount =
                            memories.Count,

                        CharacterBudget =
                            characterBudget,

                        EstimatedFullCharacters =
                            full.Length,

                        Content =
                            full
                    };
            }
            else
            {
                result =
                    BuildMapSnapshot(
                        entries,
                        characterBudget,
                        full.Length);
            }
        }
        else
        {
            result =
                BuildMapSnapshot(
                    entries,
                    characterBudget,
                    estimatedFullCharacters);
        }


        Debug.WriteLine(
            $"[MemoryContext] " +
            $"Mode={result.Mode} | " +
            $"Active={result.ActiveCount} | " +
            $"Budget={result.CharacterBudget} | " +
            $"FullChars={result.EstimatedFullCharacters} | " +
            $"SentChars={result.Content.Length}");


        return result;
    }


    private static NIRAMemoryContextSnapshot BuildMapSnapshot(
        IReadOnlyList<NIRAMemoryKnowledgeEntry> memories,
        int characterBudget,
        int estimatedFullCharacters)
    {
        string map =
            NIRACognitionMemoryFormatter.FormatMap(
                memories,
                characterBudget);


        return new NIRAMemoryContextSnapshot
        {
            Mode =
                NIRAMemoryContextMode.Map,

            ActiveCount =
                memories.Count,

            CharacterBudget =
                characterBudget,

            EstimatedFullCharacters =
                estimatedFullCharacters,

            Content =
                map
        };
    }
}

