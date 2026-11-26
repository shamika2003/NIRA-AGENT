using System.Text;
using System.Text.RegularExpressions;

namespace Nira.Infrastructure;

/// <summary>Bounded local relevance ranking. No plaintext index, network call or embedding claim.</summary>
internal static partial class MemoryRanking
{
    // Common grammatical words should not cause unrelated personal notes to be recalled.
    private static readonly HashSet<string> CommonWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "are", "as", "at", "be", "by", "can", "could", "do", "does", "for", "from",
        "have", "how", "i", "in", "is", "it", "me", "my", "of", "on", "or", "our", "please", "should",
        "that", "the", "their", "them", "this", "to", "us", "was", "we", "were", "what", "when", "where",
        "which", "who", "will", "with", "would", "you", "your", "about", "tell", "some", "use", "using"
    };

    // Generic context nouns may help rank a multi-term match, but are not
    // sufficient anchors for unrelated notes or private history excerpts.
    private static readonly HashSet<string> GenericContextWords = new(StringComparer.Ordinal)
    { "note", "notes", "memory", "memories", "project", "projects", "information", "details", "conversation", "conversations", "chat", "chats" };

    [GeneratedRegex(@"[\p{L}\p{N}][\p{L}\p{N}\p{M}_+#.-]*", RegexOptions.CultureInvariant, 100)]
    private static partial Regex Tokens();

    private static HashSet<string> Terms(string value) => Tokens().Matches(value.Normalize(NormalizationForm.FormKC))
        .Select(m => m.Value.TrimEnd('.', '-').ToLowerInvariant())
        .Where(t => t.Length >= 2 && !CommonWords.Contains(t)).Take(128).ToHashSet(StringComparer.Ordinal);

    // Formatting-only identity for learning suggestions, not a semantic merge of approved notes.
    // Keep case, punctuation, word order, negation and all non-whitespace characters intact.
    internal static string LearningIdentity(string content)
    {
        var normalized = new StringBuilder(content.Length);
        bool gap = false;
        foreach (char character in content.Trim().Normalize(NormalizationForm.FormC))
        {
            if (char.IsWhiteSpace(character)) { gap = true; continue; }
            if (gap && normalized.Length > 0) normalized.Append(' ');
            gap = false;
            normalized.Append(character);
        }
        return normalized.ToString();
    }

    // Topic association is a conservative lexical hint, not a semantic identity decision.
    // A shorter topic can match a longer topic without treating unrelated overlapping
    // topics as identical. Neither this nor ranking ever merges or deletes records.
    internal static bool RelatedTopic(string first, string second)
    {
        var a = Terms(first);
        var b = Terms(second);
        a.ExceptWith(GenericContextWords);
        b.ExceptWith(GenericContextWords);
        return a.Count > 0 && b.Count > 0 && (a.IsSubsetOf(b) || b.IsSubsetOf(a));
    }

    // Model-generated search phrases are hints, never facts or authorization.
    // A semantic fallback requires bounded multi-term overlap; a named subject must
    // also be present as whole tokens. Generic context alone cannot select a note.
    internal static bool MatchesAnchor(string text, string anchor)
    {
        var terms = Terms(anchor);
        terms.ExceptWith(GenericContextWords);
        return terms.Count > 0 && terms.IsSubsetOf(Terms(text));
    }

    internal static IReadOnlyList<T> RankExpanded<T>(IEnumerable<T> values, Func<T, string> searchable,
        IReadOnlyList<string> phrases, string anchor, int limit)
    {
        var anchors = Terms(anchor);
        anchors.ExceptWith(GenericContextWords);
        if (anchor.Length > 0 && anchors.Count == 0) return [];
        var groups = phrases.Select(Terms).Select(group => {
            group.ExceptWith(GenericContextWords);
            return group;
        }).Where(group => group.Count >= 2 && anchors.IsSubsetOf(group)).ToArray();
        if (groups.Length == 0) return [];
        return values.Select(value => (Value: value, Tokens: Terms(searchable(value))))
            .Where(item => anchors.IsSubsetOf(item.Tokens))
            .Select(item => (item.Value, Score: groups.Where(group => group.Count(item.Tokens.Contains) >= 2 &&
                    group.Count(item.Tokens.Contains) * 2 >= group.Count)
                .Select(group => group.Count(item.Tokens.Contains)).DefaultIfEmpty(0).Max()))
            .Where(item => item.Score > 0).OrderByDescending(item => item.Score)
            .Take(Math.Clamp(limit, 1, 6)).Select(item => item.Value).ToArray();
    }

    internal static IReadOnlyList<T> Rank<T>(IEnumerable<T> values, Func<T, string> searchable,
        string query, IReadOnlyList<string> alternatives, int limit, bool automatic = false, Func<T, string>? subject = null, bool preserveInputOrder = false)
    {
        var phrases = new[] { query }.Concat(alternatives).Select(x => x.Trim()).Where(x => x.Length > 0).Take(9).ToArray();
        var groups = phrases.Select(Terms).Where(g => g.Count > 0).ToArray();
        // A generic alternative is not a meaningful alias for a specific query.
        bool specificQuery = Terms(query).Any(term => !GenericContextWords.Contains(term));
        if (specificQuery)
            groups = groups.Where(g => g.Any(term => !GenericContextWords.Contains(term))).ToArray();
        var queryTerms = groups.SelectMany(g => g).ToHashSet(StringComparer.Ordinal);
        if (queryTerms.Count == 0) return [];
        var candidates = values.Select(value => (Value: value, Text: searchable(value)))
            .Select(item => (item.Value, item.Text, Terms: Terms(item.Text), Subject: Terms(subject?.Invoke(item.Value) ?? ""))).ToArray();
        var weights = queryTerms.ToDictionary(term => term,
            term => 1.0 + Math.Log((candidates.Length + 1.0) / (1.0 + candidates.Count(c => c.Terms.Contains(term)))),
            StringComparer.Ordinal);
        var scored = candidates.Select(item =>
        {
            var matched = queryTerms.Where(item.Terms.Contains).ToArray();
            bool exact = phrases.Any(p => item.Text.Contains(p, StringComparison.OrdinalIgnoreCase));
            // A single distinctive whole-word match is useful; never match only generic grammar.
            // Exact phrase and multiple terms outrank one weak overlap. Stable input order breaks ties.
            double score = matched.Sum(term => weights[term]) / Math.Sqrt(Math.Max(1, queryTerms.Count)) + (exact ? 3 : 0);
            bool groupMatch = groups.Any(g => g.Count(item.Terms.Contains) >= Math.Min(2, g.Count));
            bool subjectMatch = matched.Any(t => t.Length >= 3 && !GenericContextWords.Contains(t) && item.Subject.Contains(t));
            bool distinctiveMatch = matched.Any(t => t.Length >= 4 && !GenericContextWords.Contains(t));
            // Explicit searches may return a single whole-word hit even when the
            // question contains attributes not yet present in the note. Automatic
            // recall remains more conservative because it transmits selected notes.
            bool uniqueSubjectAnchor = matched.Any(t => t.Length >= 4 && !GenericContextWords.Contains(t) &&
                item.Subject.Contains(t) && candidates.Count(c => c.Subject.Contains(t)) == 1);
            bool hasSpecificMatch = matched.Any(t => !GenericContextWords.Contains(t));
            bool eligible = matched.Length > 0 && (automatic
                ? hasSpecificMatch && (exact || (groupMatch && matched.Length >= 2) || uniqueSubjectAnchor)
                : (!specificQuery || hasSpecificMatch) && (exact || groupMatch || distinctiveMatch || subjectMatch));
            if (subjectMatch) score += 2;
            return (item.Value, Score: eligible ? score : 0);
        });
        var ranked = scored.Where(x => x.Score > 0);
        if (!preserveInputOrder) ranked = ranked.OrderByDescending(x => x.Score);
        return ranked.Take(Math.Clamp(limit, 1, 20)).Select(x => x.Value).ToArray();
    }
}
