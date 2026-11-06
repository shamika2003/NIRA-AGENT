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

    // An internal image-layout plan is not research content. Some models
    // append this as an entire numbered final section; it used to become the
    // strongest keyword match and pull ALL images into an unwanted end pile.
    // Preserve normal prose and verified image links: remove only clearly
    // labelled placement plans and their explicit positioning instructions.
    public static string StripEditorialImagePlacement(string? reply)
    {
        if (string.IsNullOrEmpty(reply)) return reply ?? string.Empty;
        string[] lines = reply.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var keep = new List<string>(lines.Length);
        bool insidePlan = false;
        int planDepth = 7;
        foreach (string line in lines)
        {
            var heading = Regex.Match(line,
                @"^\s*(?<hash>#{1,6})\s*(?:\d{1,2}[.)]\s*)?(?<name>[^#\r\n]+?)\s*$",
                RegexOptions.CultureInvariant);
            string headingName = heading.Success ? heading.Groups["name"].Value.Trim() : "";
            bool isPlan = Regex.IsMatch(headingName,
                @"^(?:visual elements|image placement|photo placement|photograph placement|" +
                @"suggested (?:visual|image|photo) placement|media placement plan)" +
                @"(?:\s*\([^)]*\))?\s*:?\s*$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (heading.Success && isPlan)
            {
                insidePlan = true;
                planDepth = heading.Groups["hash"].Length;
                continue;
            }
            // Also recognize plain numbered headings, which earlier model
            // completions used without Markdown '#' prefixes.
            if (!insidePlan && Regex.IsMatch(line,
                @"^\s*\d{1,2}[.)]\s+(?:visual elements|image placement)\s*" +
                @"(?:\(suggested placement\))?\s*$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                insidePlan = true;
                planDepth = 2;
                continue;
            }
            if (insidePlan)
            {
                if (heading.Success && heading.Groups["hash"].Length <= planDepth)
                    insidePlan = false;
                else
                    continue;
            }
            // Do not show user-facing prompts that tell a renderer where to
            // insert a source-backed image. Actual image assets are separate.
            if (Regex.IsMatch(line,
                @"^\s*(?:[-*+]\s*)?\[Image\s*:[^\]]+\]\s*" +
                @"(?:[-–—:]\s*)?(?:placed|positioned|beside|next to|in the|near the)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) continue;
            keep.Add(line);
        }
        return string.Join("\n", keep).Trim();
    }

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