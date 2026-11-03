using System.Text.RegularExpressions;

namespace NIRAAgent.Presentation;

/// <summary>
/// Final display-only shape adapter for observed web research. Never changes
/// facts, verified links, tool evidence or execution. Ordinary informational
/// answers need explanatory content rather than a list of source cards and
/// broken citation-looking cells; explicit link-discovery requests keep links.
/// </summary>
public static class NIRAResearchPresentationPolicy
{
    private static readonly Regex DirectNavigation = new(
        @"\b(?:show|find|list|recommend|send|give|share|suggest|provide)\s+(?:me\s+)?(?:some\s+|the\s+|good\s+|best\s+|reliable\s+|useful\s+){0,4}" +
        @"(?:websites?|sites?|links?|urls?|references?|sources?)\b|" +
        @"\b(?:websites?|sites?|links?|urls?)\s+(?:for|to|where|that)\b|" +
        @"\bwhere\s+(?:can\s+i|to)\s+(?:find|get|browse)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool IsLinkDiscoveryRequest(string? userText) =>
        !string.IsNullOrWhiteSpace(userText) && DirectNavigation.IsMatch(userText);


    private static readonly Regex DecorativeReferenceHeading = new(
        @"(?is)(?:^|
)\s{0,3}(?:#{1,6}\s*)?(?:references?|sources?|citations?|further\s+reading)\s*:?\s*
[\s\S]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TrailingReferenceDirectory = new(
        @"(?is)(?:
|^)(?:(?:\s*(?:[-*]|\d+\.)\s+.*https?://\S+.*
?){2,})\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string StripDecorativeReferenceSection(
        string? reply, string? originalRequest, bool verifiedWebSearch)
    {
        if (string.IsNullOrWhiteSpace(reply) || !verifiedWebSearch ||
            IsLinkDiscoveryRequest(originalRequest))
            return reply ?? string.Empty;
        string clean = DecorativeReferenceHeading.Replace(reply!, string.Empty);
        clean = TrailingReferenceDirectory.Replace(clean, string.Empty);
        return clean.Trim();
    }

    public static IReadOnlyList<NIRARichBlock> RemoveDecorativeSourceDump(
        IReadOnlyList<NIRARichBlock> blocks, string? originalRequest, bool verifiedWebSearch)
    {
        if (!verifiedWebSearch || IsLinkDiscoveryRequest(originalRequest)) return blocks;
        var clean = new List<NIRARichBlock>(blocks.Count);
        foreach (NIRARichBlock block in blocks)
        {
            // Ordinary explanatory web research keeps citations in grounded
            // prose when needed. Dedicated native source cards are optional,
            // and here they simply push the answer off-screen.
            if (block.Type == "source") continue;
            if (block.Type == "table" && block.Columns.Count >= 3)
            {
                int index = -1;
                for (int i = 0; i < block.Columns.Count; i++)
                    if (block.Columns[i].Trim().TrimEnd(':').Equals("source",
                            StringComparison.OrdinalIgnoreCase) ||
                        block.Columns[i].Trim().TrimEnd(':').Equals("sources",
                            StringComparison.OrdinalIgnoreCase) ||
                        block.Columns[i].Trim().TrimEnd(':').Equals("citation",
                            StringComparison.OrdinalIgnoreCase))
                    { index = i; break; }
                if (index >= 0 && block.Rows.All(row => row.Count == block.Columns.Count))
                    clean.Add(block with {
                        Columns = block.Columns.Where((_, i) => i != index).ToArray(),
                        Rows = block.Rows.Select(row => (IReadOnlyList<string>)row
                            .Where((_, i) => i != index).ToArray()).ToArray()
                    });
                else clean.Add(block);
            }
            else clean.Add(block);
        }
        return clean;
    }
}
