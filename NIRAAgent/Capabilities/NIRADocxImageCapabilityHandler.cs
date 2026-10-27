using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace NIRAAgent.Capabilities;

// R43B: insert one verified local PNG/JPEG as a real DrawingML inline image
// after one exact, unique top-level paragraph. Existing DOCX files are never
// mutated or overwritten. This does not invoke Word, COM, or the shell.
internal static partial class NIRADocx
{
    private static readonly XNamespace WP43B = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private static readonly XNamespace A43B = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace PIC43B = "http://schemas.openxmlformats.org/drawingml/2006/picture";
    private const string ImageRelationship = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";
    private const long MaximumLogoBytes = 5 * 1024 * 1024;

    private static (string Ext, string Mime, int Width, int Height) VerifyImage(byte[] data)
    {
        if (data.Length < 24 || data.Length > MaximumLogoBytes)
            throw new InvalidDataException("Image must be a complete PNG/JPEG between 24 bytes and 5 MiB.");
        if (data.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            if (!data.AsSpan(12, 4).SequenceEqual("IHDR"u8) ||
                BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8, 4)) != 13)
                throw new InvalidDataException("PNG does not have a valid IHDR section.");
            uint w = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(16, 4));
            uint h = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(20, 4));
            if (w is < 1 or > 10000 || h is < 1 or > 10000)
                throw new InvalidDataException("PNG dimensions exceed the supported range.");
            if (!data.AsSpan(data.Length - 12, 12).SequenceEqual(new byte[] {
                0, 0, 0, 0, 73, 69, 78, 68, 174, 66, 96, 130 }))
                throw new InvalidDataException("PNG is missing its final IEND chunk.");
            return ("png", "image/png", (int)w, (int)h);
        }
        if (data[0] != 0xFF || data[1] != 0xD8)
            throw new InvalidDataException("Only real PNG and JPEG image bytes are allowed.");
        int offset = 2;
        while (offset + 4 <= data.Length)
        {
            if (data[offset++] != 0xFF) break;
            while (offset < data.Length && data[offset] == 0xFF) offset++;
            if (offset >= data.Length) break;
            byte marker = data[offset++];
            if (marker is 0xD9 or 0xDA) break;
            if (marker == 0x01 || marker is >= 0xD0 and <= 0xD7) continue;
            if (offset + 2 > data.Length) break;
            int len = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2));
            if (len < 2 || offset + len > data.Length) break;
            if (marker is 0xC0 or 0xC1 or 0xC2)
            {
                if (len < 7) break;
                int h = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset + 3, 2));
                int w = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset + 5, 2));
                if (w is < 1 or > 10000 || h is < 1 or > 10000)
                    throw new InvalidDataException("JPEG dimensions exceed the supported range.");
                // Require the real JPEG end-of-image marker; metadata alone is insufficient.
                if (data[^2] != 0xFF || data[^1] != 0xD9)
                    throw new InvalidDataException("JPEG is truncated (no EOI marker).");
                return ("jpg", "image/jpeg", w, h);
            }
            offset += len;
        }
        throw new InvalidDataException("Could not decode supported JPEG dimensions.");
    }

    private static XElement DrawingParagraph(string embedId, string alt, int drawingId,
        string filename, long cx, long cy, string align)
    {
        var pic = new XElement(PIC43B + "pic",
            new XElement(PIC43B + "nvPicPr",
                new XElement(PIC43B + "cNvPr", new XAttribute("id", 0),
                    new XAttribute("name", filename), new XAttribute("descr", alt)),
                new XElement(PIC43B + "cNvPicPr", new XElement(A43B + "picLocks",
                    new XAttribute("noChangeAspect", 1)))),
            new XElement(PIC43B + "blipFill",
                new XElement(A43B + "blip", new XAttribute(R + "embed", embedId)),
                new XElement(A43B + "stretch", new XElement(A43B + "fillRect"))),
            new XElement(PIC43B + "spPr",
                new XElement(A43B + "xfrm", new XElement(A43B + "off",
                    new XAttribute("x", 0), new XAttribute("y", 0)),
                    new XElement(A43B + "ext", new XAttribute("cx", cx), new XAttribute("cy", cy))),
                new XElement(A43B + "prstGeom", new XAttribute("prst", "rect"),
                    new XElement(A43B + "avLst"))));
        var inline = new XElement(WP43B + "inline",
            new XAttribute("distT", 0), new XAttribute("distB", 0),
            new XAttribute("distL", 0), new XAttribute("distR", 0),
            new XElement(WP43B + "extent", new XAttribute("cx", cx), new XAttribute("cy", cy)),
            new XElement(WP43B + "effectExtent", new XAttribute("l", 0),
                new XAttribute("t", 0), new XAttribute("r", 0), new XAttribute("b", 0)),
            new XElement(WP43B + "docPr", new XAttribute("id", drawingId),
                new XAttribute("name", "NIRA R43B image " + drawingId),
                new XAttribute("descr", alt)),
            new XElement(WP43B + "cNvGraphicFramePr",
                new XElement(A43B + "graphicFrameLocks", new XAttribute("noChangeAspect", 1))),
            new XElement(A43B + "graphic", new XElement(A43B + "graphicData",
                new XAttribute("uri", PIC43B.NamespaceName), pic)));
        return new XElement(W + "p",
            new XElement(W + "pPr", new XElement(W + "jc", new XAttribute(W + "val", align))),
            new XElement(W + "r", new XElement(W + "drawing", inline)));
    }

    private static void ImagePackageCopy(string source, string destination, XDocument document,
        byte[] image, string mediaPart, string mime, string imageRelId)
    {
        using FileStream input = new(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        using ZipArchive oldZip = new(input, ZipArchiveMode.Read);
        ValidatePackage(oldZip);
        using FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using (ZipArchive newZip = new(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (ZipArchiveEntry entry in oldZip.Entries)
            {
                if (entry.FullName == DocumentPart)
                {
                    WriteXml(newZip, DocumentPart, document);
                    continue;
                }
                if (entry.FullName == DocumentRels || entry.FullName == ContentTypePart)
                {
                    XDocument xml;
                    using (Stream read = entry.Open()) xml = LoadXml(read);
                    if (entry.FullName == DocumentRels)
                    {
                        xml.Root!.Add(new XElement(PkgRel + "Relationship",
                            new XAttribute("Id", imageRelId),
                            new XAttribute("Type", ImageRelationship),
                            new XAttribute("Target", mediaPart["word/".Length..])));
                    }
                    else
                    {
                        string ext = Path.GetExtension(mediaPart).TrimStart('.');
                        XElement? existing = xml.Root!.Elements(TypesNs + "Default").FirstOrDefault(x =>
                            string.Equals((string?)x.Attribute("Extension"), ext, StringComparison.OrdinalIgnoreCase));
                        if (existing != null && (string?)existing.Attribute("ContentType") != mime)
                            throw new InvalidDataException("Existing image extension has conflicting content type.");
                        if (existing == null)
                            xml.Root.Add(new XElement(TypesNs + "Default",
                                new XAttribute("Extension", ext), new XAttribute("ContentType", mime)));
                    }
                    WriteXml(newZip, entry.FullName, xml);
                    continue;
                }
                ZipArchiveEntry target = newZip.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                using Stream readEntry = entry.Open();
                using Stream writeEntry = target.Open();
                readEntry.CopyTo(writeEntry);
            }
            ZipArchiveEntry media = newZip.CreateEntry(mediaPart, CompressionLevel.Optimal);
            using Stream mediaStream = media.Open();
            mediaStream.Write(image);
        }
        output.Flush(true);
    }

    private static bool VerifyPublishedImage(ZipArchive archive, XDocument document, string relId,
        string mediaPart, byte[] image, string alt, string anchor, int priorImages)
    {
        var embeds = document.Descendants(A43B + "blip").Where(x =>
            (string?)x.Attribute(R + "embed") == relId).ToArray();
        var drawings = document.Descendants(WP43B + "docPr").Where(x =>
            (string?)x.Attribute("descr") == alt).ToArray();
        if (embeds.Length != 1 || drawings.Length != 1) return false;
        XElement? anchorP = document.Root?.Element(W + "body")?.Elements(W + "p")
            .SingleOrDefault(x => string.Concat(x.Descendants(W + "t").Select(t => t.Value)) == anchor);
        if (anchorP?.ElementsAfterSelf(W + "p").FirstOrDefault()?.Descendants(A43B + "blip")
            .Count(x => (string?)x.Attribute(R + "embed") == relId) != 1) return false;
        ZipArchiveEntry? relEntry = archive.GetEntry(DocumentRels);
        if (relEntry is null) return false;
        XDocument rels;
        using (Stream read = relEntry.Open()) rels = LoadXml(read);
        XElement? rel = rels.Descendants(PkgRel + "Relationship").SingleOrDefault(x =>
            (string?)x.Attribute("Id") == relId);
        if ((string?)rel?.Attribute("Type") != ImageRelationship ||
            (string?)rel?.Attribute("Target") != mediaPart["word/".Length..]) return false;
        ZipArchiveEntry? imageEntry = archive.GetEntry(mediaPart);
        if (imageEntry is null || imageEntry.Length != image.LongLength) return false;
        using Stream imgStream = imageEntry.Open();
        using MemoryStream stored = new();
        imgStream.CopyTo(stored);
        if (!stored.ToArray().AsSpan().SequenceEqual(image)) return false;
        int after = archive.Entries.Count(x => x.FullName.StartsWith("word/media/", StringComparison.OrdinalIgnoreCase));
        return after == priorImages + 1;
    }

    internal static async Task<NIRACapabilityHandlerResult> ImageInsertAsync(
        NIRACapabilityRequest request, CancellationToken ct)
    {
        string source = GetPath(request, "source");
        string destination = GetPath(request, "destination");
        string imagePath = NIRAFileIntegrity.PathArgument(request, "imagePath");
        NIRAFileIntegrity.Different(source, destination);
        NIRAFileIntegrity.RejectLinkedPaths(imagePath);
        NIRAFileIntegrity.RejectLinkedPaths(destination);
        NIRAFileIntegrity.RequireNewDestination(destination);
        string anchor = RequiredText(request, "afterParagraph", 300).Trim();
        string alt = RequiredText(request, "altText", 200).Trim();
        if (anchor.Contains('\n') || anchor.Contains('\r') || alt.Contains('\n') || alt.Contains('\r'))
            throw new ArgumentException("Anchor and image alt text must each be one line.");
        string expected = NIRACapabilityArguments.RequireString(request, "expectedSourceSha256", 64);
        if (expected.Length != 64 || expected.Any(x => !Uri.IsHexDigit(x)))
            throw new ArgumentException("expectedSourceSha256 must be a fresh SHA-256 digest.");
        string widthText = NIRACapabilityArguments.GetOptionalRawString(request, "widthMm", 10) ?? "42";
        if (!double.TryParse(widthText, NumberStyles.Float, CultureInfo.InvariantCulture, out double widthMm) ||
            !double.IsFinite(widthMm) || widthMm < 10 || widthMm > 140)
            throw new ArgumentException("widthMm must be a decimal from 10 to 140.");
        string alignment = NIRACapabilityArguments.GetOptionalRawString(request, "alignment", 10) ?? "center";
        if (alignment is not ("left" or "center" or "right"))
            throw new ArgumentException("alignment must be left, center, or right.");
        RequireValidSource(source);
        var localImage = new FileInfo(imagePath);
        if (!localImage.Exists || localImage.Length is < 24 or > MaximumLogoBytes)
            throw new InvalidDataException("A local PNG/JPEG file between 24 bytes and 5 MiB is required.");
        byte[] bytes = await File.ReadAllBytesAsync(imagePath, ct).ConfigureAwait(false);
        var format = VerifyImage(bytes);
        if (new FileInfo(source).Length + bytes.Length + 65536L > MaxPackageBytes)
            throw new InvalidOperationException("The resulting DOCX could exceed the supported 16 MiB package limit.");
        var before = await NIRAFileIntegrity.HashAsync(source, ct).ConfigureAwait(false);
        if (!string.Equals(before.Sha256, expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Source SHA-256 changed; image insertion has not started.");
        XDocument document;
        string mediaPart, relId;
        int beforeMediaCount;
        using (FileStream file = new(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (ZipArchive zip = new(file, ZipArchiveMode.Read))
        {
            document = OpenDocument(zip);
            XElement? body = document.Root?.Element(W + "body");
            if (body is null) throw new InvalidDataException("Word body is missing.");
            XElement[] matches = body.Elements(W + "p").Where(x =>
                string.Concat(x.Descendants(W + "t").Select(t => t.Value)) == anchor).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException("The requested paragraph must match exactly once at document body level.");
            ZipArchiveEntry? relEntry = zip.GetEntry(DocumentRels);
            if (relEntry == null) throw new InvalidDataException("Document relationships missing.");
            XDocument rels;
            using (Stream stream = relEntry.Open()) rels = LoadXml(stream);
            var used = new HashSet<string>(rels.Descendants(PkgRel + "Relationship")
                .Select(x => (string?)x.Attribute("Id") ?? ""), StringComparer.Ordinal);
            relId = Enumerable.Range(1, 100000).Select(i => "rId" + i)
                .FirstOrDefault(i => !used.Contains(i)) ??
                throw new InvalidOperationException("No available relationship IDs.");
            string prefix = "word/media/nira-r43b-image-";
            mediaPart = Enumerable.Range(1, 100000).Select(i => prefix + i + "." + format.Ext)
                .FirstOrDefault(candidate => zip.GetEntry(candidate) == null) ??
                throw new InvalidOperationException("No safe Word media part name.");
            beforeMediaCount = zip.Entries.Count(x => x.FullName.StartsWith("word/media/", StringComparison.OrdinalIgnoreCase));
            int maxId = document.Descendants(WP43B + "docPr")
                .Select(x => int.TryParse((string?)x.Attribute("id"), out int id) ? id : 0)
                .DefaultIfEmpty(0).Max();
            if (maxId == int.MaxValue) throw new InvalidOperationException("Drawing IDs are exhausted.");
            long cx = checked((long)Math.Round(widthMm * 36000, MidpointRounding.AwayFromZero));
            long cy = checked((long)Math.Round(cx * (double)format.Height / format.Width, MidpointRounding.AwayFromZero));
            if (cy > 160L * 36000L)
                throw new ArgumentException("The image would be taller than 160 mm; reduce widthMm.");
            // Explicit relationship prefix keeps the staged XML stable after serialization.
            document.Root!.SetAttributeValue(XNamespace.Xmlns + "r", R);
            matches[0].AddAfterSelf(DrawingParagraph(relId, alt, maxId + 1,
                Path.GetFileName(imagePath), cx, cy, alignment));
        }
        var ready = await NIRAFileIntegrity.HashAsync(source, ct).ConfigureAwait(false);
        if (!string.Equals(ready.Sha256, expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Source changed during preparation; no image document created.");
        ct.ThrowIfCancellationRequested();
        PublishNew(destination, path => ImagePackageCopy(source, path, document, bytes,
            mediaPart, format.Mime, relId), document);
        using (FileStream file = new(destination, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (ZipArchive zip = new(file, ZipArchiveMode.Read))
        {
            XDocument saved = OpenDocument(zip);
            if (!VerifyPublishedImage(zip, saved, relId, mediaPart, bytes,
                    alt, anchor, beforeMediaCount))
                throw new InvalidDataException("The saved Word drawing or image bytes failed post-write verification.");
        }
        var sourceAfter = await NIRAFileIntegrity.HashAsync(source, CancellationToken.None).ConfigureAwait(false);
        var output = await NIRAFileIntegrity.HashAsync(destination, CancellationToken.None).ConfigureAwait(false);
        if (!string.Equals(sourceAfter.Sha256, expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Original was changed externally; review the new image document.");
        return new NIRACapabilityHandlerResult
        {
            ChangedSystemState = true,
            Summary = "Native inline Word image inserted into new DOCX and independently re-opened for byte/relationship verification.",
            Output = $"Source={source}\nSourceSHA256={expected}\nDestination={destination}\nDestinationSHA256={output.Sha256}\n" +
                     $"ImagePath={imagePath}\nImageSHA256={Convert.ToHexString(SHA256.HashData(bytes))}\nImageType={format.Mime}\n" +
                     $"ImagePixels={format.Width}x{format.Height}\nImageWidthMm={widthMm.ToString("0.##", CultureInfo.InvariantCulture)}\n" +
                     $"ImageAltText={alt}\nAnchorParagraph={anchor}\nEmbeddedMediaParts={beforeMediaCount + 1}\n" +
                     "PostWriteVerified=True\nSourceUnchanged=True\nOverwroteExisting=False"
        };
    }
}

public sealed class NIRADocxImageInsertCapabilityHandler : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor { get; } = new()
    {
        Id = NIRACapabilityIds.DocxImageInsert,
        DefaultRisk = NIRACapabilityRisk.Modify,
        Description = "Insert one real local PNG/JPEG logo or photo after exactly one existing body paragraph into a NEW DOCX copy. Preserves current text, table, page layout, headers/footers. Fresh SHA-256 and no overwrite; verifies media bytes and DrawingML after writing.",
        Parameters = [
            NIRADocx.Param("source", true, "Existing .docx (read only)."),
            NIRADocx.Param("destination", true, "NEW nonexisting .docx in an authorized folder."),
            NIRADocx.Param("imagePath", true, "Existing absolute PNG/JPEG file path on the PC (not a URL)."),
            NIRADocx.Param("afterParagraph", true, "Exact unique plain text of a body paragraph. Image appears after it."),
            NIRADocx.Param("altText", true, "Brief meaningful description of the embedded image."),
            NIRADocx.Param("expectedSourceSha256", true, "Fresh observed SHA-256 of the DOCX source."),
            NIRADocx.Param("widthMm", false, "Image display width 10-140 mm, e.g. '42'. Proportional height."),
            NIRADocx.Param("alignment", false, "Image paragraph alignment: left, center (default), right.")
        ]
    };
    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest request) => NIRACapabilityRisk.Modify;
    public Task<NIRACapabilityHandlerResult> ExecuteAsync(NIRACapabilityRequest request,
        CancellationToken cancellationToken = default) => NIRADocx.ImageInsertAsync(request, cancellationToken);
}
