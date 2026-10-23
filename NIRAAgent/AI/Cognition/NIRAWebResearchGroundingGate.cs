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

    // Decimal measurements only: not every free-standing integer, year, chapter,
    // unit conversion, rounded paraphrase or arithmetic expression. If a measurement is newly asserted as a web-sourced
    // fact, at least its precise numeric magnitude must appear in the excerpts.
    // This is only a mismatch detector, not evidence of factual correctness.
    private static readonly Regex Measurement = new(
        @"(?<![\p{L}\d])(?<value>\d+[.,]\d+)\s*(?<unit>%|mph|kph|km/h|km|miles?|meters?|feet|ft|seconds?|secs?|milliseconds?|ms|minutes?|mins?|hours?|days?|kwh|kw|mw|gw|watts?|volts?|amps?|kg|lbs?|gb|tb)(?![\p{L}])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex Extract = new(
        @"(?ms)^EXTRACT:[ \t]*(?<text>.*?)(?=^SOURCE \(external extract|\z)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

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

        string observedExtracts = string.Join(" ", Extract.Matches(evidence)
            .Select(m => m.Groups["text"].Value));
        if (string.IsNullOrWhiteSpace(observedExtracts))
            return NIRAWebResearchGroundingVerdict.Pass;
        foreach (Match number in Measurement.Matches(draft))
        {
            string magnitude = number.Groups["value"].Value;
            // Thousands separators can be semantic separators in prose; keep
            // only unambiguous magnitudes for a conservative, low-noise check.
            if (magnitude.Contains(',') && !magnitude.Contains('.')) continue;
            if (!Regex.IsMatch(observedExtracts, @"(?<![\d])" +
                Regex.Escape(magnitude) + @"(?![\d])", RegexOptions.CultureInvariant))
                return new(false, "UnobservedMeasurement",
                    "A precise quantified claim in the researched draft is not " +
                    "present in the observed source extracts.");
        }
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
