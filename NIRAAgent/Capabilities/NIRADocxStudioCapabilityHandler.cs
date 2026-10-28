using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace NIRAAgent.Capabilities;

// A known create-only collision is a definite NO-EFFECT preflight result.
// Never describe it as a potentially partial document mutation.
internal sealed class NIRADocxStudioDestinationExistsException : IOException
{
    public NIRADocxStudioDestinationExistsException(string path)
        : base("R48_STUDIO_DESTINATION_EXISTS: The destination already exists: " + path +
            ". No file was modified. Inspect/hash the existing file, or select a different NEW destination. " +
            "Do not retry filesystem.docx.studio.create against this path.") { }
}

// A rejected input with no published destination is a definite no-write result.
// Keep this distinct from unknown failures that occur during/after publication.
internal sealed class NIRADocxStudioNoPublishedFileException : ArgumentException
{
    public NIRADocxStudioNoPublishedFileException(string reason, Exception inner)
        : base("STUDIO_INPUT_REJECTED_NO_FILE: " + reason +
            " No document was published. Revise the actual invalid input; " +
            "the same unused destination may be used after correction.", inner) { }
}

// R48 Word Studio: ONE atomic, styled, native DOCX composition operation.
// Its inputs are explicitly bounded JSON, not executable XML or Word macros.
internal static partial class NIRADocx
{
    private static readonly XNamespace CT48 = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace PR48 = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string BaseRel48 = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";

