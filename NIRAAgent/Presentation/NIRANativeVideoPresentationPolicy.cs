using System.Text.RegularExpressions;

namespace NIRAAgent.Presentation;

/// <summary>
/// A general presentation fallback for model-authored recommendation cards.
/// Grounding belongs to the upstream researcher; this class only determines
/// whether a supplied URL is supported by the existing in-chat player.
/// Unsupported/ambiguous links remain ordinary clickable card links.
/// </summary>
public static class NIRANativeVideoPresentationPolicy
{
    private static readonly Regex HttpsUrl = new(
        @"https://[^\s<>\]\)\(]+", RegexOptions.Compiled | RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant);

    private static bool FindSingleVideo(string text, out NIRAVideoReference? video)
    {
        video = null;
        var links = HttpsUrl.Matches(text).Cast<Match>().ToArray();
        if (links.Length != 1) return false;
        string candidate = links[0].Value.TrimEnd('.', ',', ';', ':', '!', '?', '*');
        return NIRAVideoReferencePolicy.TryParse(candidate, out video) && video is not null;
    }

    private static string WithoutStandaloneWatchLink(string text, NIRAVideoReference reference)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var remaining = new List<string>();
        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            string candidate = trimmed;
            var match = Regex.Match(trimmed,
                @"^\[.{1,160}\]\((?<url>https://[^\s<>\)]+)\)$",
                RegexOptions.CultureInvariant);
            if (match.Success) candidate = match.Groups["url"].Value;
            else
            {
                // Generic structured-link fields may have been flattened into
                // a card by the JSON reader. Remove only a standalone verified
                // video URL field; preserve all substantive description text.
                var property = Regex.Match(trimmed,
                    @"^(?:url|href|uri|link)\s*:\s*(?<url>https://\S+)\s*$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (property.Success) candidate = property.Groups["url"].Value;
            }
            if (NIRAVideoReferencePolicy.TryParse(candidate, out var linked) &&
                linked is not null && linked.Key.Equals(reference.Key,
                    StringComparison.OrdinalIgnoreCase))
                continue;
            remaining.Add(line);
        }
        return string.Join("\n", remaining).Trim();
    }

    /// <summary>
    /// Keep consecutive cards together for the existing card layout and move
    /// their verified player URLs into the existing video deck immediately after
    /// that group. Do not interpret general article links as playable videos.
    /// </summary>
    public static IReadOnlyList<NIRARichBlock> ExpandCards(IReadOnlyList<NIRARichBlock> blocks)
    {
        if (blocks.Count == 0) return blocks;
        var result = new List<NIRARichBlock>(blocks.Count);
        var scheduled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var existing in blocks.Where(b => b.Type == "video"))
            if (NIRAVideoReferencePolicy.TryParse(existing.Text, out var parsed) && parsed != null)
                scheduled.Add(parsed.Key);

        for (int i = 0; i < blocks.Count;)
        {
            if (blocks[i].Type != "card")
            {
                result.Add(blocks[i++]);
                continue;
            }
            var playerBlocks = new List<NIRARichBlock>();
            while (i < blocks.Count && blocks[i].Type == "card")
            {
                var card = blocks[i++];
                if (!FindSingleVideo(card.Text, out var video) || video is null ||
                    !scheduled.Add(video.Key))
                {
                    result.Add(card);
                    continue;
                }
                string summary = WithoutStandaloneWatchLink(card.Text, video);
                if (summary.Length > 0)
                    result.Add(card with { Text = summary });
                playerBlocks.Add(new NIRARichBlock
                {
                    Type = "video", Title = card.Title, Text = video.OriginalUrl
                });
            }
            result.AddRange(playerBlocks);
        }
        return result;
    }
}