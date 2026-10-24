using System.Text.RegularExpressions;

namespace NIRAAgent.Presentation;

/// <summary>
/// Select a safe location for a verified photo from its observed title and the
/// visible answer text. A weak match returns -1 so we never imply that the
/// image depicts a particular person, model or invention it does not depict.
/// No search, no additional model call, no domain/entity registry.
/// </summary>
public static class NIRAInlineImageAnchorPolicy
{
    private static readonly Regex Words = new(@"[\p{L}\p{N}]{4,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    // Grammatical / document boilerplate only, not a list of entities or topics.
    private static readonly HashSet<string> Generic = new(StringComparer.OrdinalIgnoreCase)
    {
        "image", "photo", "photograph", "picture", "official", "source", "review",
        "article", "news", "best", "latest", "2024", "2025", "2026", "with",
        "from", "that", "this", "there", "their", "about", "what", "where",
        "model", "shown", "file", "page", "more", "read", "media", "press",
        "high", "resolution", "website", "https", "http", "www", "picture"
    };

    // A photo may belong to a typed card or tab rather than a prose paragraph.
    // Only its observed title may establish the identity, never its provider
    // domain or inferred pixels. For one-word names require an exact label word;
    // for multi-word names require two shared meaningful words.
    public static bool MatchesRichBlock(string? verifiedTitle, NIRARichBlock block)
    {
        if (string.IsNullOrWhiteSpace(verifiedTitle)) return false;
        string[] imageTerms = Words.Matches(verifiedTitle)
            .Select(m => m.Value.ToLowerInvariant())
            .Where(t => !Generic.Contains(t))
            .Distinct(StringComparer.Ordinal).Take(16).ToArray();
        if (imageTerms.Length == 0) return false;
        string labels = block.Title + " " + string.Join(" ", block.Type == "tabs"
            ? block.Items : Array.Empty<string>());
        string[] labelTerms = Words.Matches(labels)
            .Select(m => m.Value.ToLowerInvariant()).ToArray();
        int matched = imageTerms.Count(t => labelTerms.Contains(t, StringComparer.Ordinal));
        // A title with one concrete proper noun is enough if that exact noun
        // appears in a card/tab label; common one-word descriptions are not.
        return matched >= 2 || (imageTerms.Length == 1 && matched == 1 &&
            imageTerms[0].Length >= 7);
    }

    // Typed rich blocks cannot be anchored by paragraph line numbers.
    // Require whole subject words in a section heading or tabs label.
    // A search query is placement intent, not independent verification of pixels.
    public static int MatchSection(string? subjectQuery, string? title,
        IReadOnlyList<string>? labels = null, IReadOnlyList<string>? bodyTexts = null,
        string? blockText = null)
    {
        if (string.IsNullOrWhiteSpace(subjectQuery)) return 0;
        string[] subject = Words.Matches(subjectQuery).Select(x => x.Value.ToLowerInvariant())
            .Where(x => !Generic.Contains(x)).Distinct(StringComparer.Ordinal).Take(7).ToArray();
        if (subject.Length == 0) return 0;
        bool ContainsAll(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            var tokens = Words.Matches(s).Select(x => x.Value.ToLowerInvariant())
                .ToHashSet(StringComparer.Ordinal);
            return subject.All(tokens.Contains);
        }
        bool ContainsStrongSubset(string? s)
        {
            if (string.IsNullOrWhiteSpace(s) || subject.Length < 2) return false;
            var tokens = Words.Matches(s).Select(x => x.Value.ToLowerInvariant())
                .ToHashSet(StringComparer.Ordinal);
            int overlap = subject.Count(tokens.Contains);
            return overlap >= Math.Min(subject.Length, 2);
        }
        if (ContainsAll(title)) return 100 + subject.Length;
        if (labels != null && labels.Any(ContainsAll)) return 80 + subject.Length;
        if (bodyTexts != null && bodyTexts.Any(ContainsAll)) return 68 + subject.Length;
        if (ContainsAll(blockText)) return 62 + subject.Length;
        if (labels != null && labels.Any(ContainsStrongSubset)) return 34 + subject.Length;
        if (bodyTexts != null && bodyTexts.Any(ContainsStrongSubset)) return 26 + subject.Length;
        if (ContainsStrongSubset(blockText)) return 20 + subject.Length;
        return 0; // no weak fuzzy attachment to an unrelated section
    }

    public static int FindLine(string? visibleText, string? verifiedTitle)
    {
        if (string.IsNullOrWhiteSpace(visibleText) || string.IsNullOrWhiteSpace(verifiedTitle))
            return -1;
        string[] terms = Words.Matches(verifiedTitle).Select(m => m.Value.ToLowerInvariant())
            .Where(w => !Generic.Contains(w)).Distinct(StringComparer.Ordinal).Take(18).ToArray();
        if (terms.Length < 2) return -1;
        string[] lines = visibleText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        int bestLine = -1, bestOverlap = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length < 35 || line.StartsWith("```") || line.StartsWith('|') ||
                line.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("***", StringComparison.Ordinal) ||
                Regex.IsMatch(line, @"^\s*[-*+]\s|^\s*\d+[.)]\s|^\s*#{1,4}\s"))
                continue;
            var words = Words.Matches(line).Select(m => m.Value.ToLowerInvariant())
                .ToHashSet(StringComparer.Ordinal);
            int overlap = terms.Count(words.Contains);
            if (overlap >= 2 && overlap > bestOverlap)
            {
                bestLine = i;
                bestOverlap = overlap;
            }
        }
        if (bestLine >= 0) return bestLine;

        // People are often introduced by a surname in prose (e.g. a two-word
        // personal name in the photo subject, but only the surname in the reply).
        // A proper-name fallback cannot apply to equipment or longer subjects:
        // those still require two matching tokens, preventing false anchors.
        string[] rawWords = Regex.Matches(verifiedTitle.Trim(), @"^[\p{Lu}][\p{Ll}]+(?:\s+[\p{Lu}][\p{Ll}]+){1}$")
            .Cast<Match>().Select(m => m.Value).ToArray();
        if (terms.Length == 2 && rawWords.Length == 1 &&
            !Generic.Contains(terms[1]) && terms[1].Length >= 5)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length < 40 || line.StartsWith("```", StringComparison.Ordinal) ||
                    line.StartsWith('|') || line.StartsWith("***", StringComparison.Ordinal)) continue;
                // Require the surname as an actual word, not a substring.
                if (Words.Matches(line).Any(m =>
                    string.Equals(m.Value, terms[1], StringComparison.OrdinalIgnoreCase)))
                    return i;
            }
        }
        return -1;
    }
}