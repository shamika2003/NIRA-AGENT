using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace NIRAAgent.Capabilities;

// R46: native, copy-on-write whole-document design profiles and independent
// content comparison. Does not use Microsoft Word, COM, shell or network.
internal static partial class NIRADocx
{
    private static XElement EnsureElement46(XElement parent, string name, params string[] before)
    {
        XElement? found = parent.Element(W + name);
        if (found != null) return found;
        found = new XElement(W + name);
        XElement? successor = parent.Elements().FirstOrDefault(e =>
            before.Contains(e.Name.LocalName, StringComparer.Ordinal));
        if (successor != null) successor.AddBeforeSelf(found);
        else parent.Add(found);
        return found;
    }

    private static void RunAttribute46(XElement properties, string name, string value)
    {
        string[] before = name switch
        {
            "rFonts" => ["b", "bCs", "i", "iCs", "color", "spacing", "sz", "szCs", "u", "lang"],
            "color" => ["spacing", "w", "kern", "position", "sz", "szCs", "highlight", "u", "lang"],
            "sz" => ["szCs", "highlight", "u", "vertAlign", "lang"],
            _ => []
        };
        XElement child = EnsureElement46(properties, name, before);
        child.SetAttributeValue(W + (name == "rFonts" ? "ascii" : "val"), value);
        if (name == "rFonts") child.SetAttributeValue(W + "hAnsi", value);
    }

    private static void Spacing46(XElement properties, int before, int after, int line)
    {
        XElement spacing = EnsureElement46(properties, "spacing", "ind", "contextualSpacing",
            "mirrorIndents", "suppressOverlap", "jc", "textDirection", "rPr", "sectPr", "pPrChange");
        spacing.SetAttributeValue(W + "before", before);
        spacing.SetAttributeValue(W + "after", after);
        spacing.SetAttributeValue(W + "line", line);
        spacing.SetAttributeValue(W + "lineRule", "auto");
    }

    private static XDocument WithoutDirectFormatting46(XDocument source)
    {
        XDocument stripped = new(source);
        // Ignore only the specific paragraph/run presentation fields managed
        // by a profile. Preserve every non-formatting property for comparison.
        foreach (XElement p in stripped.Descendants(W + "p"))
        {
            XElement? ppr = p.Element(W + "pPr");
            if (ppr != null)
            {
                foreach (string field in new[] { "keepNext", "spacing", "jc" })
                    ppr.Element(W + field)?.Remove();
                if (!ppr.HasElements && !ppr.HasAttributes) ppr.Remove();
            }
            foreach (XElement r in p.Descendants(W + "r"))
            {
                XElement? rpr = r.Element(W + "rPr");
                if (rpr == null) continue;
                foreach (string field in new[] { "rFonts", "sz", "color", "b" })
                    rpr.Element(W + field)?.Remove();
                if (!rpr.HasElements && !rpr.HasAttributes) rpr.Remove();
            }
        }
        return stripped;
    }

    private static bool SameNodes46(IEnumerable<XElement> a, IEnumerable<XElement> b)
    {
        XElement[] first = a.ToArray(), second = b.ToArray();
        return first.Length == second.Length && first.Zip(second,
            (x, y) => XNode.DeepEquals(x, y)).All(equal => equal);
    }

    private static bool OtherPartsEqual46(ZipArchive source, ZipArchive candidate)
    {
        if (source.Entries.Count != candidate.Entries.Count) return false;
        foreach (ZipArchiveEntry part in source.Entries)
        {
            if (part.FullName == DocumentPart) continue;
            ZipArchiveEntry? other = candidate.GetEntry(part.FullName);
            if (other == null || part.Length != other.Length) return false;
            using Stream a = part.Open();
            using Stream b = other.Open();
            if (!SHA256.HashData(a).AsSpan().SequenceEqual(SHA256.HashData(b))) return false;
        }
        return true;
    }

    private sealed record Comparison46(bool BodyContent, bool Tables, bool Sections,
        bool OtherParts, bool FormattingChanged, int SourceParagraphs, int CandidateParagraphs,
        int TablesCount, int ImagesCount)
    {
        internal bool Preserved => BodyContent && Tables && Sections && OtherParts;
    }

