using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace NIRAAgent.Capabilities;

// R43A: single-section header/footer/page-number editing via strict copy-on-write.
// Rejects documents with existing headers/footers, signed/macro/tracked edits,
// or advanced page-section settings. Never modifies the source or existing output.
internal static partial class NIRADocx
{
    private const string DocumentRels = "word/_rels/document.xml.rels";
    private const string HeaderPart = "word/headerNIRA43.xml";
    private const string FooterPart = "word/footerNIRA43.xml";
    private static readonly XNamespace PkgRel = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace TypesNs = "http://schemas.openxmlformats.org/package/2006/content-types";
    private const string RelPrefix = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";

    private static string? CleanOptionalLine(NIRACapabilityRequest request, string name)
    {
        string? text = NIRACapabilityArguments.GetOptionalRawString(request, name, 160);
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (text.Contains('\n') || text.Contains('\r') || text.Contains('\t'))
            throw new ArgumentException($"{name} must be one plain line.");
        XmlConvert.VerifyXmlChars(text);
        return text.Trim();
    }

    private static XDocument PartDocument(bool header, string? text, bool pageNumber)
    {
        XElement para = new(W + "p",
            new XElement(W + "pPr",
                new XElement(W + "jc", new XAttribute(W + "val", header ? "right" : "center"))));
        if (!string.IsNullOrEmpty(text))
            para.Add(new XElement(W + "r", new XElement(W + "t",
                new XAttribute(XNamespace.Xml + "space", "preserve"), text)));
        if (!header && pageNumber)
        {
            if (!string.IsNullOrEmpty(text))
                para.Add(new XElement(W + "r", new XElement(W + "t",
                    new XAttribute(XNamespace.Xml + "space", "preserve"), " | Page ")));
            else
                para.Add(new XElement(W + "r", new XElement(W + "t", "Page ")));
            para.Add(new XElement(W + "fldSimple", new XAttribute(W + "instr", " PAGE "),
                new XElement(W + "r", new XElement(W + "t", "1"))));
        }
        return new XDocument(new XElement(header ? W + "hdr" : W + "ftr",
            new XAttribute(XNamespace.Xmlns + "w", W), para));
    }

