/*
 * filename: SegaMemoryContextService.cs
 */

using System.Diagnostics;

namespace SegaAgent.Memory.LongTerm;

public sealed class SegaMemoryContextService
{
    private readonly SegaLongTermMemoryService
        _longTermMemory;


    private readonly SegaMemoryAssociationService
        _associations;


    public SegaMemoryContextService(
        SegaLongTermMemoryService longTermMemory,
        SegaMemoryAssociationService associations)
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


    public async Task<SegaMemoryContextSnapshot> BuildAsync(
        int characterBudget,
        CancellationToken cancellationToken = default)
    {
        characterBudget =
            Math.Max(
                1200,
                characterBudget);


        IReadOnlyList<SegaMemoryRecord> memories =
            await _longTermMemory.ReadActiveMemoriesAsync(
                cancellationToken);


        if (memories.Count ==
            0)
        {
            return new SegaMemoryContextSnapshot
            {
                Mode =
                    SegaMemoryContextMode.Full,

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


        IReadOnlyDictionary<Guid, SegaStoredMemoryAssociationProfile> profiles =
            await _associations.ReadActiveProfilesAsync(
                cancellationToken);


        SegaMemoryKnowledgeEntry[] entries =
            memories
                .Select(
                    memory =>
                    {
                        SegaMemoryAssociationProfile association =
                            profiles.TryGetValue(
                                memory.Id,
                                out SegaStoredMemoryAssociationProfile? stored)
                                ? stored.Profile
                                : SegaMemoryAssociationProfile.CreateFallback(
                                    memory);


                        return new SegaMemoryKnowledgeEntry
                        {
                            Memory =
                                memory,

                            Association =
                                association
                        };
                    })
                .ToArray();


        int estimatedFullCharacters =
            SegaCognitionMemoryFormatter.EstimateFullCharacters(
                entries);


        SegaMemoryContextSnapshot result;


        if (estimatedFullCharacters <=
            characterBudget)
        {
            string full =
                SegaCognitionMemoryFormatter.FormatFull(
                    entries);


            if (full.Length <=
                characterBudget)
            {
                result =
                    new SegaMemoryContextSnapshot
                    {
                        Mode =
                            SegaMemoryContextMode.Full,

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


    private static SegaMemoryContextSnapshot BuildMapSnapshot(
        IReadOnlyList<SegaMemoryKnowledgeEntry> memories,
        int characterBudget,
        int estimatedFullCharacters)
    {
        string map =
            SegaCognitionMemoryFormatter.FormatMap(
                memories,
                characterBudget);


        return new SegaMemoryContextSnapshot
        {
            Mode =
                SegaMemoryContextMode.Map,

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
