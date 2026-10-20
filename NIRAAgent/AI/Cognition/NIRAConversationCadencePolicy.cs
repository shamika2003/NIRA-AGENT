using System.Text;
using System.Text.RegularExpressions;

namespace NIRAAgent.AI.Cognition;

/// <summary>
/// Presentation-only cadence evidence from the SAME bounded conversation
/// provided to cognition. No phrase lists, canned responses, sentiment rules,
/// user-intent classifiers, new memory stores, or changes to character state.
/// It detects near-duplicate opening wording, not semantic disagreement.
/// </summary>
internal static class NIRAConversationCadencePolicy
{
    private static readonly Regex Words = new(@"[\p{L}\p{N}]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // The conversation owner serializes each turn as "role: content". A
    // continuation may contain arbitrary line breaks; only explicit role
    // headings begin a new message. Avoid relying on words in the reply.
    public static IReadOnlyList<string> RecentAssistantOpenings(string? context, int maximum = 6)
    {
        if (string.IsNullOrWhiteSpace(context)) return Array.Empty<string>();
        maximum = Math.Clamp(maximum, 1, 12);
        var openings = new List<string>();
        var turn = new StringBuilder();
        bool isAssistant = false;

        void Finish()
        {
            if (isAssistant && turn.Length > 0)
            {
                string opening = Opening(turn.ToString());
                if (opening.Length > 0) openings.Add(opening);
            }
            turn.Clear();
        }

        foreach (string line in context.Replace("\r\n", "\n", StringComparison.Ordinal)
                     .Replace('\r', '\n').Split('\n'))
        {
            int colon = line.IndexOf(':');
            string role = colon >= 0 && colon < 16 ? line[..colon].Trim() : "";
            if (role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ||
                role.Equals("nira", StringComparison.OrdinalIgnoreCase) ||
                role.Equals("user", StringComparison.OrdinalIgnoreCase) ||
                role.Equals("system", StringComparison.OrdinalIgnoreCase))
            {
                Finish();
                isAssistant = role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ||
                              role.Equals("nira", StringComparison.OrdinalIgnoreCase);
                if (isAssistant) turn.Append(line[(colon + 1)..].TrimStart());
            }
            else if (isAssistant)
            {
                turn.Append('\n').Append(line);
            }
        }
        Finish();
        return openings.TakeLast(maximum).ToArray();
    }

    public static bool RepeatsOpening(string? candidate, string? conversation)
    {
        string[] current = Tokens(Opening(candidate));
        if (current.Length < 5) return false;
        foreach (string previousOpening in RecentAssistantOpenings(conversation))
        {
            string[] previous = Tokens(previousOpening);
            if (previous.Length < 5) continue;
            // Identical 3+ word entry into a full sentence is enough evidence
            // to request a rephrasing, but is never a reason to erase facts.
            int matchingPrefix = 0;
            while (matchingPrefix < Math.Min(current.Length, previous.Length) &&
                   current[matchingPrefix] == previous[matchingPrefix])
                matchingPrefix++;
            if (matchingPrefix >= 3) return true;
            // Also detect longer rearranged stock introductions. Compare
            // contiguous four-word shingles, not a user-specific phrase list.
            var priorShingles = Shingles(previous);
            int matches = Shingles(current).Count(priorShingles.Contains);
            if (matches >= 2 && matches >= Math.Min(current.Length - 3, previous.Length - 3) * 0.55)
                return true;
        }
        return false;
    }

    public static string DescribeRecentOpenings(string? conversation)
    {
        var openings = RecentAssistantOpenings(conversation, 5);
        if (openings.Count < 2) return string.Empty;
        return "RECENT NIRA OPENINGS (actual conversation, NOT example lines to imitate):\n" +
               string.Join("\n", openings.Select(line => "- " + line)) + "\n" +
               "Continue the SAME conversation without reintroducing NIRA or commenting on " +
               "the user's frequency of questions. If an answer has results/visual blocks, " +
               "lead with the substance. Personality is expressed through judgment and " +
               "wording, not a repeated introductory performance.";
    }

    private static string Opening(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        string normalized = text.Trim();
        // Take one opening sentence or visible line; never copy an entire
        // factual result/technical document into the cadence summary.
        int end = Math.Min(normalized.Length, 240);
        for (int i = 0; i < end; i++)
        {
            char c = normalized[i];
            if (c == '\n' || c is '!' or '?') { end = i + 1; break; }
            if (c == '.' && (i + 1 == normalized.Length ||
                (i + 1 < normalized.Length && char.IsWhiteSpace(normalized[i + 1]))))
            { end = i + 1; break; }
        }
        return normalized[..end].Trim();
    }

    private static string[] Tokens(string source) =>
        Words.Matches(source.ToLowerInvariant()).Select(match => match.Value).Take(24).ToArray();

    private static HashSet<string> Shingles(string[] tokens)
    {
        var shingles = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i + 3 < tokens.Length; i++)
            shingles.Add(string.Join(" ", tokens.Skip(i).Take(4)));
        return shingles;
    }
}