    private static Comparison46 ComparePackages46(string source, string candidate)
    {
        using ZipArchive a = ZipFile.OpenRead(source);
        using ZipArchive b = ZipFile.OpenRead(candidate);
        XDocument first = OpenDocument(a);
        XDocument second = OpenDocument(b);
        XElement sourceBody = first.Root!.Element(W + "body")!;
        XElement targetBody = second.Root!.Element(W + "body")!;
        bool body = XNode.DeepEquals(WithoutDirectFormatting46(first).Root,
            WithoutDirectFormatting46(second).Root);
        bool tables = SameNodes46(first.Descendants(W + "tbl"), second.Descendants(W + "tbl"));
        bool sections = SameNodes46(first.Descendants(W + "sectPr"), second.Descendants(W + "sectPr"));
        bool other = OtherPartsEqual46(a, b);
        int paragraphs(XElement elem) => elem.Descendants(W + "p")
            .Count(p => !string.IsNullOrWhiteSpace(string.Concat(p.Descendants(W + "t").Select(t => t.Value))));
        return new Comparison46(body, tables, sections, other,
            !XNode.DeepEquals(first.Root, second.Root), paragraphs(sourceBody),
            paragraphs(targetBody), second.Descendants(W + "tbl").Count(),
            b.Entries.Count(e => e.FullName.StartsWith("word/media/", StringComparison.OrdinalIgnoreCase)));
    }

