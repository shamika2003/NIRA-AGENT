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

    // Includes bare URLs and Markdown destinations. A citation is not grounded
    // merely because it is written outside Markdown link syntax.
    private static readonly Regex VisibleUrl = new(
        @"https?://[^\s<>\[\]""']+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    // A model may invent a URL during a focused factual correction even when
    // its prose can still be checked. Remove ONLY unsupported citation links
    // before independent re-review. This never certifies the corrected claims,
    // never changes a real tool receipt and never runs on arbitrary user text.
    // Markdown link labels remain visible as plain text, without false sourcing.
    private static readonly Regex MarkdownCitation = new(
        @"\[(?<label>[^\]\r\n]{1,200})\]\((?<url>https?://[^\s\)]+)\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    internal static string RemoveUnobservedCitationLinks(string evidence, string correction)
    {
        if (string.IsNullOrWhiteSpace(correction)) return correction;
        string[] observed = ObservedUrls(evidence);
        if (observed.Length == 0) return correction; // No unverified promotion.
        bool Allowed(string url) =>
            observed.Any(source => Equivalent(source, CleanUrl(url)));
        string cleaned = MarkdownCitation.Replace(correction, match =>
            Allowed(match.Groups["url"].Value) ? match.Value : match.Groups["label"].Value);
        // Bare invented URLs are not citation evidence either. Existing
        // observed URLs are preserved byte-for-byte for the grounding gate.
        cleaned = VisibleUrl.Replace(cleaned, match =>
            Allowed(match.Value) ? match.Value : string.Empty);
        return cleaned.Trim();
    }

    internal static string ObservedCitationAllowlist(string evidence) =>
        string.Join("\n", ObservedUrls(evidence).Select(url => "- " + url));

    internal static bool HasObservedReference(string? evidence, string? answer)
    {
        string[] observed = ObservedUrls(evidence);
        return observed.Length > 0 && VisibleUrl.Matches(answer ?? string.Empty)
            .Select(m => CleanUrl(m.Value))
            .Any(link => observed.Any(url => Equivalent(url, link)));
    }

    private static string[] ObservedUrls(string? evidence) =>
        UrlRow.Matches(evidence ?? string.Empty).Select(m => CleanUrl(m.Groups["url"].Value))
            .Where(u => Uri.TryCreate(u, UriKind.Absolute, out Uri? parsed) &&
                parsed is { Scheme: "https" or "http" })
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static string CleanUrl(string url) =>
        url.TrimEnd('.', ',', ';', ':', ')', ']', '}', '>');

    internal static NIRAWebResearchGroundingVerdict Check(string? evidence, string? draft)
    {
        if (string.IsNullOrWhiteSpace(evidence) || string.IsNullOrWhiteSpace(draft))
            return NIRAWebResearchGroundingVerdict.Pass;

        string[] urls = ObservedUrls(evidence);
        // Unparseable evidence is never upgraded to authority by this gate.
        // The Executive's trust envelope and reviewer still own eligibility.
        if (urls.Length == 0) return NIRAWebResearchGroundingVerdict.Pass;

        foreach (Match link in VisibleUrl.Matches(draft))
        {
            string target = CleanUrl(link.Value);
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
