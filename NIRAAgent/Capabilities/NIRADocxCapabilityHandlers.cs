using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace NIRAAgent.Capabilities;

// R41 (stage 14.9): pure .NET Open XML DOCX support, no Word/COM/third-party tools.
// Only NEW outputs are written. Existing source packages are never modified.
internal static class NIRADocx
{
    internal static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const long MaxPackageBytes = 16L * 1024 * 1024;
    private const long MaxExpandedBytes = 64L * 1024 * 1024;
    private const int MaxEntries = 1024;
    private const string DocumentPart = "word/document.xml";
    private const string ContentTypePart = "[Content_Types].xml";
    private const string RelationshipPart = "_rels/.rels";

    internal static NIRACapabilityParameterDescriptor Param(string name, bool required, string description) =>
        new() { Name = name, Type = "string", Required = required, Description = description };

    internal static string GetPath(NIRACapabilityRequest req, string name)
    {
        string path = NIRAFileIntegrity.PathArgument(req, name);
        if (!string.Equals(Path.GetExtension(path), ".docx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{name} must have the .docx extension.");
        return path;
    }

    internal static string RequiredText(NIRACapabilityRequest req, string name, int maxChars)
    {
        string text = NIRACapabilityArguments.RequireRawString(req, name, maxChars);
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException($"{name} needs non-empty text.");
        try { XmlConvert.VerifyXmlChars(text); }
        catch (XmlException) { throw new ArgumentException($"{name} contains XML-invalid characters."); }
        return text;
    }

    private static void RequireValidSource(string path)
    {
        NIRAFileIntegrity.RejectLinkedPaths(path);
        FileInfo file = new(path);
        if (!file.Exists || (file.Attributes & FileAttributes.Directory) != 0)
            throw new FileNotFoundException("DOCX source must be an existing file.", path);
        if (file.Length == 0 || file.Length > MaxPackageBytes)
            throw new InvalidOperationException("DOCX source must be between 1 byte and 16 MiB.");
    }

    private static XmlReader SecureReader(Stream stream)
    {
        return XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxExpandedBytes,
            CloseInput = false
        });
    }

    private static XDocument LoadXml(Stream stream)
    {
        using XmlReader reader = SecureReader(stream);
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }

    private static void ValidatePackage(ZipArchive archive)
    {
        if (archive.Entries.Count < 3 || archive.Entries.Count > MaxEntries)
            throw new InvalidDataException("Unsupported DOCX package entry count.");
        long total = 0;
        HashSet<string> entries = new(StringComparer.OrdinalIgnoreCase);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string name = entry.FullName;
            if (string.IsNullOrWhiteSpace(name) || name.StartsWith('/') || name.Contains('\\') ||
                name.Split('/').Any(segment => segment is ".." or ".") || !entries.Add(name))
                throw new InvalidDataException("Unsafe or duplicate DOCX package entry.");
            total += entry.Length;
            if (entry.Length > MaxExpandedBytes || total > MaxExpandedBytes)
                throw new InvalidDataException("DOCX expands beyond the 64 MiB inspection limit.");
            if (name.StartsWith("_xmlsignatures/", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Signed/macro-bearing packages are not supported by R41.");
        }
        if (!entries.Contains(DocumentPart) || !entries.Contains(ContentTypePart) ||
            !entries.Contains(RelationshipPart))
            throw new InvalidDataException("Missing mandatory DOCX package parts.");
        using (Stream content = archive.GetEntry(ContentTypePart)!.Open())
        {
            XDocument types = LoadXml(content);
            XNamespace ct = "http://schemas.openxmlformats.org/package/2006/content-types";
            if (!types.Descendants(ct + "Override").Any(x =>
                string.Equals((string?)x.Attribute("PartName"), "/word/document.xml", StringComparison.OrdinalIgnoreCase) &&
                string.Equals((string?)x.Attribute("ContentType"),
                    "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml", StringComparison.Ordinal)))
                throw new InvalidDataException("Not a normal .docx Word document (wrong main content type).");
        }
        using (Stream relations = archive.GetEntry(RelationshipPart)!.Open())
        {
            XDocument rels = LoadXml(relations);
            XNamespace pr = "http://schemas.openxmlformats.org/package/2006/relationships";
            if (!rels.Descendants(pr + "Relationship").Any(x =>
                ((string?)x.Attribute("Type"))?.EndsWith("/officeDocument", StringComparison.Ordinal) == true &&
                string.Equals((string?)x.Attribute("Target"), "word/document.xml", StringComparison.Ordinal)))
                throw new InvalidDataException("DOCX root is missing its document relationship.");
        }
    }

    private static XDocument OpenDocument(ZipArchive archive)
    {
        ValidatePackage(archive);
        using Stream input = archive.GetEntry(DocumentPart)!.Open();
        XDocument xml = LoadXml(input);
        if (xml.Root?.Name != W + "document" || xml.Root.Element(W + "body") is null)
            throw new InvalidDataException("Word document body is missing.");
        if (xml.Descendants(W + "ins").Any() || xml.Descendants(W + "del").Any() ||
            xml.Descendants(W + "moveFrom").Any() || xml.Descendants(W + "moveTo").Any() ||
            xml.Descendants(W + "altChunk").Any())
            throw new InvalidDataException("Tracked changes or external-content chunks require a later DOCX editor.");
        return xml;
    }

    private static IEnumerable<(string Text, int Heading)> Lines(string content)
    {
        foreach (string line in content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            string trimmed = line.TrimEnd();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;
            int level = trimmed.StartsWith("## ", StringComparison.Ordinal) ? 2 :
                trimmed.StartsWith("# ", StringComparison.Ordinal) ? 1 : 0;
            yield return (level == 0 ? trimmed : trimmed[(level + 1)..], level);
        }
    }

    private static XElement Paragraph(string text, int heading = 0)
    {
        var paragraph = new XElement(W + "p");
        if (heading is 1 or 2)
            paragraph.Add(new XElement(W + "pPr",
                new XElement(W + "pStyle", new XAttribute(W + "val", "Heading" + heading))));
        paragraph.Add(new XElement(W + "r",
            new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text)));
        return paragraph;
    }

    private static void AddText(XElement body, string content)
    {
        var paragraphs = Lines(content).ToArray();
        if (paragraphs.Length == 0 || paragraphs.Length > 300)
            throw new ArgumentException("Provide 1-300 nonempty DOCX paragraphs.");
        XElement? sect = body.Elements(W + "sectPr").LastOrDefault();
        foreach ((string text, int heading) in paragraphs)
        {
            XElement paragraph = Paragraph(text, heading);
            if (sect is null) body.Add(paragraph);
            else sect.AddBeforeSelf(paragraph);
        }
    }

    private static XDocument NewDocument(string content, string? title)
    {
        XElement body = new(W + "body");
        if (!string.IsNullOrWhiteSpace(title)) body.Add(Paragraph(title, 1));
        AddText(body, content);
        body.Add(new XElement(W + "sectPr",
            new XElement(W + "pgSz", new XAttribute(W + "w", 11906), new XAttribute(W + "h", 16838)),
            new XElement(W + "pgMar", new XAttribute(W + "top", 1440), new XAttribute(W + "right", 1440),
                new XAttribute(W + "bottom", 1440), new XAttribute(W + "left", 1440))));
        return new XDocument(new XElement(W + "document", new XAttribute(XNamespace.Xmlns + "w", W), body));
    }

    private static void WriteXml(ZipArchive archive, string part, XDocument document)
    {
        ZipArchiveEntry entry = archive.CreateEntry(part, CompressionLevel.Optimal);
        using Stream stream = entry.Open();
        using XmlWriter writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false), OmitXmlDeclaration = false, CloseOutput = false
        });
        document.Save(writer);
    }

    private static void NewPackage(string path, XDocument xml)
    {
        using FileStream output = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            XNamespace ct = "http://schemas.openxmlformats.org/package/2006/content-types";
            WriteXml(archive, ContentTypePart, new XDocument(new XElement(ct + "Types",
                new XElement(ct + "Default", new XAttribute("Extension", "rels"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(ct + "Default", new XAttribute("Extension", "xml"),
                    new XAttribute("ContentType", "application/xml")),
                new XElement(ct + "Override", new XAttribute("PartName", "/word/document.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml")),
                new XElement(ct + "Override", new XAttribute("PartName", "/word/styles.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml")))));
            XNamespace pr = "http://schemas.openxmlformats.org/package/2006/relationships";
            WriteXml(archive, RelationshipPart, new XDocument(new XElement(pr + "Relationships",
                new XElement(pr + "Relationship", new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                    new XAttribute("Target", "word/document.xml")))));
            WriteXml(archive, DocumentPart, xml);
            WriteXml(archive, "word/styles.xml", new XDocument(new XElement(W + "styles",
                new XAttribute(XNamespace.Xmlns + "w", W),
                new XElement(W + "style", new XAttribute(W + "type", "paragraph"),
                    new XAttribute(W + "default", "1"), new XAttribute(W + "styleId", "Normal"),
                    new XElement(W + "name", new XAttribute(W + "val", "Normal"))),
                HeadingStyle(1, 32), HeadingStyle(2, 26))));
            WriteXml(archive, "word/_rels/document.xml.rels", new XDocument(new XElement(pr + "Relationships",
                new XElement(pr + "Relationship", new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"),
                    new XAttribute("Target", "styles.xml")))));
        }
        output.Flush(true);
    }

    private static XElement HeadingStyle(int level, int size) =>
        new(W + "style", new XAttribute(W + "type", "paragraph"),
            new XAttribute(W + "styleId", "Heading" + level),
            new XElement(W + "name", new XAttribute(W + "val", "heading " + level)),
            new XElement(W + "basedOn", new XAttribute(W + "val", "Normal")),
            new XElement(W + "pPr", new XElement(W + "keepNext")),
            new XElement(W + "rPr", new XElement(W + "b"),
                new XElement(W + "sz", new XAttribute(W + "val", size))));

    private static void Repackage(string original, string output, XDocument updated)
    {
        using FileStream input = new(original, FileMode.Open, FileAccess.Read, FileShare.Read);
        using ZipArchive source = new(input, ZipArchiveMode.Read);
        ValidatePackage(source);
        using FileStream file = new(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using (ZipArchive dest = new(file, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (ZipArchiveEntry entry in source.Entries)
            {
                if (entry.FullName == DocumentPart) WriteXml(dest, DocumentPart, updated);
                else
                {
                    ZipArchiveEntry copied = dest.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                    using Stream from = entry.Open();
                    using Stream to = copied.Open();
                    from.CopyTo(to);
                }
            }
        }
        file.Flush(true);
    }

    // Stage output in same directory. Publication is CreateNew; no caller file overwritten.
    private static string PublishNew(string destination, Action<string> create, XDocument? intended = null)
    {
        NIRAFileIntegrity.RejectLinkedPaths(destination);
        NIRAFileIntegrity.RequireNewDestination(destination);
        string temp = destination + ".nira-docx-stage-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            create(temp);
            using (FileStream check = new(temp, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (ZipArchive zip = new(check, ZipArchiveMode.Read))
            {
                XDocument stored = OpenDocument(zip);
                if (intended != null && !XNode.DeepEquals(intended.Root, stored.Root))
                    throw new InvalidDataException("Staged DOCX failed exact XML verification; nothing was published.");
            }
            File.Move(temp, destination, overwrite: false);
            return destination;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    internal static async Task<NIRACapabilityHandlerResult> CreateAsync(
        NIRACapabilityRequest req, CancellationToken cancellationToken)
    {
        string path = GetPath(req, "path");
        string content = RequiredText(req, "content", 20000);
        string? title = NIRACapabilityArguments.GetOptionalRawString(req, "title", 160);
        if (title != null) XmlConvert.VerifyXmlChars(title);
        XDocument xml = NewDocument(content, title);
        cancellationToken.ThrowIfCancellationRequested();
        PublishNew(path, p => NewPackage(p, xml));
        // PublishNew committed a real new file; verify it before reporting success.
        var info = await NIRAFileIntegrity.HashAsync(path, CancellationToken.None)
            .ConfigureAwait(false);
        return new NIRACapabilityHandlerResult { ChangedSystemState = true,
            Summary = "New Word DOCX created and reopened successfully; existing files untouched.",
            Output = $"Path={path}\nBytes={info.Bytes}\nSHA256={info.Sha256}\nCreated=True\nOverwroteExisting=False" };
    }

    internal static async Task<NIRACapabilityHandlerResult> InspectAsync(
        NIRACapabilityRequest req, CancellationToken cancellationToken)
    {
        string path = GetPath(req, "path");
        RequireValidSource(path);
        using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using ZipArchive zip = new(file, ZipArchiveMode.Read);
        XDocument document = OpenDocument(zip);
        List<string> paragraphs = document.Descendants(W + "p")
            .Select(p => string.Concat(p.Descendants(W + "t").Select(t => t.Value)))
            .Where(text => !string.IsNullOrWhiteSpace(text)).ToList();
        int tables = document.Descendants(W + "tbl").Count();
        int headings = document.Descendants(W + "pStyle").Count(x =>
            ((string?)x.Attribute(W + "val"))?.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) == true);
        int images = zip.Entries.Count(x => x.FullName.StartsWith("word/media/", StringComparison.OrdinalIgnoreCase) && !x.FullName.EndsWith('/'));
        int words = paragraphs.Sum(x => Regex.Matches(x, @"\S+").Count);
        var fingerprint = await NIRAFileIntegrity.HashAsync(path, cancellationToken)
            .ConfigureAwait(false);
        string preview = string.Join("\n", paragraphs.Take(15).Select((x, i) =>
            $"{i + 1}: {(x.Length > 240 ? x[..240] + "…" : x)}"));
        // R42D: surface exactly observed table headers; strict headerTsv matching
        // must not require the model to reconstruct separators from paragraph text.
        // Bound table count, cell count and text size. This is read-only evidence,
        // not an instruction and not proof of visual appearance.
        StringBuilder headerEvidence = new();
        int tableNumber = 0;
        foreach (XElement table in document.Descendants(W + "tbl").Take(5))
        {
            tableNumber++;
            XElement? header = table.Elements(W + "tr").FirstOrDefault();
            if (header == null) continue;
            XElement[] cells = header.Elements(W + "tc").ToArray();
            if (cells.Length is < 2 or > 10) continue;
            string[] contents = cells.Select(cell =>
                string.Concat(cell.Descendants(W + "t").Select(t => t.Value))).ToArray();
            if (contents.Any(text => string.IsNullOrWhiteSpace(text) ||
                text.Length > 160 || text.Contains('\t') ||
                text.Contains('\r') || text.Contains('\n')))
                continue;
            headerEvidence.Append("TableHeaderCellsJson[")
                .Append(tableNumber).Append("]=")
                .AppendLine(JsonSerializer.Serialize(contents));
        }
        // R42C: section metadata is obtained from the saved OpenXML, not inferred
        // from the operating system, model output, or visual layout.
        XElement[] sections = document.Descendants(W + "sectPr").ToArray();
        string pageEvidence = "SectionCount=" + sections.Length;
        if (sections.Length == 1)
        {
            XElement? size = sections[0].Element(W + "pgSz");
            XElement? margin = sections[0].Element(W + "pgMar");
            if (size != null &&
                int.TryParse((string?)size.Attribute(W + "w"), out int width) &&
                int.TryParse((string?)size.Attribute(W + "h"), out int height))
            {
                string paper = (Math.Min(width, height), Math.Max(width, height)) switch
                {
                    (11906, 16838) => "A4",
                    (12240, 15840) => "LETTER",
                    _ => "Other"
                };
                string orientation = width > height ? "landscape" : "portrait";
                pageEvidence += $"\nPaper={paper}\nOrientation={orientation}\nPageWidthTwips={width}\nPageHeightTwips={height}";
            }
            if (margin != null && new[] { "top", "right", "bottom", "left" }
                .All(edge => int.TryParse((string?)margin.Attribute(W + edge), out _)))
            {
                int top = int.Parse((string)margin.Attribute(W + "top")!);
                int right = int.Parse((string)margin.Attribute(W + "right")!);
                int bottom = int.Parse((string)margin.Attribute(W + "bottom")!);
                int left = int.Parse((string)margin.Attribute(W + "left")!);
                string ToMm(int twips) => (twips * 25.4 / 1440.0)
                    .ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                pageEvidence += $"\nMarginTopMm={ToMm(top)}\nMarginRightMm={ToMm(right)}" +
                    $"\nMarginBottomMm={ToMm(bottom)}\nMarginLeftMm={ToMm(left)}";
            }
        }
        return new NIRACapabilityHandlerResult { ChangedSystemState = false,
            Summary = "DOCX inspected read-only; verified file contents, section layout and bounded text preview returned.",
            Output = $"Path={path}\nSHA256={fingerprint.Sha256}\nBytes={fingerprint.Bytes}\nParagraphs={paragraphs.Count}\nHeadings={headings}\nTables={tables}\n{headerEvidence}{pageEvidence}\nEmbeddedMediaParts={images}\nWordsApprox={words}\nPreviewLimited=True\nPreview:\n{preview}" };
    }

    internal static async Task<NIRACapabilityHandlerResult> AppendAsync(
        NIRACapabilityRequest req, CancellationToken cancellationToken)
    {
        string source = GetPath(req, "source");
        string destination = GetPath(req, "destination");
        NIRAFileIntegrity.Different(source, destination);
        string content = RequiredText(req, "content", 16000);
        string expected = NIRACapabilityArguments.RequireString(req, "expectedSourceSha256", 64);
        if (expected.Length != 64 || expected.Any(ch => !Uri.IsHexDigit(ch)))
            throw new ArgumentException("expectedSourceSha256 must be an observed 64-character SHA-256 value.");
        RequireValidSource(source);
        var before = await NIRAFileIntegrity.HashAsync(source, cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(before.Sha256, expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("DOCX source changed since its approved SHA-256 observation. Nothing written.");
        XDocument updated;
        using (FileStream file = new(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (ZipArchive zip = new(file, ZipArchiveMode.Read))
        {
            updated = OpenDocument(zip);
            AddText(updated.Root!.Element(W + "body")!, content);
        }
        var check = await NIRAFileIntegrity.HashAsync(source, cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(check.Sha256, expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("DOCX source changed during inspection. Nothing written.");
        cancellationToken.ThrowIfCancellationRequested();
        PublishNew(destination, p => Repackage(source, p, updated));
        // Publication has committed: complete verification without cancellation.
        var afterSource = await NIRAFileIntegrity.HashAsync(source, CancellationToken.None)
            .ConfigureAwait(false);
        var output = await NIRAFileIntegrity.HashAsync(destination, CancellationToken.None)
            .ConfigureAwait(false);
        if (!string.Equals(afterSource.Sha256, expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Source changed concurrently; original may have been edited externally. Review the new output.");
        return new NIRACapabilityHandlerResult { ChangedSystemState = true,
            Summary = "New DOCX copy created with appended paragraphs; original source preserved and reverified.",
            Output = $"Source={source}\nSourceSHA256={before.Sha256}\nDestination={destination}\nDestinationSHA256={output.Sha256}\nDestinationBytes={output.Bytes}\nSourceUnchanged=True\nOverwroteExisting=False" };
    }
// R42A: single-target, copy-only document edits. Keep authorization,
// package hardening, atomic new-file publishing, and source verification
// identical to the accepted R41 copy-on-write append contract.
internal static async Task<NIRACapabilityHandlerResult> StructuredEditAsync(
    NIRACapabilityRequest req, bool addTable, CancellationToken cancellationToken)
{
    string source = GetPath(req, "source");
    string destination = GetPath(req, "destination");
    NIRAFileIntegrity.Different(source, destination);
    string expected = NIRACapabilityArguments.RequireString(req, "expectedSourceSha256", 64);
    if (expected.Length != 64 || expected.Any(ch => !Uri.IsHexDigit(ch)))
        throw new ArgumentException("expectedSourceSha256 must be a freshly observed SHA-256.");
    RequireValidSource(source);
    NIRAFileIntegrity.RejectLinkedPaths(destination);
    NIRAFileIntegrity.RequireNewDestination(destination);
    var before = await NIRAFileIntegrity.HashAsync(source, cancellationToken).ConfigureAwait(false);
    if (!string.Equals(before.Sha256, expected, StringComparison.OrdinalIgnoreCase))
        throw new IOException("DOCX source has changed since the observed SHA-256. Nothing written.");

    XDocument updated;
    int count = 0;
    string detail;
    using (FileStream file = new(source, FileMode.Open, FileAccess.Read, FileShare.Read))
    using (ZipArchive zip = new(file, ZipArchiveMode.Read))
    {
        updated = OpenDocument(zip);
        XElement body = updated.Root!.Element(W + "body")!;
        if (addTable)
        {
            string tsv = RequiredText(req, "tsv", 12000);
            string[] lines = tsv.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n').Split('\n');
            if (lines.Length < 2 || lines.Length > 30)
                throw new ArgumentException("Table needs a header and 1-29 data rows.");
            string[][] cells = lines.Select(line => line.Split('\t')).ToArray();
            int columns = cells[0].Length;
            if (columns < 2 || columns > 10 || cells.Any(row => row.Length != columns ||
                row.Any(cell => string.IsNullOrWhiteSpace(cell) || cell.Length > 800)))
                throw new ArgumentException("Table needs 2-10 uniformly sized columns, nonempty cells (max 800 chars).");
            XElement table = new(W + "tbl",
                new XElement(W + "tblPr",
                    new XElement(W + "tblW", new XAttribute(W + "w", "5000"), new XAttribute(W + "type", "pct")),
                    new XElement(W + "tblBorders", new[] {"top", "left", "bottom", "right", "insideH", "insideV"}
                        .Select(edge => new XElement(W + edge, new XAttribute(W + "val", "single"),
                            new XAttribute(W + "sz", "6"), new XAttribute(W + "color", "CEDCEB"))))),
                new XElement(W + "tblGrid", Enumerable.Range(0, columns)
                    .Select(_ => new XElement(W + "gridCol", new XAttribute(W + "w", (9000 / columns).ToString())))));
            for (int r = 0; r < cells.Length; r++)
            {
                XElement tr = new(W + "tr");
                if (r == 0) tr.Add(new XElement(W + "trPr", new XElement(W + "tblHeader", new XAttribute(W + "val", "true"))));
                foreach (string cell in cells[r])
                {
                    XElement run = new(W + "r");
                    if (r == 0) run.Add(new XElement(W + "rPr", new XElement(W + "b")));
                    run.Add(new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), cell));
                    tr.Add(new XElement(W + "tc", new XElement(W + "p", run)));
                }
                table.Add(tr);
            }
            XElement? section = body.Elements(W + "sectPr").LastOrDefault();
            if (section is null) body.Add(table); else section.AddBeforeSelf(table);
            count = lines.Length;
            detail = $"TableRows={count}\nTableColumns={columns}";
        }
        else
        {
            string oldText = RequiredText(req, "oldText", 2000);
            string newText = RequiredText(req, "newText", 4000);
            if (newText.Contains('\n') || newText.Contains('\r') || oldText.Contains('\n') || oldText.Contains('\r'))
                throw new ArgumentException("Replace one paragraph at a time; newlines require the append capability.");
            var matches = body.Elements(W + "p").Where(p =>
                string.Equals(string.Concat(p.Descendants(W + "t").Select(x => x.Value)),
                    oldText, StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException($"Exact paragraph match must be unique (found {matches.Length}). Nothing written.");
            XElement paragraph = matches[0];
            XElement[] runs = paragraph.Elements(W + "r").ToArray();
            // Reject inline media, fields, bookmarks, hyperlinks, split runs,
            // tracked changes, special breaks and other complex structures.
            if (paragraph.Elements().Any(x => x.Name != W + "pPr" && x.Name != W + "r") ||
                runs.Length != 1 || runs[0].Elements(W + "t").Count() != 1 ||
                runs[0].Elements().Any(x => x.Name != W + "rPr" && x.Name != W + "t"))
                throw new InvalidOperationException("Target paragraph has complex formatting/content. Safe exact replacement is not supported.");
            runs[0].Element(W + "t")!.Value = newText;
            detail = "ExactParagraphMatches=1";
        }
    }
    var check = await NIRAFileIntegrity.HashAsync(source, cancellationToken).ConfigureAwait(false);
    if (!string.Equals(check.Sha256, expected, StringComparison.OrdinalIgnoreCase))
        throw new IOException("DOCX source changed during preparation. Nothing written.");
    cancellationToken.ThrowIfCancellationRequested();
    PublishNew(destination, p => Repackage(source, p, updated));
    var afterSource = await NIRAFileIntegrity.HashAsync(source, CancellationToken.None).ConfigureAwait(false);
    var output = await NIRAFileIntegrity.HashAsync(destination, CancellationToken.None).ConfigureAwait(false);
    if (!string.Equals(afterSource.Sha256, expected, StringComparison.OrdinalIgnoreCase))
        throw new IOException("Source was changed by another process; the NEW output needs manual review.");
    return new NIRACapabilityHandlerResult
    {
        ChangedSystemState = true,
        Summary = addTable ? "Table added to a new Word document copy, source unchanged." :
            "One exact paragraph replaced in a new Word document copy, source unchanged.",
        Output = $"Source={source}\nSourceSHA256={before.Sha256}\nDestination={destination}\nDestinationSHA256={output.Sha256}\nDestinationBytes={output.Bytes}\n{detail}\nSourceUnchanged=True\nOverwroteExisting=False"
    };
}

    // R42B: format exactly one plain paragraph in a NEW Word document.
    // Never alter source XML or source package; reject unsafe, ambiguous targets.
    internal static Task<NIRACapabilityHandlerResult> FormatParagraphAsync(
        NIRACapabilityRequest request, CancellationToken token) =>
        FormatCopyAsync(request, pageLayout: false, token);

    // R42B: set a single-section document's paper dimensions and margins in a NEW copy.
    internal static Task<NIRACapabilityHandlerResult> PageLayoutAsync(
        NIRACapabilityRequest request, CancellationToken token) =>
        FormatCopyAsync(request, pageLayout: true, token);

    private static void PutChild(XElement parent, string tag, string? value = null)
    {
        XElement? existing = parent.Element(W + tag);
        if (existing is null)
        {
            existing = new XElement(W + tag);
            parent.Add(existing);
        }
        if (value is not null) existing.SetAttributeValue(W + "val", value);
    }

    // Independent post-write validation: reopen the newly published DOCX and
    // compare its parsed OpenXML tree with the in-memory intended result.
    // This happens INSIDE the successful write receipt, so a model cannot lose
    // verification simply by attempting an inspection before output exists.
    private static string VerifyPublishedDocument(string destination, XDocument intended)
    {
        using FileStream file = new(destination, FileMode.Open, FileAccess.Read, FileShare.Read);
        using ZipArchive zip = new(file, ZipArchiveMode.Read);
        XDocument stored = OpenDocument(zip);
        if (!XNode.DeepEquals(intended.Root, stored.Root))
            throw new InvalidDataException("Reopened DOCX does not match the intended document XML.");
        XElement body = stored.Root!.Element(W + "body")!;
        int paragraphs = body.Descendants(W + "p").Count(p =>
            !string.IsNullOrWhiteSpace(string.Concat(p.Descendants(W + "t").Select(t => t.Value))));
        int headings = body.Descendants(W + "pStyle").Count(p =>
            ((string?)p.Attribute(W + "val"))?.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) == true);
        int tables = body.Descendants(W + "tbl").Count();
        return $"PostWriteVerified=True\nVerifiedParagraphs={paragraphs}\nVerifiedHeadings={headings}\nVerifiedTables={tables}";
    }

    private static async Task<NIRACapabilityHandlerResult> FormatCopyAsync(
        NIRACapabilityRequest request, bool pageLayout, CancellationToken token)
    {
        string source = GetPath(request, "source");
        string destination = GetPath(request, "destination");
        NIRAFileIntegrity.Different(source, destination);
        string expected = NIRACapabilityArguments.RequireString(request, "expectedSourceSha256", 64);
        if (expected.Length != 64 || expected.Any(ch => !Uri.IsHexDigit(ch)))
            throw new ArgumentException("expectedSourceSha256 must be the freshly observed SHA-256.");
        RequireValidSource(source);
        NIRAFileIntegrity.RejectLinkedPaths(destination);
        NIRAFileIntegrity.RequireNewDestination(destination);
        var before = await NIRAFileIntegrity.HashAsync(source, token).ConfigureAwait(false);
        if (!string.Equals(before.Sha256, expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("DOCX source SHA-256 is stale. Nothing written.");
        XDocument document;
        string detail;
        using (FileStream input = new(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (ZipArchive archive = new(input, ZipArchiveMode.Read))
        {
            document = OpenDocument(archive);
            XElement body = document.Root!.Element(W + "body")!;
            if (pageLayout)
            {
                string paper = NIRACapabilityArguments.RequireString(request, "paper", 16).ToUpperInvariant();
                string orientation = NIRACapabilityArguments.RequireString(request, "orientation", 16).ToLowerInvariant();
                string marginText = NIRACapabilityArguments.RequireString(request, "marginMm", 4);
                if (paper is not ("A4" or "LETTER") || orientation is not ("portrait" or "landscape") ||
                    !int.TryParse(marginText, out int margin) || margin is < 10 or > 35)
                    throw new ArgumentException("Use A4/LETTER, portrait/landscape, and marginMm 10-35.");
                // Multiple sections require section-aware layout management, not a silent global edit.
                XElement[] sections = body.Descendants(W + "sectPr").ToArray();
                if (sections.Length != 1 || sections[0].Parent != body)
                    throw new InvalidOperationException("Page layout requires exactly one final body section.");
                XElement sect = sections[0];
                XElement size = sect.Element(W + "pgSz") ?? new XElement(W + "pgSz");
                if (size.Parent is null) sect.AddFirst(size);
                int width = paper == "A4" ? 11906 : 12240;
                int height = paper == "A4" ? 16838 : 15840;
                if (orientation == "landscape") (width, height) = (height, width);
                size.SetAttributeValue(W + "w", width);
                size.SetAttributeValue(W + "h", height);
                size.SetAttributeValue(W + "orient", orientation);
                XElement margins = sect.Element(W + "pgMar") ?? new XElement(W + "pgMar");
                if (margins.Parent is null) size.AddAfterSelf(margins);
                int twips = (int)Math.Round(margin * 1440.0 / 25.4);
                foreach (string edge in new[] { "top", "right", "bottom", "left" })
                    margins.SetAttributeValue(W + edge, twips);
                detail = $"Paper={paper}\nOrientation={orientation}\nMarginMm={margin}";
            }
            else
            {
                string text = RequiredText(request, "exactText", 2000);
                string alignment = NIRACapabilityArguments.RequireString(request, "alignment", 12).ToLowerInvariant();
                string sizeText = NIRACapabilityArguments.RequireString(request, "fontSizePt", 3);
                string boldText = NIRACapabilityArguments.RequireString(request, "bold", 5).ToLowerInvariant();
                if (alignment is not ("left" or "center" or "right" or "justify") ||
                    !int.TryParse(sizeText, out int fontSize) || fontSize is < 8 or > 36 ||
                    boldText is not ("true" or "false"))
                    throw new ArgumentException("Use alignment left/center/right/justify, fontSizePt 8-36, bold true/false.");
                XElement[] matches = body.Elements(W + "p").Where(p =>
                    string.Equals(string.Concat(p.Descendants(W + "t").Select(t => t.Value)),
                        text, StringComparison.Ordinal)).ToArray();
                if (matches.Length != 1)
                    throw new InvalidOperationException($"Exact paragraph must match once (found {matches.Length}). Nothing written.");
                XElement paragraph = matches[0];
                XElement[] runs = paragraph.Elements(W + "r").ToArray();
                if (paragraph.Elements().Any(e => e.Name != W + "pPr" && e.Name != W + "r") ||
                    runs.Length != 1 || runs[0].Elements(W + "t").Count() != 1 ||
                    runs[0].Elements().Any(e => e.Name != W + "rPr" && e.Name != W + "t"))
                    throw new InvalidOperationException("Complex paragraph or split runs; refusing format edit.");
                XElement pPr = paragraph.Element(W + "pPr") ?? new XElement(W + "pPr");
                if (pPr.Parent is null) paragraph.AddFirst(pPr);
                // Preserve any existing heading style and other paragraph properties.
                XElement jc = pPr.Element(W + "jc") ?? new XElement(W + "jc");
                if (jc.Parent is null) pPr.Add(jc);
                jc.SetAttributeValue(W + "val", alignment == "justify" ? "both" : alignment);
                XElement rPr = runs[0].Element(W + "rPr") ?? new XElement(W + "rPr");
                if (rPr.Parent is null) runs[0].AddFirst(rPr);
                PutChild(rPr, "b", boldText == "true" ? "1" : "0");
                PutChild(rPr, "sz", (fontSize * 2).ToString());
                detail = $"ExactParagraphMatches=1\nAlignment={alignment}\nFontSizePt={fontSize}\nBold={boldText}";
            }
        }
        var check = await NIRAFileIntegrity.HashAsync(source, token).ConfigureAwait(false);
        if (!string.Equals(check.Sha256, expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("DOCX source changed during formatting. Nothing written.");
        token.ThrowIfCancellationRequested();
        PublishNew(destination, path => Repackage(source, path, document), document);
        string verified = VerifyPublishedDocument(destination, document);
        var after = await NIRAFileIntegrity.HashAsync(source, CancellationToken.None).ConfigureAwait(false);
        var output = await NIRAFileIntegrity.HashAsync(destination, CancellationToken.None).ConfigureAwait(false);
        if (!string.Equals(after.Sha256, expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Source was concurrently changed; inspect the created copy manually.");
        return new NIRACapabilityHandlerResult
        {
            ChangedSystemState = true,
            Summary = pageLayout ? "Single-section Word page layout updated in a new document copy." :
                "Exact Word paragraph formatted in a new document copy.",
            Output = $"Source={source}\nSourceSHA256={before.Sha256}\nDestination={destination}\nDestinationSHA256={output.Sha256}\nDestinationBytes={output.Bytes}\n{detail}\n{verified}\nSourceUnchanged=True\nOverwroteExisting=False"
        };
    }

    // R42C: copy-on-write formatting for a uniquely identified ordinary table.
    // Content and column positions remain unchanged; only table appearance is edited.
    internal static async Task<NIRACapabilityHandlerResult> TableStyleAsync(
        NIRACapabilityRequest request, CancellationToken token)
    {
        string source = GetPath(request, "source");
        string destination = GetPath(request, "destination");
        NIRAFileIntegrity.Different(source, destination);
        string expected = NIRACapabilityArguments.RequireString(request, "expectedSourceSha256", 64);
        if (expected.Length != 64 || expected.Any(ch => !Uri.IsHexDigit(ch)))
            throw new ArgumentException("expectedSourceSha256 must be an observed SHA-256.");
        string headerTsv = RequiredText(request, "headerTsv", 1000);
        string[] headers = headerTsv.Split('\t');
        if (headers.Length is < 2 or > 10 || headers.Any(h => h.Length == 0 || h.Contains('\n') || h.Contains('\r')))
            throw new ArgumentException("Provide 2-10 complete, exact header cells separated by tabs.");
        string headerColor = NIRACapabilityArguments.RequireString(request, "headerFillHex", 6).ToUpperInvariant();
        string stripeColor = NIRACapabilityArguments.RequireString(request, "stripeFillHex", 6).ToUpperInvariant();
        if (!Regex.IsMatch(headerColor, "^[0-9A-F]{6}$") ||
            !Regex.IsMatch(stripeColor, "^[0-9A-F]{6}$"))
            throw new ArgumentException("Colors must be exactly six RGB hexadecimal characters, without #.");
        RequireValidSource(source);
        NIRAFileIntegrity.RejectLinkedPaths(destination);
        NIRAFileIntegrity.RequireNewDestination(destination);
        var before = await NIRAFileIntegrity.HashAsync(source, token).ConfigureAwait(false);
        if (!string.Equals(before.Sha256, expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Source has changed since SHA-256 observation. Nothing written.");
        XDocument updated;
        int rowCount;
        using (FileStream input = new(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (ZipArchive zip = new(input, ZipArchiveMode.Read))
        {
            updated = OpenDocument(zip);
            XElement body = updated.Root!.Element(W + "body")!;
            var matching = body.Elements(W + "tbl").Where(tbl =>
            {
                XElement? first = tbl.Elements(W + "tr").FirstOrDefault();
                if (first is null) return false;
                string[] cells = first.Elements(W + "tc").Select(c =>
                    string.Concat(c.Descendants(W + "t").Select(t => t.Value))).ToArray();
                return cells.SequenceEqual(headers, StringComparer.Ordinal);
            }).ToArray();
            if (matching.Length != 1)
                throw new InvalidOperationException($"Header must match exactly one top-level table (found {matching.Length}). Nothing written.");
            XElement table = matching[0];
            XElement[] rows = table.Elements(W + "tr").ToArray();
            if (rows.Length < 2 || rows.Length > 200 || rows.Any(row =>
                row.Elements(W + "tc").Count() != headers.Length ||
                row.Descendants(W + "gridSpan").Any() || row.Descendants(W + "vMerge").Any() ||
                row.Descendants(W + "tbl").Any() ||
                row.Elements(W + "tc").Any(cell => cell.Elements(W + "p").Count() != 1 ||
                    cell.Elements().Any(x => x.Name != W + "p" && x.Name != W + "tcPr"))))
                throw new InvalidOperationException("Unsupported merged, nested, or complex table; nothing written.");
            for (int r = 0; r < rows.Length; r++)
            {
                foreach (XElement cell in rows[r].Elements(W + "tc"))
                {
                    XElement properties = cell.Element(W + "tcPr") ?? new XElement(W + "tcPr");
                    if (properties.Parent is null) cell.AddFirst(properties);
                    XElement shading = properties.Element(W + "shd") ?? new XElement(W + "shd");
                    if (shading.Parent is null) properties.Add(shading);
                    shading.SetAttributeValue(W + "val", "clear");
                    shading.SetAttributeValue(W + "fill", r == 0 ? headerColor : r % 2 == 0 ? stripeColor : "FFFFFF");
                    if (r == 0)
                    {
                        foreach (XElement run in cell.Descendants(W + "r"))
                        {
                            XElement rPr = run.Element(W + "rPr") ?? new XElement(W + "rPr");
                            if (rPr.Parent is null) run.AddFirst(rPr);
                            PutChild(rPr, "b", "1");
                            PutChild(rPr, "color", "FFFFFF");
                        }
                    }
                }
            }
            rowCount = rows.Length;
        }
        var recheck = await NIRAFileIntegrity.HashAsync(source, token).ConfigureAwait(false);
        if (!string.Equals(recheck.Sha256, expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Source changed during table preparation. Nothing written.");
        token.ThrowIfCancellationRequested();
        PublishNew(destination, path => Repackage(source, path, updated), updated);
        string verified = VerifyPublishedDocument(destination, updated);
        var after = await NIRAFileIntegrity.HashAsync(source, CancellationToken.None).ConfigureAwait(false);
        var output = await NIRAFileIntegrity.HashAsync(destination, CancellationToken.None).ConfigureAwait(false);
        if (!string.Equals(after.Sha256, expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Source was changed concurrently; inspect new output manually.");
        return new NIRACapabilityHandlerResult
        {
            ChangedSystemState = true,
            Summary = "Selected Word table styled in a verified NEW DOCX; original preserved.",
            Output = $"Source={source}\nSourceSHA256={before.Sha256}\nDestination={destination}\nDestinationSHA256={output.Sha256}\nDestinationBytes={output.Bytes}\nTableHeaderMatches=1\nStyledRows={rowCount}\nHeaderFillHex={headerColor}\nStripeFillHex={stripeColor}\n{verified}\nSourceUnchanged=True\nOverwroteExisting=False"
        };
    }

}

public sealed class NIRADocxCreateCapabilityHandler : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor { get; } = new()
    {
        Id = NIRACapabilityIds.DocxCreate, DefaultRisk = NIRACapabilityRisk.Modify,
        Description = "Create a genuine new .docx Word document (never overwrite), using plain paragraphs and # / ## Markdown heading lines. Optional title; maximum 20,000 characters and 300 paragraphs. Does not require Word.",
        Parameters = [NIRADocx.Param("path", true, "New absolute .docx file path in an existing folder; never overwritten."),
            NIRADocx.Param("content", true, "Document paragraphs as plain text; # Heading and ## Subheading lines supported."),
            NIRADocx.Param("title", false, "Optional first-page heading; maximum 160 characters.")]
    };
    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest request) => NIRACapabilityRisk.Modify;
    public Task<NIRACapabilityHandlerResult> ExecuteAsync(NIRACapabilityRequest request, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); return NIRADocx.CreateAsync(request, cancellationToken); }
}

public sealed class NIRADocxInspectCapabilityHandler : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor { get; } = new()
    {
        Id = NIRACapabilityIds.DocxInspect, DefaultRisk = NIRACapabilityRisk.Observe,
        Description = "Read-only inspection of a real .docx Word document: SHA-256, paragraphs, headings, tables, media counts and bounded text preview. Does not interpret images/tables or change documents.",
        Parameters = [NIRADocx.Param("path", true, "Existing .docx file to inspect.")]
    };
    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest request) => NIRACapabilityRisk.Observe;
    public Task<NIRACapabilityHandlerResult> ExecuteAsync(NIRACapabilityRequest request, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); return NIRADocx.InspectAsync(request, cancellationToken); }
}

public sealed class NIRADocxAppendCapabilityHandler : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor { get; } = new()
    {
        Id = NIRACapabilityIds.DocxAppend, DefaultRisk = NIRACapabilityRisk.Modify,
        Description = "Append plain paragraphs or # / ## heading lines to an EXISTING .docx but publish only a NEW output document; source stays intact. Requires a freshly observed SHA-256; rejects encrypted, signed, macro-bearing, tracked-edit, or unsupported packages. No Word needed.",
        Parameters = [NIRADocx.Param("source", true, "Existing .docx input file (never modified)."),
            NIRADocx.Param("destination", true, "New .docx output path in an existing authorized folder (never overwritten)."),
            NIRADocx.Param("content", true, "New text/paragraphs to append after the existing body."),
            NIRADocx.Param("expectedSourceSha256", true, "Fresh SHA-256 of original .docx from filesystem.hash; must match.")]
    };
    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest request) => NIRACapabilityRisk.Modify;
    public Task<NIRACapabilityHandlerResult> ExecuteAsync(NIRACapabilityRequest request, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); return NIRADocx.AppendAsync(request, cancellationToken); }
}

// R42A: copy-only exact paragraph replacement.
public sealed class NIRADocxReplaceCapabilityHandler : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor { get; } = new()
    {
        Id = NIRACapabilityIds.DocxReplace, DefaultRisk = NIRACapabilityRisk.Modify,
        Description = "Replace exactly one unique, plain-text paragraph in a new DOCX copy; preserves the original and existing paragraph style. Rejects ambiguous or complex paragraphs. Requires source SHA-256 and new destination. No Word needed.",
        Parameters = [
            NIRADocx.Param("source", true, "Existing DOCX source path; never modified."),
            NIRADocx.Param("destination", true, "NEW nonexisting .docx output path."),
            NIRADocx.Param("oldText", true, "Exact complete text of the unique original paragraph, including punctuation."),
            NIRADocx.Param("newText", true, "One replacement paragraph, no newline (max 4000 chars)."),
            NIRADocx.Param("expectedSourceSha256", true, "Fresh exact SHA-256 from filesystem.hash or docx.inspect.")]
    };
    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest request) => NIRACapabilityRisk.Modify;
    public Task<NIRACapabilityHandlerResult> ExecuteAsync(NIRACapabilityRequest request, CancellationToken cancellationToken = default) =>
        NIRADocx.StructuredEditAsync(request, false, cancellationToken);
}

// R42A: add a bounded, real OpenXML table into a NEW output DOCX copy.
public sealed class NIRADocxTableAddCapabilityHandler : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor { get; } = new()
    {
        Id = NIRACapabilityIds.DocxTableAdd, DefaultRisk = NIRACapabilityRisk.Modify,
        Description = "Add a real styled Word table (header bold, borders) to a new DOCX copy from TSV: 2-10 uniform columns, header plus 1-29 data rows, nonempty cells. Original unchanged, requires SHA-256. No Word needed.",
        Parameters = [
            NIRADocx.Param("source", true, "Existing DOCX source path; never modified."),
            NIRADocx.Param("destination", true, "NEW nonexisting .docx output path."),
            NIRADocx.Param("tsv", true, "Tab-separated data: first row headings, subsequent rows values (\t between cells, \n between rows)."),
            NIRADocx.Param("expectedSourceSha256", true, "Fresh SHA-256 of source document.")]
    };
    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest request) => NIRACapabilityRisk.Modify;
    public Task<NIRACapabilityHandlerResult> ExecuteAsync(NIRACapabilityRequest request, CancellationToken cancellationToken = default) =>
        NIRADocx.StructuredEditAsync(request, true, cancellationToken);
}


// R42B: native page/paragraph formatting in verified, never-overwritten copies.
public sealed class NIRADocxParagraphFormatCapabilityHandler : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor { get; } = new()
    {
        Id = NIRACapabilityIds.DocxParagraphFormat, DefaultRisk = NIRACapabilityRisk.Modify,
        Description = "Format one unique simple paragraph in a NEW DOCX copy: alignment, font size (8-36pt) and bold. Existing heading style retained. Requires fresh SHA-256; no overwrites; complex runs rejected.",
        Parameters = [
            NIRADocx.Param("source", true, "Existing source DOCX; never modified."),
            NIRADocx.Param("destination", true, "NEW .docx path; never overwritten."),
            NIRADocx.Param("exactText", true, "Exact unique plain paragraph text, including punctuation."),
            NIRADocx.Param("alignment", true, "left, center, right or justify."),
            NIRADocx.Param("fontSizePt", true, "Integer point size from 8 through 36."),
            NIRADocx.Param("bold", true, "true or false."),
            NIRADocx.Param("expectedSourceSha256", true, "Fresh SHA-256 of source.")]
    };
    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest request) => NIRACapabilityRisk.Modify;
    public Task<NIRACapabilityHandlerResult> ExecuteAsync(NIRACapabilityRequest request, CancellationToken cancellationToken = default) =>
        NIRADocx.FormatParagraphAsync(request, cancellationToken);
}

public sealed class NIRADocxPageLayoutCapabilityHandler : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor { get; } = new()
    {
        Id = NIRACapabilityIds.DocxPageLayout, DefaultRisk = NIRACapabilityRisk.Modify,
        Description = "Set A4/Letter, portrait/landscape and uniform 10-35mm margins in a NEW copy of a single-section DOCX. Never overwrites source; requires fresh SHA-256.",
        Parameters = [
            NIRADocx.Param("source", true, "Existing source DOCX; never modified."),
            NIRADocx.Param("destination", true, "NEW .docx path; never overwritten."),
            NIRADocx.Param("paper", true, "A4 or LETTER."),
            NIRADocx.Param("orientation", true, "portrait or landscape."),
            NIRADocx.Param("marginMm", true, "Uniform margins in integer mm (10 through 35)."),
            NIRADocx.Param("expectedSourceSha256", true, "Fresh SHA-256 of source.")]
    };
    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest request) => NIRACapabilityRisk.Modify;
    public Task<NIRACapabilityHandlerResult> ExecuteAsync(NIRACapabilityRequest request, CancellationToken cancellationToken = default) =>
        NIRADocx.PageLayoutAsync(request, cancellationToken);
}

// R42C: style an existing, unambiguous table without altering content.
public sealed class NIRADocxTableStyleCapabilityHandler : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor { get; } = new()
    {
        Id = NIRACapabilityIds.DocxTableStyle, DefaultRisk = NIRACapabilityRisk.Modify,
        Description = "Style an exactly identified simple Word table in a NEW DOCX copy: colored bold white-text header and alternating row fills. Preserves data, table borders, and original; requires fresh SHA-256. Rejects merged/complex tables.",
        Parameters = [
            NIRADocx.Param("source", true, "Existing .docx to read only."),
            NIRADocx.Param("destination", true, "NEW nonexisting .docx path."),
            NIRADocx.Param("headerTsv", true, "Exact first-row text of the unique table, cells separated by tabs."),
            NIRADocx.Param("headerFillHex", true, "Six-digit RGB hex header fill, no #; e.g. 17365D."),
            NIRADocx.Param("stripeFillHex", true, "Six-digit RGB hex alternating row fill, no #; e.g. EAF2F8."),
            NIRADocx.Param("expectedSourceSha256", true, "Fresh SHA-256 of source document.")]
    };
    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest request) => NIRACapabilityRisk.Modify;
    public Task<NIRACapabilityHandlerResult> ExecuteAsync(NIRACapabilityRequest request, CancellationToken cancellationToken = default) =>
        NIRADocx.TableStyleAsync(request, cancellationToken);
}
