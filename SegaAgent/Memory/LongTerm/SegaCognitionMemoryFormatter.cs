/*
 * filename: SegaCognitionMemoryFormatter.cs
 */

using System.Text;
using System.Text.Json;

namespace SegaAgent.Memory.LongTerm;

public static class SegaCognitionMemoryFormatter
{
    private const int MapContentPreviewCharacters =
        180;


    public static int EstimateFullCharacters(
        IReadOnlyList<SegaMemoryKnowledgeEntry>? memories)
    {
        if (
            memories ==
                null
            ||
            memories.Count ==
                0)
        {
            return 0;
        }


        long estimate =
            320;


        foreach (
            SegaMemoryKnowledgeEntry entry
            in memories)
        {
            SegaMemoryRecord memory =
                entry.Memory;


            SegaMemoryAssociationProfile association =
                entry.Association.Normalize();


            estimate +=
                190
                +
                memory.Content.Length
                +
                (memory.CanonicalKey?.Length ?? 0)
                +
                (memory.TopicKey?.Length ?? 0)
                +
                (association.RetrievalDescription?.Length ?? 0)
                +
                association.RetrievalCues.Sum(
                    value =>
                        value.Length +
                        4)
                +
                association.Concepts.Sum(
                    value =>
                        value.Length +
                        4)
                +
                association.Entities.Sum(
                    value =>
                        value.Length +
                        4);


            if (estimate >=
                int.MaxValue)
            {
                return int.MaxValue;
            }
        }


        return (int)estimate;
    }


    public static string FormatFull(
        IReadOnlyList<SegaMemoryKnowledgeEntry>? memories)
    {
        if (
            memories ==
                null
            ||
            memories.Count ==
                0)
        {
            return
                "No active long-term memory is currently stored.";
        }


        StringBuilder builder =
            new();


        builder.AppendLine(
            "MEMORY CONTEXT MODE: FULL");


        builder.AppendLine(
            $"Active durable memories: {memories.Count}");


        builder.AppendLine(
            "Every active durable memory currently fits inside the cognition memory budget.");


        builder.AppendLine(
            "Content is authoritative remembered data. Retrieval descriptions/cues/concepts/entities are navigation metadata around that memory, not additional facts.");


        builder.AppendLine();


        for (
            int index = 0;
            index < memories.Count;
            index++)
        {
            AppendMemory(
                builder,
                memories[index].Memory,
                memories[index].Association,
                $"MEMORY {index + 1}");


            if (index <
                memories.Count -
                    1)
            {
                builder.AppendLine();
            }
        }


        return builder
            .ToString()
            .TrimEnd();
    }


