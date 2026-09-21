/*
 * filename: NIRAMemoryAssociationProfile.cs
 */

namespace NIRAAgent.Memory.LongTerm;

// =============================================================
// ASSOCIATIVE MEMORY PROFILE
//
// This is retrieval structure around one authoritative durable
// memory. It does not replace the memory's Content.
//
// The profile gives NIRA multiple ways to rediscover the same
// memory later through concepts, named entities and semantic cues.
// =============================================================

public sealed record NIRAMemoryAssociationProfile
{
    private const int MaximumRetrievalDescriptionLength =
        900;

    private const int MaximumCueLength =
        240;

    private const int MaximumTermLength =
        120;

    private const int MaximumRetrievalCues =
        8;

    private const int MaximumConcepts =
        12;

    private const int MaximumEntities =
        12;


    public string? RetrievalDescription
    {
        get;
        init;
    }


    public IReadOnlyList<string> RetrievalCues
    {
        get;
        init;
    } =
        Array.Empty<string>();


    public IReadOnlyList<string> Concepts
    {
        get;
        init;
    } =
        Array.Empty<string>();


    public IReadOnlyList<string> Entities
    {
        get;
        init;
    } =
        Array.Empty<string>();


    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(
            RetrievalDescription)
        &&
        RetrievalCues.Count ==
            0
        &&
        Concepts.Count ==
            0
        &&
        Entities.Count ==
            0;


    public NIRAMemoryAssociationProfile Normalize()
    {
        return this with
        {
            RetrievalDescription =
                NormalizeOptionalText(
                    RetrievalDescription,
                    MaximumRetrievalDescriptionLength),

            RetrievalCues =
                NormalizeValues(
                    RetrievalCues,
                    MaximumRetrievalCues,
                    MaximumCueLength),

            Concepts =
                NormalizeValues(
                    Concepts,
                    MaximumConcepts,
                    MaximumTermLength),

            Entities =
                NormalizeValues(
                    Entities,
                    MaximumEntities,
                    MaximumTermLength)
        };
    }


    public NIRAMemoryAssociationProfile Merge(
        NIRAMemoryAssociationProfile? other)
    {
        NIRAMemoryAssociationProfile current =
            Normalize();


        NIRAMemoryAssociationProfile incoming =
            (other ?? new NIRAMemoryAssociationProfile())
                .Normalize();


        string? description =
            ChooseDescription(
                current.RetrievalDescription,
                incoming.RetrievalDescription);


        return new NIRAMemoryAssociationProfile
        {
            RetrievalDescription =
                description,

            RetrievalCues =
                MergeValues(
                    current.RetrievalCues,
                    incoming.RetrievalCues,
                    MaximumRetrievalCues,
                    MaximumCueLength),

            Concepts =
                MergeValues(
                    current.Concepts,
                    incoming.Concepts,
                    MaximumConcepts,
                    MaximumTermLength),

            Entities =
                MergeValues(
                    current.Entities,
                    incoming.Entities,
                    MaximumEntities,
                    MaximumTermLength)
        }
        .Normalize();
    }


    public string BuildRetrievalText(
        NIRAMemoryRecord memory)
    {
        ArgumentNullException.ThrowIfNull(
            memory);


        NIRAMemoryAssociationProfile normalized =
            Normalize();


        List<string> parts =
            new();


        if (!string.IsNullOrWhiteSpace(
                normalized.RetrievalDescription))
        {
            parts.Add(
                normalized.RetrievalDescription!);
        }


        parts.AddRange(
            normalized.RetrievalCues);


        if (normalized.Concepts.Count >
            0)
        {
            parts.Add(
                "Concepts: " +
                string.Join(
                    ", ",
                    normalized.Concepts));
        }


        if (normalized.Entities.Count >
            0)
        {
            parts.Add(
                "Entities: " +
                string.Join(
                    ", ",
                    normalized.Entities));
        }


        if (parts.Count ==
            0)
        {
            parts.Add(
                memory.Content);
        }


        return string.Join(
            Environment.NewLine,
            parts);
    }


    public static NIRAMemoryAssociationProfile CreateFallback(
        NIRAMemoryRecord memory)
    {
        ArgumentNullException.ThrowIfNull(
            memory);


        List<string> cues =
            new();


        if (!string.IsNullOrWhiteSpace(
                memory.CanonicalKey))
        {
            cues.Add(
                HumanizeKey(
                    memory.CanonicalKey!));
        }


        if (!string.IsNullOrWhiteSpace(
                memory.TopicKey))
        {
            cues.Add(
                HumanizeKey(
                    memory.TopicKey!));
        }


        return new NIRAMemoryAssociationProfile
        {
            RetrievalDescription =
                memory.Content,

            RetrievalCues =
                cues
        }
        .Normalize();
    }


    private static string? ChooseDescription(
        string? left,
        string? right)
    {
        if (string.IsNullOrWhiteSpace(
                left))
        {
            return right;
        }


        if (string.IsNullOrWhiteSpace(
                right))
        {
            return left;
        }


        // Fresh grounded retrieval wording should replace an older
        // fallback/previous description. The authoritative Content
        // is unchanged; this affects retrieval navigation only.
        return right;
    }


    private static IReadOnlyList<string> MergeValues(
        IReadOnlyList<string>? left,
        IReadOnlyList<string>? right,
        int maximumCount,
        int maximumLength)
    {
        return (left ?? Array.Empty<string>())
            .Concat(
                right ?? Array.Empty<string>())
            .Where(
                value =>
                    !string.IsNullOrWhiteSpace(
                        value))
            .Select(
                value =>
                    NormalizeRequiredText(
                        value,
                        maximumLength))
            .Distinct(
                StringComparer.OrdinalIgnoreCase)
            .Take(
                maximumCount)
            .ToArray();
    }


    private static IReadOnlyList<string> NormalizeValues(
        IReadOnlyList<string>? values,
        int maximumCount,
        int maximumLength)
    {
        if (
            values ==
                null
            ||
            values.Count ==
                0)
        {
            return Array.Empty<string>();
        }


        return values
            .Where(
                value =>
                    !string.IsNullOrWhiteSpace(
                        value))
            .Select(
                value =>
                    NormalizeRequiredText(
                        value,
                        maximumLength))
            .Distinct(
                StringComparer.OrdinalIgnoreCase)
            .Take(
                maximumCount)
            .ToArray();
    }


    private static string? NormalizeOptionalText(
        string? value,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return null;
        }


        return NormalizeRequiredText(
            value,
            maximumLength);
    }


    private static string NormalizeRequiredText(
        string value,
        int maximumLength)
    {
        string normalized =
            string.Join(
                ' ',
                value.Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries));


        if (normalized.Length <=
            maximumLength)
        {
            return normalized;
        }


        return normalized[..maximumLength]
            .TrimEnd();
    }


    private static string HumanizeKey(
        string value)
    {
        return value
            .Replace(
                '.',
                ' ')
            .Replace(
                '_',
                ' ')
            .Replace(
                '-',
                ' ')
            .Trim();
    }
}

