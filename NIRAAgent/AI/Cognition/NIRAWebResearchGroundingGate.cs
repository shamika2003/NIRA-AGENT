using System.Text.RegularExpressions;

namespace NIRAAgent.AI.Cognition;

/// <summary>
/// Mechanical safeguards for a draft built from real web.search results.
/// They do not judge semantic truth, infer intent, demand media or grant tools.
/// The independent model reviewer must still compare the ACTUAL claim with
/// the cited extract. No product names, fixed domains or example answers.
/// </summary>
internal static class NIRAWebResearchGroundingGate
{
    private static readonly Regex UrlRow = new(
        @"(?m)^URL:\s*(?<url>https?://\S+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex LinkedUrl = new(
        @"\]\(\s*(?<url>https?://(?:[^\s()]|\([^)]*\))+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    internal static NIRAWebResearchGroundingVerdict Check(string? evidence, string? draft)
    {
        if (string.IsNullOrWhiteSpace(evidence) || string.IsNullOrWhiteSpace(draft))
            return NIRAWebResearchGroundingVerdict.Pass;

        string[] urls = UrlRow.Matches(evidence).Select(m => m.Groups["url"].Value)
            .Where(u => Uri.TryCreate(u, UriKind.Absolute, out Uri? parsed) &&
                parsed is { Scheme: "https" or "http" })
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        // Unparseable evidence is never upgraded to authority by this gate.
        // The Executive's trust envelope and reviewer still own eligibility.
        if (urls.Length == 0) return NIRAWebResearchGroundingVerdict.Pass;

        foreach (Match link in LinkedUrl.Matches(draft))
        {
            string target = link.Groups["url"].Value.TrimEnd('.', ',', ';');
            if (!urls.Any(u => Equivalent(u, target)))
                return new(false, "UnobservedCitation",
                    "The researched reply cites a URL not present in the observed web sources.");
        }

        // Web excerpts are evidence of published claims, not a numerical whitelist.
        // A calculated value, unit conversion, or explicit hypothetical assumption
        // can correctly introduce numbers absent from any fetched page. Requiring
        // every magnitude to occur literally in the source is unsound and caused
        // repeated false rejections of otherwise useful quantitative explanations.
        // The independent semantic reviewer checks whether *externally asserted*
        // measurements and comparisons have support. Source URL identity remains
        // an inexpensive deterministic preflight above.
        return NIRAWebResearchGroundingVerdict.Pass;
    }

    private static bool Equivalent(string source, string target)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out Uri? a) ||
            !Uri.TryCreate(target, UriKind.Absolute, out Uri? b)) return false;
        // Links may add fragment navigation but not silently swap publisher,
        // path, query or a deeper unvisited source.
        return a.Scheme.Equals(b.Scheme, StringComparison.OrdinalIgnoreCase) &&
            a.Host.Equals(b.Host, StringComparison.OrdinalIgnoreCase) &&
            a.Port == b.Port &&
            a.AbsolutePath.Equals(b.AbsolutePath, StringComparison.Ordinal) &&
            a.Query.Equals(b.Query, StringComparison.Ordinal);
    }
}

internal sealed record NIRAWebResearchGroundingVerdict(bool Accepted,
    string Code, string Explanation)
{
    public static readonly NIRAWebResearchGroundingVerdict Pass = new(true, "Pass", "");
}