    public static string FormatMap(
        IReadOnlyList<SegaMemoryKnowledgeEntry> memories,
        int characterBudget)
    {
        ArgumentNullException.ThrowIfNull(
            memories);


        characterBudget =
            Math.Max(
                1200,
                characterBudget);


        StringBuilder builder =
            new();


        AppendWithinBudget(
            builder,
            "MEMORY CONTEXT MODE: MAP\n",
            characterBudget);


        AppendWithinBudget(
            builder,
            $"Active durable memories: {memories.Count}\n",
            characterBudget);


        AppendWithinBudget(
            builder,
            "The complete durable-memory set is larger than this cognition memory budget. This map is navigation only.\n",
            characterBudget);


        AppendWithinBudget(
            builder,
            "Absence from this map never proves Sega lacks a memory. Search when exact remembered content could matter.\n\n",
            characterBudget);


        AppendWithinBudget(
            builder,
            "KINDS\n",
            characterBudget);


        foreach (
            IGrouping<SegaMemoryKind, SegaMemoryKnowledgeEntry> group
            in memories
                .GroupBy(
                    entry =>
                        entry.Memory.Kind)
                .OrderBy(
                    group =>
                        group.Key))
        {
            if (!AppendWithinBudget(
                    builder,
                    $"- {group.Key}: {group.Count()}\n",
                    characterBudget))
            {
                return FinishBudgeted(
                    builder,
                    characterBudget);
            }
        }


        AppendInventory(
            builder,
            "TOPICS",
            memories
                .Select(
                    entry =>
                        entry.Memory.TopicKey)
                .Where(
                    value =>
                        !string.IsNullOrWhiteSpace(
                            value))
                .Select(
                    value =>
                        value!),
            characterBudget);


        AppendInventory(
            builder,
            "CONCEPTS",
            memories.SelectMany(
                entry =>
                    entry.Association.Concepts),
            characterBudget);


        AppendInventory(
            builder,
            "ENTITIES",
            memories.SelectMany(
                entry =>
                    entry.Association.Entities),
            characterBudget);


        if (!AppendWithinBudget(
                builder,
                "\nMEMORY ANCHORS\n",
                characterBudget))
        {
            return FinishBudgeted(
                builder,
                characterBudget);
        }


        IEnumerable<SegaMemoryKnowledgeEntry> anchors =
            memories
                .OrderByDescending(
                    entry =>
                        CalculateMapSalience(
                            entry.Memory))
                .ThenByDescending(
                    entry =>
                        entry.Memory.UpdatedAt);


        int anchorIndex =
            0;


        foreach (
            SegaMemoryKnowledgeEntry entry
            in anchors)
        {
            SegaMemoryRecord memory =
                entry.Memory.Normalize();


            SegaMemoryAssociationProfile association =
                entry.Association.Normalize();


            anchorIndex++;


            StringBuilder anchor =
                new();


            anchor.AppendLine(
                $"ANCHOR {anchorIndex}");


            anchor.AppendLine(
                $"Kind: {memory.Kind}");


            if (!string.IsNullOrWhiteSpace(
                    memory.CanonicalKey))
            {
                anchor.AppendLine(
                    $"Canonical: {memory.CanonicalKey}");
            }


            if (!string.IsNullOrWhiteSpace(
                    memory.TopicKey))
            {
                anchor.AppendLine(
                    $"Topic: {memory.TopicKey}");
            }


            if (association.Concepts.Count >
                0)
            {
                anchor.AppendLine(
                    $"Concepts: {string.Join(", ", association.Concepts.Take(5))}");
            }


            if (association.Entities.Count >
                0)
            {
                anchor.AppendLine(
                    $"Entities: {string.Join(", ", association.Entities.Take(5))}");
            }


            anchor.AppendLine(
                $"Preview: {JsonSerializer.Serialize(TrimPreview(memory.Content, MapContentPreviewCharacters))}");


            anchor.AppendLine();


            if (!AppendWithinBudget(
                    builder,
                    anchor.ToString(),
                    characterBudget))
            {
                break;
            }
        }


        return FinishBudgeted(
            builder,
            characterBudget);
    }


    public static string FormatSearchResults(
        IReadOnlyList<SegaMemorySearchResult>? results)
    {
        if (
            results ==
                null
            ||
            results.Count ==
                0)
        {
            return
                "The memory search returned no candidates.";
        }


        StringBuilder builder =
            new();


        builder.AppendLine(
            "These are retrieval candidates, not automatically relevant memories.");


        builder.AppendLine(
            "Main cognition must judge them against the current event. Association-expanded candidates are one-hop connected memories, not proof of relevance.");


        builder.AppendLine();


        for (
            int index = 0;
            index < results.Count;
            index++)
        {
            SegaMemorySearchResult result =
                results[index];


            builder.AppendLine(
                $"CANDIDATE {index + 1}");


            builder.AppendLine(
                $"ContentSemantic: {result.Similarity:F3}");


            builder.AppendLine(
                $"RetrievalSemantic: {result.RetrievalSimilarity:F3}");


            builder.AppendLine(
                $"Lexical: {result.LexicalAffinity:F3}");


            builder.AppendLine(
                $"AssociationMetadata: {result.AssociationAffinity:F3}");


            if (result.AssociationReasons.Count >
                0)
            {
                builder.AppendLine(
                    $"AssociationReasons: {string.Join("; ", result.AssociationReasons)}");
            }


            AppendMemory(
                builder,
                result.Memory,
                result.Association,
                label: null);


            if (index <
                results.Count -
                    1)
            {
                builder.AppendLine();
            }
        }


        return builder
            .ToString()
            .TrimEnd();
    }


