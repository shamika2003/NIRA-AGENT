/*
 * filename: NIRAMemorySearchRequest.cs
 */

namespace NIRAAgent.Memory.LongTerm;

public sealed record NIRAMemorySearchRequest
{
    public string Query
    {
        get;
        init;
    } =
        string.Empty;


    public IReadOnlyList<NIRAMemoryKind> Kinds
    {
        get;
        init;
    } =
        Array.Empty<NIRAMemoryKind>();


    public IReadOnlyList<string> CanonicalKeys
    {
        get;
        init;
    } =
        Array.Empty<string>();


    public IReadOnlyList<string> TopicKeys
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


    public bool ExpandAssociations
    {
        get;
        init;
    }


    public int MaximumResults
    {
        get;
        init;
    } =
        12;


    public bool HasSearchCriteria =>
        !string.IsNullOrWhiteSpace(
            Query)
        ||
        Kinds.Count >
            0
        ||
        CanonicalKeys.Count >
            0
        ||
        TopicKeys.Count >
            0
        ||
        Concepts.Count >
            0
        ||
        Entities.Count >
            0;


    public NIRAMemorySearchRequest Normalize()
    {
        return this with
        {
            Query =
                Query?.Trim()
                ?? string.Empty,

            Kinds =
                (Kinds ?? Array.Empty<NIRAMemoryKind>())
                    .Distinct()
                    .Take(
                        6)
                    .ToArray(),

            CanonicalKeys =
                NormalizeKeys(
                    CanonicalKeys,
                    maximumCount: 8),

            TopicKeys =
                NormalizeKeys(
                    TopicKeys,
                    maximumCount: 8),

            Concepts =
                NormalizeValues(
                    Concepts,
                    maximumCount: 12),

            Entities =
                NormalizeValues(
                    Entities,
                    maximumCount: 12),

            MaximumResults =
                Math.Clamp(
                    MaximumResults,
                    1,
                    20)
        };
    }


    public string BuildSignature()
    {
        NIRAMemorySearchRequest normalized =
            Normalize();


        return string.Join(
            "|",
            $"q={normalized.Query.ToLowerInvariant()}",
            $"k={Join(normalized.Kinds.Select(value => value.ToString()))}",
            $"c={Join(normalized.CanonicalKeys)}",
            $"t={Join(normalized.TopicKeys)}",
            $"x={Join(normalized.Concepts)}",
            $"e={Join(normalized.Entities)}",
            $"a={normalized.ExpandAssociations}",
            $"n={normalized.MaximumResults}");
    }


    private static string Join(
        IEnumerable<string> values)
    {
        return string.Join(
            ",",
            values
                .OrderBy(
                    value =>
                        value,
                    StringComparer.OrdinalIgnoreCase));
    }


    private static string[] NormalizeKeys(
        IReadOnlyList<string>? values,
        int maximumCount)
    {
        return NormalizeValues(
                values,
                maximumCount)
            .Select(
                value =>
                    value.ToLowerInvariant())
            .ToArray();
    }


    private static string[] NormalizeValues(
        IReadOnlyList<string>? values,
        int maximumCount)
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
                    string.Join(
                        ' ',
                        value.Split(
                            (char[]?)null,
                            StringSplitOptions.RemoveEmptyEntries))
                        .Trim())
            .Distinct(
                StringComparer.OrdinalIgnoreCase)
            .Take(
                maximumCount)
            .ToArray();
    }
}