    internal static async Task<NIRACapabilityHandlerResult> ProfessionalFormat46Async(
        NIRACapabilityRequest request, CancellationToken cancellationToken)
    {
        string source = GetPath(request, "source");
        string destination = GetPath(request, "destination");
        NIRAFileIntegrity.Different(source, destination);
        string expected = NIRACapabilityArguments.RequireString(request, "expectedSourceSha256", 64);
        if (expected.Length != 64 || expected.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("expectedSourceSha256 requires a freshly observed SHA-256.");
        string profile = (NIRACapabilityArguments.GetOptionalRawString(request, "profile", 16) ?? "business")
            .ToLowerInvariant();
        string accent = (NIRACapabilityArguments.GetOptionalRawString(request, "accentHex", 6) ?? "17365D")
            .ToUpperInvariant();
        if (profile is not ("business" or "academic" or "compact") ||
            !Regex.IsMatch(accent, "^[0-9A-F]{6}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("profile must be business, academic or compact; accentHex must be six RGB hex digits.");
        RequireValidSource(source);
        NIRAFileIntegrity.RejectLinkedPaths(destination);
        NIRAFileIntegrity.RequireNewDestination(destination);
        var observed = await NIRAFileIntegrity.HashAsync(source, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(observed.Sha256, expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Source SHA-256 is stale; formatting was not started.");

        XDocument updated;
        int formatted = 0, skippedComplex = 0, headings = 0;
        using (ZipArchive package = ZipFile.OpenRead(source))
        {
            updated = OpenDocument(package);
            XElement body = updated.Root!.Element(W + "body")!;
            // A different section's page setup must never be silently changed.
            XElement[] sections = body.Descendants(W + "sectPr").ToArray();
            if (sections.Length != 1 || sections[0].Parent != body)
                throw new InvalidOperationException("Professional profile currently requires one final body section.");
            XElement[] paragraphs = body.Elements(W + "p").ToArray();
            XElement? firstText = paragraphs.FirstOrDefault(p => p.Descendants(W + "t").Any(t =>
                !string.IsNullOrWhiteSpace(t.Value)));
            foreach (XElement p in paragraphs)
            {
                XElement[] runs = p.Elements(W + "r").ToArray();
                if (runs.Length == 0 || p.Descendants(W + "drawing").Any() ||
                    p.Descendants(W + "br").Any() || p.Descendants(W + "fldChar").Any() ||
                    p.Elements().Any(e => e.Name != W + "pPr" && e.Name != W + "r"))
                { skippedComplex++; continue; }
                string text = string.Concat(p.Descendants(W + "t").Select(t => t.Value));
                if (string.IsNullOrWhiteSpace(text)) continue;
                string style = (string?)p.Element(W + "pPr")?.Element(W + "pStyle")?.Attribute(W + "val") ?? "";
                bool heading = style.StartsWith("Heading", StringComparison.OrdinalIgnoreCase);
                bool title = style.Equals("Title", StringComparison.OrdinalIgnoreCase) ||
                    (p == firstText && heading);
                bool caption = style.Equals("Caption", StringComparison.OrdinalIgnoreCase) ||
                    text.StartsWith("Figure ", StringComparison.OrdinalIgnoreCase);
                if (heading && !title) headings++;
                XElement ppr = p.Element(W + "pPr") ?? new XElement(W + "pPr");
                if (ppr.Parent == null) p.AddFirst(ppr);
                if (title || heading)
                    EnsureElement46(ppr, "keepNext", "keepLines", "spacing", "jc", "rPr", "sectPr");
                int before = title ? 0 : heading ? 180 : caption ? 0 : 0;
                int after = title ? 200 : heading ? 90 : caption ? 110 :
                    profile == "compact" ? 50 : profile == "academic" ? 150 : 100;
                int line = profile == "academic" ? 288 : profile == "compact" ? 240 : 276;
                Spacing46(ppr, before, after, line);
                XElement jc = EnsureElement46(ppr, "jc", "textDirection", "rPr", "sectPr", "pPrChange");
                jc.SetAttributeValue(W + "val", title || caption ? "center" : "left");

                bool simple = runs.All(r => r.Elements().All(e => e.Name == W + "t" || e.Name == W + "rPr"));
                if (!simple) { skippedComplex++; formatted++; continue; }
                int halfPoints = title ? (profile == "compact" ? 38 : 44) : heading ? 30 :
                    caption ? 18 : profile == "compact" ? 20 : profile == "academic" ? 24 : 22;
                foreach (XElement run in runs)
                {
                    XElement properties = run.Element(W + "rPr") ?? new XElement(W + "rPr");
                    if (properties.Parent == null) run.AddFirst(properties);
                    RunAttribute46(properties, "rFonts", "Calibri");
                    RunAttribute46(properties, "sz", halfPoints.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    if (title || heading)
                        RunAttribute46(properties, "color", accent);
                    if (title || heading)
                    {
                        XElement bold = EnsureElement46(properties, "b", "color", "sz", "szCs", "lang");
                        bold.SetAttributeValue(W + "val", "1");
                    }
                }
                formatted++;
            }
        }
        if (formatted == 0)
            throw new InvalidOperationException("No eligible native Word body paragraphs could be formatted.");
        var beforeWrite = await NIRAFileIntegrity.HashAsync(source, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(beforeWrite.Sha256, expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Source changed during preparation; no output was written.");
        cancellationToken.ThrowIfCancellationRequested();
        PublishNew(destination, path => Repackage(source, path, updated), updated);
        string verifiedXml = VerifyPublishedDocument(destination, updated);
        Comparison46 comparison = ComparePackages46(source, destination);
        if (!comparison.Preserved)
            throw new InvalidDataException("Post-write content, table, section or unrelated package preservation failed.");
        var sourceAfter = await NIRAFileIntegrity.HashAsync(source, CancellationToken.None).ConfigureAwait(false);
        var destAfter = await NIRAFileIntegrity.HashAsync(destination, CancellationToken.None).ConfigureAwait(false);
        if (!string.Equals(sourceAfter.Sha256, expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Original changed externally after publication; check output before use.");
        return new NIRACapabilityHandlerResult
        {
            ChangedSystemState = true,
            Summary = "R46: professional whole-document formatting applied in one verified new DOCX copy; content and unrelated package parts preserved.",
            Output = $"Source={source}\nSourceSHA256={sourceAfter.Sha256}\nDestination={destination}\nDestinationSHA256={destAfter.Sha256}\n" +
                $"Profile={profile}\nAccentHex={accent}\nFormattedParagraphs={formatted}\nSkippedComplexParagraphs={skippedComplex}\n" +
                $"SourceAndDestinationContentIdentical=True\nTablesPreserved=True\nSectionsPreserved=True\n" +
                $"OtherPackagePartsPreserved=True\nEmbeddedMediaParts={comparison.ImagesCount}\n" +
                $"{verifiedXml}\nSourceUnchanged=True\nOverwroteExisting=False"
        };
    }

    internal static async Task<NIRACapabilityHandlerResult> ContentCompare46Async(
        NIRACapabilityRequest request, CancellationToken cancellationToken)
    {
        string source = GetPath(request, "source");
        string candidate = GetPath(request, "candidate");
        NIRAFileIntegrity.Different(source, candidate);
        RequireValidSource(source);
        RequireValidSource(candidate);
        var sourceHash = await NIRAFileIntegrity.HashAsync(source, cancellationToken).ConfigureAwait(false);
        var targetHash = await NIRAFileIntegrity.HashAsync(candidate, cancellationToken).ConfigureAwait(false);
        Comparison46 result = ComparePackages46(source, candidate);
        return new NIRACapabilityHandlerResult
        {
            ChangedSystemState = false,
            Summary = "Read-only native DOCX content/structure comparison with independently observed SHA-256 values.",
            Output = $"Source={source}\nSourceSHA256={sourceHash.Sha256}\nCandidate={candidate}\nCandidateSHA256={targetHash.Sha256}\n" +
                $"BodyContentIdentical={result.BodyContent}\nTablesIdentical={result.Tables}\nSectionsIdentical={result.Sections}\n" +
                $"OtherPackagePartsIdentical={result.OtherParts}\nFormattingOnlyDifference={result.Preserved && result.FormattingChanged}\n" +
                $"SourceParagraphs={result.SourceParagraphs}\nCandidateParagraphs={result.CandidateParagraphs}\n" +
                $"CandidateTables={result.TablesCount}\nCandidateEmbeddedMediaParts={result.ImagesCount}\n" +
                "NoFilesModified=True\nVisualPaginationVerified=False"
        };
    }
}

public sealed class NIRADocxProfessionalFormatCapabilityHandler : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor { get; } = new()
    {
        Id = NIRACapabilityIds.DocxProfessionalFormat,
        DefaultRisk = NIRACapabilityRisk.Modify,
        Description = "R46 one-call professional Word document formatting in a NEW DOCX copy; business/academic/compact profiles, optional RGB accent. Preserves every body text element, complete tables, images, headers/footers, PAGE fields and page setup; reopens and audits the saved copy. Requires fresh source SHA-256 and refuses overwrites.",
        Parameters = [
            NIRADocx.Param("source", true, "Existing Word .docx, read only."),
            NIRADocx.Param("destination", true, "NEW unused Word .docx destination; no overwrite."),
            NIRADocx.Param("expectedSourceSha256", true, "Fresh source SHA-256 from filesystem.docx.inspect."),
            NIRADocx.Param("profile", false, "Optional: business (default), academic or compact."),
            NIRADocx.Param("accentHex", false, "Optional six-digit RGB accent without #, default 17365D.")
        ]
    };
    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest request) => NIRACapabilityRisk.Modify;
    public Task<NIRACapabilityHandlerResult> ExecuteAsync(NIRACapabilityRequest request,
        CancellationToken cancellationToken = default) =>
        NIRADocx.ProfessionalFormat46Async(request, cancellationToken);
}

public sealed class NIRADocxContentCompareCapabilityHandler : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor { get; } = new()
    {
        Id = NIRACapabilityIds.DocxContentCompare,
        DefaultRisk = NIRACapabilityRisk.Observe,
        Description = "R46 read-only comparison of two existing Word DOCX files. Reports fresh hashes and whether body content, complete tables, sections and all other ZIP parts are identical, allowing presentation-only changes without inventing rendered pagination.",
        Parameters = [
            NIRADocx.Param("source", true, "Existing .docx source."),
            NIRADocx.Param("candidate", true, "Existing .docx candidate to compare.")
        ]
    };
    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest request) => NIRACapabilityRisk.Observe;
    public Task<NIRACapabilityHandlerResult> ExecuteAsync(NIRACapabilityRequest request,
        CancellationToken cancellationToken = default) =>
        NIRADocx.ContentCompare46Async(request, cancellationToken);
}
