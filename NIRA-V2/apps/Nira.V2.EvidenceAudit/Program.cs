using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Nira.V2.Evidence;

// Read-only public-log audit. Does not access run vaults, browser, model, or credentials.
// It audits source provenance and output encoding, NOT claim truthfulness.
if (args.Length != 1 || !File.Exists(args[0]))
{
    Console.Error.WriteLine("Usage: dotnet run --project apps/Nira.V2.EvidenceAudit -- <public-logs.zip>");
    return 2;
}

var domains = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
{
    // The heat-pump prompt permits engineering sources generally; no exhaustive host allowlist.
    ["A-heat-pump"] = Array.Empty<string>(),
    ["B-mars-missions"] = new[] { "nasa.gov" },
    ["C-future-claim"] = new[] { "nobelprize.org" }
};
var citedUrl = new Regex(@"https?://[^\s\)\]\>]+",
    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
    TimeSpan.FromSeconds(1));

using var archive = ZipFile.OpenRead(args[0]);
foreach (ZipArchiveEntry entry in archive.Entries)
{
    if (!entry.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || entry.Length > 4_000_000)
        continue;
    using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
    string text = reader.ReadToEnd();
    string caseName = Path.GetFileNameWithoutExtension(entry.Name);
    string[] desired = domains.TryGetValue(caseName, out string[]? rule) ? rule : Array.Empty<string>();
    var requirement = new EvidenceRequirement(desired);
    var uniqueHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    int citations = 0, outside = 0, unusable = 0, thirdParty = 0;
    foreach (Match match in citedUrl.Matches(text))
    {
        string url = match.Value.TrimEnd('.', ',', ';', ':', '"', '\'', '`');
        if (!EvidenceLedger.IsPublicHttpsUrl(url, out Uri? uri)) { unusable++; continue; }
        citations++;
        uniqueHosts.Add(uri!.Host);
        if (!requirement.IsAllowedDomain(uri.Host)) outside++;
        if (uri.Host.Equals("en.wikipedia.org", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.Equals("www.youtube.com", StringComparison.OrdinalIgnoreCase)) thirdParty++;
    }
    bool complete = Regex.IsMatch(text, @"(?m)^STATUS:\s*Completed\s*$", RegexOptions.CultureInvariant);
    bool encodingDamage = text.Contains("ΓÇ", StringComparison.Ordinal) ||
        text.Contains("Γê", StringComparison.Ordinal) || text.Contains('�');
    Console.WriteLine(caseName + ": " + (complete ? "Completed" : "NotCompleted") +
        " | citation_links=" + citations + " | domains=" + uniqueHosts.Count +
        " | outside_requested_domains=" + outside + " | encyclopedia_or_video_links=" + thirdParty +
        " | unusable_urls=" + unusable +
        " | possible_mojibake=" + encodingDamage);
    if (outside > 0)
        Console.WriteLine("  REVIEW: Citation host does not match the specified source-domain preference.");
    if (thirdParty > 0)
        Console.WriteLine("  REVIEW: Encyclopedias/video hosts were cited; check primary source preference.");
    if (encodingDamage)
        Console.WriteLine("  REVIEW: Captured output contains suspicious Unicode encoding sequences.");
    Console.WriteLine("  NOTE: URL identity and domain matching do not prove claim-level support.");
}
return 0;