    // R48.2: exact expected XML after the production serializer adds namespace
    // bindings (not a relaxed comparison and not a verifier bypass).
    private static XDocument SerializedExpected48(XDocument original)
    {
        using MemoryStream buffer = new();
        using (XmlWriter writer = XmlWriter.Create(buffer, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            OmitXmlDeclaration = false,
            CloseOutput = false
        }))
        {
            original.Save(writer);
        }
        buffer.Position = 0;
        return LoadXml(buffer);
    }
    private static string GetString48(JsonElement obj, string key, int max, bool required = true)
    {
        if (!obj.TryGetProperty(key, out var prop) || prop.ValueKind == JsonValueKind.Null)
        {
            if (required) throw new ArgumentException("Word Studio requires '" + key + "'.");
            return "";
        }
        if (prop.ValueKind != JsonValueKind.String) throw new ArgumentException(key + " must be a string.");
        string val = prop.GetString() ?? "";
        if (val.Length > max || (required && string.IsNullOrWhiteSpace(val)))
            throw new ArgumentException(key + " is missing or exceeds " + max + " characters.");
        XmlConvert.VerifyXmlChars(val);
        return val;
    }

    private static bool GetBool48(JsonElement obj, string key, bool fallback = false)
    {
        if (!obj.TryGetProperty(key, out var x)) return fallback;
        return x.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new ArgumentException(key + " must be a JSON boolean.")
        };
    }
    private static int GetInt48(JsonElement obj, string key, int fallback, int min, int max)
    {
        if (!obj.TryGetProperty(key, out var x)) return fallback;
        if (x.ValueKind != JsonValueKind.Number || !x.TryGetInt32(out int value) || value < min || value > max)
            throw new ArgumentException(key + " must be an integer between " + min + " and " + max + ".");
        return value;
    }
    private static string Color48(string value)
    {
        if (value.Length != 6 || value.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("accentHex must contain six hexadecimal RGB digits.");
        return value.ToUpperInvariant();
    }
    private static XElement TextRun48(string text, bool bold = false, bool italic = false,
        bool underline = false, string? color = null, int size = 0)
    {
        XElement r = new(W + "r");
        XElement props = new(W + "rPr");
        if (bold) props.Add(new XElement(W + "b"));
        if (italic) props.Add(new XElement(W + "i"));
        if (underline) props.Add(new XElement(W + "u", new XAttribute(W + "val", "single")));
        if (color != null) props.Add(new XElement(W + "color", new XAttribute(W + "val", color)));
        if (size != 0) props.Add(new XElement(W + "sz", new XAttribute(W + "val", size * 2)));
        if (props.HasElements) r.Add(props);
        r.Add(new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text));
        return r;
    }
    private static XElement P48(string alignment, int afterTwips = 120, string? style = null, int listId = 0)
    {
        if (alignment is not ("left" or "center" or "right" or "justify"))
            throw new ArgumentException("alignment must be left/center/right/justify.");
        XElement props = new(W + "pPr");
        if (style != null) props.Add(new XElement(W + "pStyle", new XAttribute(W + "val", style)));
        if (listId != 0) props.Add(new XElement(W + "numPr",
            new XElement(W + "ilvl", new XAttribute(W + "val", 0)),
            new XElement(W + "numId", new XAttribute(W + "val", listId))));
        if (style != null && style.StartsWith("Heading", StringComparison.Ordinal))
        {
            props.Add(new XElement(W + "keepNext"));
            props.Add(new XElement(W + "keepLines"));
        }
        props.Add(new XElement(W + "spacing", new XAttribute(W + "after", afterTwips),
            new XAttribute(W + "line", 285), new XAttribute(W + "lineRule", "auto")));
        props.Add(new XElement(W + "jc", new XAttribute(W + "val", alignment == "justify" ? "both" : alignment)));
        return new XElement(W + "p", props);
    }
    private static XElement BodyParagraph48(JsonElement data, string type, string accent)
    {
        int level = type == "heading" ? GetInt48(data, "level", 1, 1, 3) : 0;
        int fontSize = GetInt48(data, "fontSizePt", level == 1 ? 18 : level == 2 ? 14 : 11, 8, 36);
        string alignment = GetString48(data, "alignment", 12, false);
        if (alignment.Length == 0) alignment = "left";
        XElement p = P48(alignment, level > 0 ? 180 : 130, level > 0 ? "Heading" + level : null,
            type == "bullet" ? 1 : type == "number" ? 2 : 0);
        bool bold = type == "heading" || GetBool48(data, "bold");
        bool italic = GetBool48(data, "italic");
        string color = type == "heading" ? accent : "202B3A";
        if (data.TryGetProperty("runs", out var runs))
        {
            if (runs.ValueKind != JsonValueKind.Array || runs.GetArrayLength() is < 1 or > 20 ||
                data.TryGetProperty("text", out _))
                throw new ArgumentException("runs requires 1-20 entries and no text property.");
            foreach (var part in runs.EnumerateArray())
            {
                if (part.ValueKind != JsonValueKind.Object) throw new ArgumentException("runs elements must be objects.");
                string chunk = GetString48(part, "text", 2000);
                string? customColor = null;
                string colorText = GetString48(part, "colorHex", 6, false);
                if (colorText.Length != 0) customColor = Color48(colorText);
                p.Add(TextRun48(chunk, bold || GetBool48(part, "bold"), italic || GetBool48(part, "italic"),
                    GetBool48(part, "underline"), customColor ?? color, fontSize));
            }
        }
        else
        {
            string text = GetString48(data, "text", 4000);
            p.Add(TextRun48(text, bold, italic, GetBool48(data, "underline"), color, fontSize));
        }
        return p;
    }
    private static XElement Table48(JsonElement data, string accent, int availableTwips)
    {
        if (!data.TryGetProperty("rows", out var rows) || rows.ValueKind != JsonValueKind.Array ||
            rows.GetArrayLength() is < 2 or > 40)
            throw new ArgumentException("table rows require 2-40 rows.");
        var parsed = new List<List<string>>();
        int columns = 0;
        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() is < 2 or > 8)
                throw new ArgumentException("Every table row must contain 2-8 cells.");
            if (columns == 0) columns = row.GetArrayLength();
            if (columns != row.GetArrayLength()) throw new ArgumentException("Table rows must have matching column counts.");
            var cells = new List<string>();
            foreach (var cell in row.EnumerateArray())
            {
                if (cell.ValueKind != JsonValueKind.String || cell.GetString()!.Length > 500)
                    throw new ArgumentException("Table cell must be text up to 500 characters.");
                string text = cell.GetString()!;
                XmlConvert.VerifyXmlChars(text);
                cells.Add(text);
            }
            parsed.Add(cells);
        }
        // Match the editable page width instead of the old 1500-twip grid
        // that produced narrow tables on landscape documents. Allocate cells
        // proportionally to bounded observed contents, not named column titles.
        int[] weights = Enumerable.Range(0, columns)
            .Select(col => Math.Clamp(parsed.Max(row => row[col].Length), 8, 60)).ToArray();
        int totalWeight = weights.Sum();
        int minimum = availableTwips * (columns <= 4 ? 14 : 8) / 100;
        int remaining = availableTwips - minimum * columns;
        int[] widths = new int[columns];
        int assigned = 0;
        for (int col = 0; col < columns; col++)
        {
            widths[col] = col == columns - 1 ? availableTwips - assigned :
                minimum + (int)((long)remaining * weights[col] / totalWeight);
            assigned += widths[col];
        }
        XElement table = new(W + "tbl",
            new XElement(W + "tblPr", new XElement(W + "tblW",
                new XAttribute(W + "w", availableTwips), new XAttribute(W + "type", "dxa")),
                new XElement(W + "tblLayout", new XAttribute(W + "type", "fixed")),
                new XElement(W + "tblBorders", new XElement(W + "bottom",
                    new XAttribute(W + "val", "single"), new XAttribute(W + "sz", 4),
                    new XAttribute(W + "color", "CFD9E8")))),
            new XElement(W + "tblGrid", widths.Select(width =>
                new XElement(W + "gridCol", new XAttribute(W + "w", width)))));
        for (int i = 0; i < parsed.Count; i++)
        {
            XElement tr = new(W + "tr");
            tr.Add(new XElement(W + "trPr", new XElement(W + "cantSplit")));
            if (i == 0) tr.Element(W + "trPr")!.Add(new XElement(W + "tblHeader",
                new XAttribute(W + "val", 1)));
            for (int col = 0; col < columns; col++)
            {
                string text = parsed[i][col];
                XElement tc = new(W + "tc", new XElement(W + "tcPr",
                    new XElement(W + "tcW", new XAttribute(W + "w", widths[col]),
                        new XAttribute(W + "type", "dxa")),
                    new XElement(W + "shd", new XAttribute(W + "fill", i == 0 ? accent : i % 2 == 0 ? "F0F5FB" : "FFFFFF"))));
                XElement p = P48("left", 60);
                p.Add(TextRun48(text, i == 0, false, false, i == 0 ? "FFFFFF" : "202B3A", 10));
                tc.Add(p);
                tr.Add(tc);
            }
            table.Add(tr);
        }
        return table;
    }
    private static XDocument Numbering48()
    {
        XElement Abstract(int id, string format, string glyph) => new(W + "abstractNum",
            new XAttribute(W + "abstractNumId", id), new XElement(W + "multiLevelType", new XAttribute(W + "val", "singleLevel")),
            new XElement(W + "lvl", new XAttribute(W + "ilvl", 0),
                new XElement(W + "start", new XAttribute(W + "val", 1)),
                new XElement(W + "numFmt", new XAttribute(W + "val", format)),
                new XElement(W + "lvlText", new XAttribute(W + "val", glyph)),
                new XElement(W + "lvlJc", new XAttribute(W + "val", "left")),
                new XElement(W + "pPr", new XElement(W + "tabs", new XElement(W + "tab",
                    new XAttribute(W + "val", "num"), new XAttribute(W + "pos", 720))),
                    new XElement(W + "ind", new XAttribute(W + "left", 720), new XAttribute(W + "hanging", 360)))));
        return new XDocument(new XElement(W + "numbering", Abstract(0, "bullet", "•"), Abstract(1, "decimal", "%1."),
            new XElement(W + "num", new XAttribute(W + "numId", 1),
                new XElement(W + "abstractNumId", new XAttribute(W + "val", 0))),
            new XElement(W + "num", new XAttribute(W + "numId", 2),
                new XElement(W + "abstractNumId", new XAttribute(W + "val", 1)))));
    }
    private static XDocument Styles48(string accent, string profile)
    {
        string face = profile == "academic" ? "Cambria" : "Calibri";
        XElement Heading(int level, int size) => new(W + "style", new XAttribute(W + "type", "paragraph"),
            new XAttribute(W + "styleId", "Heading" + level),
            new XElement(W + "name", new XAttribute(W + "val", "heading " + level)),
            new XElement(W + "basedOn", new XAttribute(W + "val", "Normal")),
            new XElement(W + "pPr", new XElement(W + "keepNext")),
            new XElement(W + "rPr", new XElement(W + "b"),
                new XElement(W + "color", new XAttribute(W + "val", accent)),
                new XElement(W + "sz", new XAttribute(W + "val", size * 2))));
        return new XDocument(new XElement(W + "styles",
            new XElement(W + "style", new XAttribute(W + "type", "paragraph"),
                new XAttribute(W + "styleId", "Normal"), new XAttribute(W + "default", 1),
                new XElement(W + "name", new XAttribute(W + "val", "Normal")),
                new XElement(W + "rPr", new XElement(W + "rFonts", new XAttribute(W + "ascii", face),
                    new XAttribute(W + "hAnsi", face)), new XElement(W + "sz", new XAttribute(W + "val", 22)))),
            Heading(1, 18), Heading(2, 14), Heading(3, 12)));
    }
    private static void Package48(string path, XDocument doc, string accent, string profile,
        XDocument? header, XDocument? footer, IReadOnlyList<Studio48Media> media)
    {
        using FileStream file = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using (ZipArchive zip = new(file, ZipArchiveMode.Create, leaveOpen: true))
        {
            XElement types = new(CT48 + "Types",
                new XElement(CT48 + "Default", new XAttribute("Extension", "rels"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(CT48 + "Default", new XAttribute("Extension", "xml"),
                    new XAttribute("ContentType", "application/xml")));
            void Type(string part, string mime) => types.Add(new XElement(CT48 + "Override",
                new XAttribute("PartName", "/" + part), new XAttribute("ContentType", mime)));
            Type(DocumentPart, "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml");
            Type("word/styles.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml");
            Type("word/numbering.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml");
            if (header != null) Type("word/headerR48.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml");
            if (footer != null) Type("word/footerR48.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml");
            foreach (Studio48Media asset in media.GroupBy(m => m.Extension, StringComparer.OrdinalIgnoreCase)
                         .Select(group => group.First()))
                types.Add(new XElement(CT48 + "Default", new XAttribute("Extension", asset.Extension),
                    new XAttribute("ContentType", asset.Mime)));
            WriteXml(zip, ContentTypePart, new XDocument(types));
            WriteXml(zip, RelationshipPart, new XDocument(new XElement(PR48 + "Relationships",
                new XElement(PR48 + "Relationship", new XAttribute("Id", "rId1"),
                    new XAttribute("Type", BaseRel48 + "officeDocument"), new XAttribute("Target", "word/document.xml")))));
            XElement links = new(PR48 + "Relationships",
                new XElement(PR48 + "Relationship", new XAttribute("Id", "rId1"),
                    new XAttribute("Type", BaseRel48 + "styles"), new XAttribute("Target", "styles.xml")),
                new XElement(PR48 + "Relationship", new XAttribute("Id", "rId2"),
                    new XAttribute("Type", BaseRel48 + "numbering"), new XAttribute("Target", "numbering.xml")));
            if (header != null) links.Add(new XElement(PR48 + "Relationship", new XAttribute("Id", "rId3"),
                new XAttribute("Type", BaseRel48 + "header"), new XAttribute("Target", "headerR48.xml")));
            if (footer != null) links.Add(new XElement(PR48 + "Relationship", new XAttribute("Id", "rId4"),
                new XAttribute("Type", BaseRel48 + "footer"), new XAttribute("Target", "footerR48.xml")));
            foreach (Studio48Media asset in media)
                links.Add(new XElement(PR48 + "Relationship", new XAttribute("Id", asset.RelationshipId),
                    new XAttribute("Type", BaseRel48 + "image"), new XAttribute("Target", "media/" + asset.FileName)));
            WriteXml(zip, "word/_rels/document.xml.rels", new XDocument(links));
            WriteXml(zip, DocumentPart, doc);
            WriteXml(zip, "word/styles.xml", Styles48(accent, profile));
            WriteXml(zip, "word/numbering.xml", Numbering48());
            if (header != null) WriteXml(zip, "word/headerR48.xml", header);
            if (footer != null) WriteXml(zip, "word/footerR48.xml", footer);
            foreach (Studio48Media asset in media)
            {
                ZipArchiveEntry entry = zip.CreateEntry("word/media/" + asset.FileName, CompressionLevel.Optimal);
                using Stream dst = entry.Open();
                dst.Write(asset.Bytes);
            }
        }
        file.Flush(true);
    }
    internal static async Task<NIRACapabilityHandlerResult> Studio48Async(
        NIRACapabilityRequest request, CancellationToken ct)
    {
        // Parse/quality/media validation can fail before the create-only publish.
        // File absence is checked *after* failure; a published destination or an
        // unclassified I/O exception remains uncertain and must be inspected.
        string? intendedPath = null;
        try
        {
            intendedPath = GetPath(request, "path");
            return await Studio48CoreAsync(request, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException or XmlException or InvalidDataException)
        {
            if (intendedPath != null && !File.Exists(intendedPath) && !Directory.Exists(intendedPath))
                throw new NIRADocxStudioNoPublishedFileException(ex.Message, ex);
            throw;
        }
    }

    private static async Task<NIRACapabilityHandlerResult> Studio48CoreAsync(
        NIRACapabilityRequest request, CancellationToken ct)
    {
        string path = GetPath(request, "path");
        string title = RequiredText(request, "title", 180).Trim();
        string raw = RequiredText(request, "blocksJson", 180000);
        NIRAFileIntegrity.RejectLinkedPaths(path);
        if (File.Exists(path) || Directory.Exists(path))
            throw new NIRADocxStudioDestinationExistsException(path);
        NIRAFileIntegrity.RequireNewDestination(path);
        if (title.Contains('\n') || title.Contains('\r')) throw new ArgumentException("Title must be one line.");
        string paper = (NIRACapabilityArguments.GetOptionalRawString(request, "paper", 8) ?? "A4").ToUpperInvariant();
        string orient = (NIRACapabilityArguments.GetOptionalRawString(request, "orientation", 12) ?? "portrait").ToLowerInvariant();
        string mm = NIRACapabilityArguments.GetOptionalRawString(request, "marginMm", 2) ?? "20";
        string accent = Color48(NIRACapabilityArguments.GetOptionalRawString(request, "accentHex", 6) ?? "17365D");
        string profile = (NIRACapabilityArguments.GetOptionalRawString(request, "profile", 10) ?? "general")
            .ToLowerInvariant();
        if (profile is not ("general" or "academic" or "business"))
            throw new ArgumentException("profile must be general, academic or business.");
        string minWordsText = NIRACapabilityArguments.GetOptionalRawString(request, "minimumBodyWords", 5) ?? "0";
        if (!int.TryParse(minWordsText, out int minimumBodyWords) || minimumBodyWords is < 0 or > 12000)
            throw new ArgumentException("minimumBodyWords must be an integer between 0 and 12000.");
        string minImagesText = NIRACapabilityArguments.GetOptionalRawString(request, "minimumImages", 1) ?? "0";
        if (!int.TryParse(minImagesText, out int minimumImages) || minimumImages is < 0 or > 6)
            throw new ArgumentException("minimumImages must be an integer between 0 and 6.");
        string? headerText = CleanOptionalLine(request, "headerText");
        string? footerText = CleanOptionalLine(request, "footerText");
        string pageFlag = NIRACapabilityArguments.GetOptionalRawString(request, "pageNumbers", 5) ?? "false";
        if (paper is not ("A4" or "LETTER") || orient is not ("portrait" or "landscape") ||
            !int.TryParse(mm, out int margin) || margin is < 10 or > 35 ||
            pageFlag is not ("true" or "false"))
            throw new ArgumentException("paper A4/LETTER, orientation portrait/landscape, marginMm 10-35, pageNumbers true/false.");
        bool pages = pageFlag == "true";
        string? legacyImagePath = NIRACapabilityArguments.GetOptionalRawString(request, "imagePath", 32760);
        using JsonDocument parsed = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = 12 });
        if (parsed.RootElement.ValueKind != JsonValueKind.Array || parsed.RootElement.GetArrayLength() is < 1 or > 120)
            throw new ArgumentException("blocksJson requires 1-120 blocks.");
        List<Studio48Media> media = await LoadStudio48MediaAsync(parsed.RootElement,
            legacyImagePath, ct).ConfigureAwait(false);
        if (media.Count < minimumImages)
            throw new ArgumentException($"Document has {media.Count} verified local images, " +
                $"but minimumImages={minimumImages}. No document was created.");
        int bodyWords = CountStudio48BodyWords(parsed.RootElement);
        if (bodyWords < minimumBodyWords)
            throw new ArgumentException($"Document content is too short: {bodyWords} body words, " +
                $"minimumBodyWords={minimumBodyWords}. No document was created. Expand substantive writing first.");
        int pageWidth = paper == "A4" ? 11906 : 12240;
        int pageHeight = paper == "A4" ? 16838 : 15840;
        if (orient == "landscape") (pageWidth, pageHeight) = (pageHeight, pageWidth);
        int marginTwips = (int)Math.Round(margin * 1440.0 / 25.4);
        int tableWidth = pageWidth - 2 * marginTwips;
        if (tableWidth <= 0) throw new ArgumentException("Margins leave no usable table width.");
        XElement body = new(W + "body");
        XElement titleP = P48("center", 280);
        titleP.Element(W + "pPr")!.Add(new XElement(W + "keepNext"));
        titleP.Add(TextRun48(title, true, false, false, accent, profile == "academic" ? 20 : 24));
        body.Add(titleP);
        int count = 0, headings = 0, tables = 0, charts = 0, lists = 0, breaks = 0, links = 0, images = 0;
        foreach (JsonElement block in parsed.RootElement.EnumerateArray())
        {
            ct.ThrowIfCancellationRequested();
            if (block.ValueKind != JsonValueKind.Object) throw new ArgumentException("Word blocks must be JSON objects.");
            string type = GetString48(block, "type", 12).ToLowerInvariant();
            switch (type)
            {
                case "paragraph": case "heading": case "bullet": case "number":
                    body.Add(BodyParagraph48(block, type, accent));
                    if (type == "heading") headings++;
                    if (type is "bullet" or "number") lists++;
                    break;
                case "table":
                    body.Add(Table48(block, accent, tableWidth)); tables++; break;
                case "chart":
                    foreach (XElement element in ChartTable48(block, accent, tableWidth)) body.Add(element);
                    charts++; tables++; break;
                case "quote":
                    XElement quote = BodyParagraph48(block, "paragraph", accent);
                    XElement quoteProps = quote.Element(W + "pPr")!;
                    quoteProps.Element(W + "spacing")!.AddBeforeSelf(new XElement(W + "pBdr", new XElement(W + "left",
                        new XAttribute(W + "val", "single"), new XAttribute(W + "sz", 24),
                        new XAttribute(W + "space", 10), new XAttribute(W + "color", accent))));
                    quoteProps.Element(W + "jc")!.AddBeforeSelf(new XElement(W + "ind", new XAttribute(W + "left", 500),
                        new XAttribute(W + "right", 280)));
                    body.Add(quote); break;
                case "divider":
                    XElement divider = P48("left", 160);
                    divider.Element(W + "pPr")!.Element(W + "spacing")!.AddBeforeSelf(new XElement(W + "pBdr", new XElement(W + "bottom",
                        new XAttribute(W + "val", "single"), new XAttribute(W + "sz", 9),
                        new XAttribute(W + "color", accent))));
                    divider.Add(TextRun48(" "));
                    body.Add(divider); break;
                case "toc":
                    XElement toc = P48("left", 160);
                    toc.Add(new XElement(W + "fldSimple", new XAttribute(W + "instr", " TOC \\o \"1-3\" \\h \\z \\u "),
                        TextRun48("Update the table of contents in Word", false, true, false, "617086", 10)));
                    body.Add(toc); break;
                case "pagebreak":
                    body.Add(new XElement(W + "p", new XElement(W + "r",
                        new XElement(W + "br", new XAttribute(W + "type", "page"))))); breaks++; break;
                case "link":
                    string url = GetString48(block, "url", 1000);
                    if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
                        uri.Scheme is not ("https" or "http") || !string.IsNullOrWhiteSpace(uri.UserInfo))
                        throw new ArgumentException("Link URL must be an absolute http/https URL without embedded credentials.");
                    // Word field-based hyperlink avoids external ZIP relationships.
                    XElement linkP = P48("left");
                    linkP.Add(new XElement(W + "fldSimple", new XAttribute(W + "instr",
                        " HYPERLINK \"" + uri.AbsoluteUri.Replace("\"", "%22", StringComparison.Ordinal) + "\" "),
                        TextRun48(GetString48(block, "text", 300), false, false, true, "1769AA", 11)));
                    body.Add(linkP); links++; break;
                case "image":
                    if (images >= media.Count) throw new ArgumentException("Image block has no verified local imagePath.");
                    Studio48Media asset = media[images];
                    string alt = GetString48(block, "altText", 200);
                    int width = GetInt48(block, "widthMm", 55, 10, 160);
                    string alignment = GetString48(block, "alignment", 12, false);
                    if (alignment.Length == 0) alignment = "center";
                    if (alignment is not ("left" or "center" or "right"))
                        throw new ArgumentException("Image alignment must be left, center or right.");
                    int maximumWidthMm = (int)Math.Floor(tableWidth * 25.4 / 1440.0);
                    if (width > maximumWidthMm)
                        throw new ArgumentException($"Image widthMm={width} exceeds usable page width of {maximumWidthMm} mm.");
                    long cx = width * 36000L;
                    long cy = (long)Math.Round(cx * (double)asset.Height / asset.Width);
                    if (cy > 160 * 36000L) throw new ArgumentException("Image too tall; reduce widthMm.");
                    string caption = GetString48(block, "caption", 300, false);
                    body.Add(DrawingParagraph(asset.RelationshipId, alt, images + 1,
                        asset.FileName, cx, cy, alignment, 0, 0, caption.Length != 0));
                    if (caption.Length != 0)
                    {
                        XElement cap = P48(alignment, 110);
                        cap.Add(TextRun48(caption, false, true, false, "617086", 9));
                        body.Add(cap);
                    }
                    string credit = GetString48(block, "credit", 400, false);
                    if (credit.Length != 0)
                    {
                        XElement sourceP = P48(alignment, 120);
                        sourceP.Add(TextRun48(credit, false, false, false, "65758A", 8));
                        body.Add(sourceP);
                    }
                    images++; break;
                default: throw new ArgumentException("Unsupported Word Studio block type: " + type);
            }
            count++;
        }
        if (images != media.Count)
            throw new ArgumentException("Each provided image must correspond to exactly one image block.");
        int w = pageWidth;
        int h = pageHeight;
        int twips = marginTwips;
        XElement sec = new(W + "sectPr");
        if (headerText != null) sec.Add(new XElement(W + "headerReference",
            new XAttribute(W + "type", "default"), new XAttribute(R + "id", "rId3")));
        if (footerText != null || pages) sec.Add(new XElement(W + "footerReference",
            new XAttribute(W + "type", "default"), new XAttribute(R + "id", "rId4")));
        sec.Add(new XElement(W + "pgSz", new XAttribute(W + "w", w),
            new XAttribute(W + "h", h), new XAttribute(W + "orient", orient)));
        sec.Add(new XElement(W + "pgMar", new XAttribute(W + "top", twips),
            new XAttribute(W + "right", twips), new XAttribute(W + "bottom", twips),
            new XAttribute(W + "left", twips)));
        body.Add(sec);
        XElement root = new(W + "document", new XAttribute(XNamespace.Xmlns + "w", W), body);
        if (headerText != null || footerText != null || pages) root.SetAttributeValue(XNamespace.Xmlns + "r", R);
        XDocument doc = new(root);
        XDocument? hdr = headerText == null ? null : PartDocument(true, headerText, false);
        XDocument? ftr = footerText == null && !pages ? null : PartDocument(false, footerText, pages);
        ct.ThrowIfCancellationRequested();
        XDocument expectedSavedXml = SerializedExpected48(doc);
        PublishNew(path, staged => Package48(staged, doc, accent, profile, hdr, ftr, media), expectedSavedXml);
        // Verify *actual saved* media, relationships, header/footer and numbering parts.
        using (ZipArchive saved = ZipFile.OpenRead(path))
        {
            if (saved.GetEntry("word/numbering.xml") == null ||
                (hdr != null && saved.GetEntry("word/headerR48.xml") == null) ||
                (ftr != null && saved.GetEntry("word/footerR48.xml") == null))
                throw new InvalidDataException("Word Studio saved package missing a required Word part.");
            foreach (Studio48Media asset in media)
            {
                ZipArchiveEntry? entry = saved.GetEntry("word/media/" + asset.FileName);
                if (entry == null) throw new InvalidDataException("Image media part missing: " + asset.FileName);
                using Stream input = entry.Open();
                if (!SHA256.HashData(input).AsSpan().SequenceEqual(SHA256.HashData(asset.Bytes)))
                    throw new InvalidDataException("Saved image bytes differ from original: " + asset.FileName);
            }
            using Stream relStream = saved.GetEntry("word/_rels/document.xml.rels")?.Open() ??
                throw new InvalidDataException("Word media relationships missing.");
            XDocument savedRelations = LoadXml(relStream);
            foreach (Studio48Media asset in media)
                if (!savedRelations.Descendants(PR48 + "Relationship").Any(r =>
                    (string?)r.Attribute("Id") == asset.RelationshipId &&
                    (string?)r.Attribute("Type") == BaseRel48 + "image" &&
                    (string?)r.Attribute("Target") == "media/" + asset.FileName))
                    throw new InvalidDataException("Word image relationship not preserved: " + asset.RelationshipId);
        }
        string structure = VerifyPublishedDocument(path, expectedSavedXml);
        var hash = await NIRAFileIntegrity.HashAsync(path, CancellationToken.None).ConfigureAwait(false);
        return new NIRACapabilityHandlerResult
        {
            ChangedSystemState = true,
            Summary = "R48 Word Studio: complete styled Word document saved and structurally verified with one atomic new-file operation.",
            Output = $"Destination={path}\nDestinationSHA256={hash.Sha256}\nDestinationBytes={hash.Bytes}\n" +
                $"StudioBlocks={count}\nStudioHeadings={headings}\nStudioTables={tables}\nStudioCharts={charts}\nStudioLists={lists}\n" +
                $"StudioPageBreaks={breaks}\nStudioLinks={links}\nStudioImages={images}\n" +
                $"BodyWords={bodyWords}\nMinimumBodyWords={minimumBodyWords}\nMinimumImages={minimumImages}\nProfile={profile}\n" +
                $"HeaderPresent={hdr != null}\nFooterPresent={ftr != null}\nPageNumberField={pages}\n" +
                $"Paper={paper}\nOrientation={orient}\nMarginMm={margin}\nAccentHex={accent}\n" +
                $"{structure}\nOverwroteExisting=False\nVisualPaginationVerified=False"
        };
    }
}