    private static void AppendMemory(
        StringBuilder builder,
        SegaMemoryRecord rawMemory,
        SegaMemoryAssociationProfile? rawAssociation,
        string? label)
    {
        SegaMemoryRecord memory =
            rawMemory.Normalize();


        SegaMemoryAssociationProfile association =
            (rawAssociation ?? new SegaMemoryAssociationProfile())
                .Normalize();


        if (!string.IsNullOrWhiteSpace(
                label))
        {
            builder.AppendLine(
                label);
        }


        builder.AppendLine(
            $"Kind: {memory.Kind}");


        if (!string.IsNullOrWhiteSpace(
                memory.CanonicalKey))
        {
            builder.AppendLine(
                $"Canonical: {memory.CanonicalKey}");
        }


        if (!string.IsNullOrWhiteSpace(
                memory.TopicKey))
        {
            builder.AppendLine(
                $"Topic: {memory.TopicKey}");
        }


        builder.AppendLine(
            $"Importance: {memory.Importance:F2}");


        double effectiveConfidence =
            SegaMemoryConfidencePolicy.CalculateEffectiveConfidence(
                memory);


        builder.AppendLine(
            $"Confidence: {effectiveConfidence:F2}");


        builder.AppendLine(
            $"Source: {memory.Provenance.SourceType}");


        if (!string.IsNullOrWhiteSpace(
                association.RetrievalDescription))
        {
            builder.AppendLine(
                $"RetrievalDescription: {JsonSerializer.Serialize(association.RetrievalDescription)}");
        }


        if (association.RetrievalCues.Count >
            0)
        {
            builder.AppendLine(
                $"RetrievalCues: {string.Join(" | ", association.RetrievalCues)}");
        }


        if (association.Concepts.Count >
            0)
        {
            builder.AppendLine(
                $"Concepts: {string.Join(", ", association.Concepts)}");
        }


        if (association.Entities.Count >
            0)
        {
            builder.AppendLine(
                $"Entities: {string.Join(", ", association.Entities)}");
        }


        builder.AppendLine(
            $"Content: {JsonSerializer.Serialize(memory.Content)}");
    }


    private static void AppendInventory(
        StringBuilder builder,
        string title,
        IEnumerable<string> values,
        int characterBudget)
    {
        string[] ranked =
            values
                .Where(
                    value =>
                        !string.IsNullOrWhiteSpace(
                            value))
                .GroupBy(
                    value =>
                        value.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(
                    group =>
                        group.Count())
                .ThenBy(
                    group =>
                        group.Key,
                    StringComparer.OrdinalIgnoreCase)
                .Select(
                    group =>
                        $"- {group.Key}: {group.Count()}")
                .ToArray();


        if (ranked.Length ==
            0)
        {
            return;
        }


        if (!AppendWithinBudget(
                builder,
                $"\n{title}\n",
                characterBudget))
        {
            return;
        }


        foreach (
            string value
            in ranked)
        {
            if (!AppendWithinBudget(
                    builder,
                    value +
                        Environment.NewLine,
                    characterBudget))
            {
                return;
            }
        }
    }


    private static double CalculateMapSalience(
        SegaMemoryRecord memory)
    {
        double reinforcement =
            Math.Min(
                1.0,
                Math.Log(
                    1.0 +
                    memory.ReinforcementCount)
                /
                4.0);


        double effectiveConfidence =
            SegaMemoryConfidencePolicy.CalculateEffectiveConfidence(
                memory);


        return Math.Clamp(
            memory.Importance *
                0.46
            +
            effectiveConfidence *
                0.30
            +
            memory.EmotionalWeight *
                0.10
            +
            reinforcement *
                0.14,
            0.0,
            1.0);
    }


    private static string TrimPreview(
        string value,
        int maximumCharacters)
    {
        string clean =
            string.Join(
                ' ',
                value.Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries));


        return clean.Length <=
                maximumCharacters
            ? clean
            : clean[..maximumCharacters]
                .TrimEnd()
                +
                "...";
    }


    private static bool AppendWithinBudget(
        StringBuilder builder,
        string text,
        int characterBudget)
    {
        if (builder.Length +
            text.Length >
            characterBudget)
        {
            return false;
        }


        builder.Append(
            text);


        return true;
    }


    private static string FinishBudgeted(
        StringBuilder builder,
        int characterBudget)
    {
        const string suffix =
            "\n[Memory map truncated to cognition budget. Use memory search for exact remembered content.]";


        if (builder.Length +
            suffix.Length <=
            characterBudget)
        {
            builder.Append(
                suffix);
        }


        return builder
            .ToString()
            .TrimEnd();
    }
}