    private static void VerifyEligible(ZipArchive archive, XDocument doc)
    {
        XElement[] sections = doc.Descendants(W + "sectPr").ToArray();
        if (sections.Length != 1 ||
            sections[0].Descendants(W + "headerReference").Any() ||
            sections[0].Descendants(W + "footerReference").Any() ||
            sections[0].Descendants(W + "titlePg").Any() ||
            doc.Descendants(W + "sectPrChange").Any())
            throw new InvalidOperationException("R43A supports one section with no pre-existing headers, footers, or different-first-page settings.");
        if (archive.Entries.Any(x => x.FullName.StartsWith("word/header", StringComparison.OrdinalIgnoreCase) ||
                                     x.FullName.StartsWith("word/footer", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Document already contains header/footer parts; R43A refuses to overwrite or coexist with them.");
        ZipArchiveEntry? settings = archive.GetEntry("word/settings.xml");
        if (settings != null)
        {
            using Stream stream = settings.Open();
            var xml = LoadXml(stream);
            if (xml.Descendants(W + "evenAndOddHeaders").Any())
                throw new InvalidOperationException("Even/odd header settings require a later multi-section editor.");
        }
        if (archive.GetEntry(DocumentRels) is null)
            throw new InvalidDataException("Document relationship part is missing.");
    }

    private static void MakeHeaderFooterCopy(string source, string target, XDocument updated,
        XDocument? headerDoc, XDocument? footerDoc, string? headerId, string? footerId)
    {
        using FileStream input = new(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        using ZipArchive from = new(input, ZipArchiveMode.Read);
        ValidatePackage(from);
        VerifyEligible(from, OpenDocument(from));
        using FileStream file = new(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using (ZipArchive to = new(file, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (ZipArchiveEntry entry in from.Entries)
            {
                if (entry.FullName == DocumentPart)
                {
                    WriteXml(to, DocumentPart, updated);
                    continue;
                }
                if (entry.FullName is DocumentRels or ContentTypePart)
                {
                    using Stream stream = entry.Open();
                    XDocument xml = LoadXml(stream);
                    if (entry.FullName == DocumentRels)
                    {
                        if (headerId != null)
                            xml.Root!.Add(new XElement(PkgRel + "Relationship",
                                new XAttribute("Id", headerId),
                                new XAttribute("Type", RelPrefix + "header"),
                                new XAttribute("Target", "headerNIRA43.xml")));
                        if (footerId != null)
                            xml.Root!.Add(new XElement(PkgRel + "Relationship",
                                new XAttribute("Id", footerId),
                                new XAttribute("Type", RelPrefix + "footer"),
                                new XAttribute("Target", "footerNIRA43.xml")));
                    }
                    else
                    {
                        if (headerDoc != null)
                            xml.Root!.Add(new XElement(TypesNs + "Override",
                                new XAttribute("PartName", "/" + HeaderPart),
                                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml")));
                        if (footerDoc != null)
                            xml.Root!.Add(new XElement(TypesNs + "Override",
                                new XAttribute("PartName", "/" + FooterPart),
                                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml")));
                    }
                    WriteXml(to, entry.FullName, xml);
                    continue;
                }
                ZipArchiveEntry copied = to.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                using Stream read = entry.Open();
                using Stream write = copied.Open();
                read.CopyTo(write);
            }
            if (headerDoc != null) WriteXml(to, HeaderPart, headerDoc);
            if (footerDoc != null) WriteXml(to, FooterPart, footerDoc);
        }
        file.Flush(true);
    }

    // Returns evidence based on RELATIONSHIPS and actual parts, never on model claims.
    private static string InspectHeaderFooterParts(ZipArchive zip, XDocument doc)
    {
        XElement? sec = doc.Descendants(W + "sectPr").SingleOrDefault();
        if (sec == null) return "HeadersFooters=UnsupportedMultipleSections";
        var refs = sec.Elements().Where(x => x.Name == W + "headerReference" || x.Name == W + "footerReference").ToArray();
        if (refs.Length == 0) return "HeaderPresent=False\nFooterPresent=False\nPageNumberField=False";
        ZipArchiveEntry? relEntry = zip.GetEntry(DocumentRels);
        if (relEntry == null) return "HeadersFooters=InvalidMissingRelationships";
        XDocument rels;
        using (Stream stream = relEntry.Open()) rels = LoadXml(stream);
        List<string> data = new();
        foreach (XElement reference in refs.Take(4))
        {
            bool header = reference.Name == W + "headerReference";
            string kind = header ? "Header" : "Footer";
            string? id = (string?)reference.Attribute(R + "id");
            XElement? relation = rels.Descendants(PkgRel + "Relationship")
                .SingleOrDefault(x => (string?)x.Attribute("Id") == id);
            string? target = (string?)relation?.Attribute("Target");
            if ((string?)relation?.Attribute("Type") != RelPrefix + (header ? "header" : "footer") ||
                target == null || target.StartsWith('/') || target.Contains("..") || target.Contains('\\') ||
                target.Contains(':') || !target.StartsWith(header ? "header" : "footer", StringComparison.Ordinal))
                return $"HeadersFooters=Invalid{kind}Relationship";
            ZipArchiveEntry? part = zip.GetEntry("word/" + target);
            if (part == null) return $"HeadersFooters=Missing{kind}Part";
            XDocument xml;
            using (Stream stream = part.Open()) xml = LoadXml(stream);
            if (xml.Root?.Name != (header ? W + "hdr" : W + "ftr"))
                return $"HeadersFooters=Invalid{kind}Part";
            string content = string.Concat(xml.Descendants(W + "t").Select(x => x.Value));
            bool pageField = xml.Descendants(W + "fldSimple").Any(x =>
                string.Equals(((string?)x.Attribute(W + "instr"))?.Trim(), "PAGE", StringComparison.OrdinalIgnoreCase));
            if (content.Length > 400) content = content[..400];
            data.Add($"{kind}Present=True");
            data.Add($"{kind}TextJson={System.Text.Json.JsonSerializer.Serialize(content)}");
            if (!header) data.Add($"PageNumberField={pageField}");
        }
        if (!data.Any(x => x.StartsWith("HeaderPresent=", StringComparison.Ordinal))) data.Add("HeaderPresent=False");
        if (!data.Any(x => x.StartsWith("FooterPresent=", StringComparison.Ordinal)))
            data.AddRange(["FooterPresent=False", "PageNumberField=False"]);
        return string.Join("\n", data);
    }

    internal static async Task<NIRACapabilityHandlerResult> HeaderFooterAsync(
        NIRACapabilityRequest request, CancellationToken ct)
    {
        string source = GetPath(request, "source");
        string destination = GetPath(request, "destination");
        NIRAFileIntegrity.Different(source, destination);
        string expected = NIRACapabilityArguments.RequireString(request, "expectedSourceSha256", 64);
        if (expected.Length != 64 || expected.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("Expected a fresh exact SHA-256 for the original DOCX.");
        string? headerText = CleanOptionalLine(request, "headerText");
        string? footerText = CleanOptionalLine(request, "footerText");
        string? pageText = NIRACapabilityArguments.GetOptionalRawString(request, "pageNumbers", 5);
        if (pageText != null && pageText is not ("true" or "false"))
            throw new ArgumentException("pageNumbers must be 'true' or 'false'.");
        bool pages = pageText == "true";
        if (headerText == null && footerText == null && !pages)
            throw new ArgumentException("Specify headerText, footerText, or pageNumbers=true.");
        RequireValidSource(source);
        NIRAFileIntegrity.RejectLinkedPaths(destination);
        NIRAFileIntegrity.RequireNewDestination(destination);
        var before = await NIRAFileIntegrity.HashAsync(source, ct).ConfigureAwait(false);
        if (!string.Equals(before.Sha256, expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Source SHA-256 is stale; no document created.");

        XDocument main;
        string? headerId = null, footerId = null;
        XDocument? hdr = headerText is null ? null : PartDocument(true, headerText, false);
        XDocument? ftr = (footerText != null || pages) ? PartDocument(false, footerText, pages) : null;
        using (FileStream stream = new(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (ZipArchive archive = new(stream, ZipArchiveMode.Read))
        {
            main = OpenDocument(archive);
            VerifyEligible(archive, main);
            // Declare the relationship prefix explicitly so saved/reopened XML
            // compares exactly in PublishNew, including namespace attributes.
            main.Root!.SetAttributeValue(XNamespace.Xmlns + "r", R);
            using Stream relStream = archive.GetEntry(DocumentRels)!.Open();
            XDocument rels = LoadXml(relStream);
            var taken = new HashSet<string>(rels.Descendants(PkgRel + "Relationship")
                .Select(x => (string?)x.Attribute("Id") ?? ""), StringComparer.Ordinal);
            string NextId()
            {
                for (int n = 1; n < 10000; n++)
                    if (taken.Add("rId" + n)) return "rId" + n;
                throw new InvalidDataException("Too many document relationships.");
            }
            XElement sect = main.Descendants(W + "sectPr").Single();
            if (hdr != null)
            {
                headerId = NextId();
                sect.AddFirst(new XElement(W + "headerReference",
                    new XAttribute(W + "type", "default"), new XAttribute(R + "id", headerId)));
            }
            if (ftr != null)
            {
                footerId = NextId();
                XElement footerRef = new(W + "footerReference",
                    new XAttribute(W + "type", "default"), new XAttribute(R + "id", footerId));
                XElement? previous = sect.Elements(W + "headerReference").LastOrDefault();
                if (previous == null) sect.AddFirst(footerRef);
                else previous.AddAfterSelf(footerRef);
            }
        }
        var recheck = await NIRAFileIntegrity.HashAsync(source, ct).ConfigureAwait(false);
        if (!string.Equals(recheck.Sha256, expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Source changed while preparing header/footer; nothing published.");
        ct.ThrowIfCancellationRequested();
        PublishNew(destination, path => MakeHeaderFooterCopy(source, path, main,
            hdr, ftr, headerId, footerId), main);

        string evidence;
        using (FileStream stream = new(destination, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (ZipArchive archive = new(stream, ZipArchiveMode.Read))
        {
            XDocument verified = OpenDocument(archive);
            evidence = InspectHeaderFooterParts(archive, verified);
            if (evidence.Contains("Invalid", StringComparison.Ordinal) ||
                (hdr != null && !evidence.Contains("HeaderPresent=True", StringComparison.Ordinal)) ||
                (ftr != null && !evidence.Contains("FooterPresent=True", StringComparison.Ordinal)) ||
                (pages && !evidence.Contains("PageNumberField=True", StringComparison.Ordinal)))
                throw new InvalidDataException("The published DOCX header/footer could not be verified.");
            if (hdr != null && !evidence.Contains(System.Text.Json.JsonSerializer.Serialize(headerText), StringComparison.Ordinal))
                throw new InvalidDataException("Header content verification failed.");
            if (ftr != null && footerText != null && !evidence.Contains(footerText, StringComparison.Ordinal))
                throw new InvalidDataException("Footer content verification failed.");
        }
        var after = await NIRAFileIntegrity.HashAsync(source, CancellationToken.None).ConfigureAwait(false);
        var output = await NIRAFileIntegrity.HashAsync(destination, CancellationToken.None).ConfigureAwait(false);
        if (!string.Equals(after.Sha256, expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Source was concurrently changed; the new copy needs manual review.");
        return new NIRACapabilityHandlerResult
        {
            ChangedSystemState = true,
            Summary = "New DOCX copy published with native verified header/footer and optional page-number field.",
            Output = $"Source={source}\nSourceSHA256={before.Sha256}\nDestination={destination}\nDestinationSHA256={output.Sha256}\nDestinationBytes={output.Bytes}\n{evidence}\nPostWriteVerified=True\nSourceUnchanged=True\nOverwroteExisting=False"
        };
    }
}

public sealed class NIRADocxHeaderFooterCapabilityHandler : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor { get; } = new()
    {
        Id = NIRACapabilityIds.DocxHeaderFooter,
        DefaultRisk = NIRACapabilityRisk.Modify,
        Description = "Add a one-line document header, footer, and optional live PAGE field to a NEW Word DOCX copy. Single-section documents with no existing headers/footers only. Fresh SHA-256 and no-overwrite required; does not require Microsoft Word.",
        Parameters = [
            NIRADocx.Param("source", true, "Existing single-section DOCX; read only."),
            NIRADocx.Param("destination", true, "NEW nonexisting DOCX output path in authorized folder."),
            NIRADocx.Param("expectedSourceSha256", true, "Fresh exact SHA-256 from filesystem.hash or filesystem.docx.inspect."),
            NIRADocx.Param("headerText", false, "Optional single-line right-aligned header (<=160 chars)."),
            NIRADocx.Param("footerText", false, "Optional single-line centered footer (<=160 chars)."),
            NIRADocx.Param("pageNumbers", false, "Set string 'true' for an automatic footer PAGE field; otherwise 'false'.")
        ]
    };
    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest request) => NIRACapabilityRisk.Modify;
    public Task<NIRACapabilityHandlerResult> ExecuteAsync(NIRACapabilityRequest request,
        CancellationToken cancellationToken = default) => NIRADocx.HeaderFooterAsync(request, cancellationToken);
}