public sealed class NIRADocxStudioCapabilityHandler : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor { get; } = new()
    {
        Id = NIRACapabilityIds.DocxStudioCreate,
        DefaultRisk = NIRACapabilityRisk.Modify,
        Description = "Native Word composer: create a NEW structured DOCX atomically. Academic/business/general typography, substantive paragraphs, headings, bullets, numbering, full-width tables, editable numeric data-bar charts, quotations, images (up to six), captions/credits, hyperlinks, header/footer and PAGE field. Optional minimumBodyWords prevents short content from satisfying longer assignments. Images require verified existing local files and independent reuse-right checks. Verifies saved XML/media, never rendered pagination.",
        Parameters = [
            NIRADocx.Param("path", true, "NEW unused .docx destination in an authorized folder."),
            NIRADocx.Param("title", true, "Title of document; one line, maximum 180 characters."),
            NIRADocx.Param("blocksJson", true, "JSON array 1-120 of blocks: paragraph, heading(level 1-3), bullet, number, table(rows 2-40 x 2-8), chart(data:[{label,value}], optional title/source), quote, divider, toc, pagebreak, link(text,url), image(altText,widthMm,caption,credit,imagePath). Paragraph/heading/list: text or runs with formatting. Use multiple image blocks with each imagePath. Do not insert charts/tables without grounded data."),
            NIRADocx.Param("paper", false, "A4 (default) or LETTER"),
            NIRADocx.Param("orientation", false, "portrait (default) or landscape"),
            NIRADocx.Param("marginMm", false, "10-35, default 20"),
            NIRADocx.Param("accentHex", false, "Six RGB hex digits; default 17365D"),
            NIRADocx.Param("headerText", false, "Optional one-line running header"),
            NIRADocx.Param("footerText", false, "Optional one-line running footer"),
            NIRADocx.Param("pageNumbers", false, "true for native Word PAGE field; default false"),
            NIRADocx.Param("imagePath", false, "Backward compatibility: local PNG/JPEG if exactly one image block lacks its own imagePath."),
            NIRADocx.Param("profile", false, "general (default), academic (student/research writing), or business."),
            NIRADocx.Param("minimumBodyWords", false, "Optional word-count quality gate 0-12000, as a STRING integer; for explicit long-form tasks set from requested length. No output if insufficient substantive text."),
            NIRADocx.Param("minimumImages", false, "Optional 0-6 STRING quality gate for images when user requires them; only count verified local image blocks. Missing images cause a pre-write failure, not a false success.")
        ]
    };
    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest request) => NIRACapabilityRisk.Modify;
    public Task<NIRACapabilityHandlerResult> ExecuteAsync(NIRACapabilityRequest request,
        CancellationToken cancellationToken = default) => NIRADocx.Studio48Async(request, cancellationToken);
}
