using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace NIRAAgent.Capabilities;

// R43D: insert a real Word hard page break with non-overwriting copy-on-write.
// Existing complex formatting, images, headers/footers, tables, and margins
// are carried forward as untouched parts or unchanged body nodes.
internal static partial class NIRADocx
{
    internal static async Task<NIRACapabilityHandlerResult> PageBreakInsertAsync(
        NIRACapabilityRequest request, CancellationToken cancellationToken)
    {
        string source = GetPath(request, "source");
        string destination = GetPath(request, "destination");
        NIRAFileIntegrity.Different(source, destination);
        string anchorText = RequiredText(request, "afterParagraph", 500);
        if (anchorText.Contains('\r') || anchorText.Contains('\n') || anchorText.Contains('\t'))
            throw new ArgumentException("afterParagraph must be one exact, single-line body paragraph.");
        string expected = NIRACapabilityArguments.RequireString(request, "expectedSourceSha256", 64);
        if (expected.Length != 64 || expected.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("expectedSourceSha256 must be a 64-character SHA-256.");

        RequireValidSource(source);
        NIRAFileIntegrity.RejectLinkedPaths(destination);
        NIRAFileIntegrity.RequireNewDestination(destination);
        var before = await NIRAFileIntegrity.HashAsync(source, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(before.Sha256, expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The DOCX source SHA-256 has changed. Nothing written.");

        XDocument updated;
        int oldBreaks;
        int originalTables;
        int originalImages;
        using (FileStream input = new(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (ZipArchive archive = new(input, ZipArchiveMode.Read))
        {
            updated = OpenDocument(archive);
            XElement body = updated.Root!.Element(W + "body")!;
            XElement[] anchors = body.Elements(W + "p").Where(p =>
                string.Equals(string.Concat(p.Descendants(W + "t").Select(x => x.Value)),
                    anchorText, StringComparison.Ordinal)).ToArray();
            if (anchors.Length != 1)
                throw new InvalidOperationException(
                    $"Expected exactly one top-level paragraph matching afterParagraph; found {anchors.Length}. Nothing written.");
            oldBreaks = updated.Descendants(W + "br").Count(br =>
                (string?)br.Attribute(W + "type") == "page");
            originalTables = updated.Descendants(W + "tbl").Count();
            originalImages = archive.Entries.Count(e => e.FullName.StartsWith("word/media/", StringComparison.OrdinalIgnoreCase)
                && !e.FullName.EndsWith('/'));
            XElement breakParagraph = new(W + "p", new XElement(W + "r",
                new XElement(W + "br", new XAttribute(W + "type", "page"))));
            anchors[0].AddAfterSelf(breakParagraph);
        }
        var recheck = await NIRAFileIntegrity.HashAsync(source, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(recheck.Sha256, expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Source changed during DOCX inspection. Nothing written.");
        cancellationToken.ThrowIfCancellationRequested();
        PublishNew(destination, stage => Repackage(source, stage, updated), updated);

        // Verify the published package, not just the pre-publication model.
        int savedBreaks;
        int savedTables;
        int savedImages;
        using (FileStream file = new(destination, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (ZipArchive outputArchive = new(file, ZipArchiveMode.Read))
        {
            XDocument stored = OpenDocument(outputArchive);
            if (!XNode.DeepEquals(stored.Root, updated.Root))
                throw new InvalidDataException("Published DOCX does not match the intended page-break structure.");
            savedBreaks = stored.Descendants(W + "br").Count(br =>
                (string?)br.Attribute(W + "type") == "page");
            savedTables = stored.Descendants(W + "tbl").Count();
            savedImages = outputArchive.Entries.Count(e => e.FullName.StartsWith("word/media/", StringComparison.OrdinalIgnoreCase)
                && !e.FullName.EndsWith('/'));
            // Re-open original and compare every unmodified package part by bytes,
            // not merely by relationship count (headers, images, styles, footers).
            using FileStream sourceFile = new(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            using ZipArchive originalArchive = new(sourceFile, ZipArchiveMode.Read);
            if (originalArchive.Entries.Count != outputArchive.Entries.Count)
                throw new InvalidDataException("Page break unexpectedly changed the DOCX package part count.");
            foreach (ZipArchiveEntry originalPart in originalArchive.Entries)
            {
                ZipArchiveEntry? savedPart = outputArchive.GetEntry(originalPart.FullName);
                if (savedPart is null || savedPart.Length != originalPart.Length && originalPart.FullName != "word/document.xml")
                    throw new InvalidDataException("DOCX package parts differ unexpectedly.");
                if (originalPart.FullName == "word/document.xml" || originalPart.FullName.EndsWith('/')) continue;
                using Stream first = originalPart.Open();
                using Stream second = savedPart.Open();
                if (!SHA256.HashData(first).AsSpan().SequenceEqual(SHA256.HashData(second)))
                    throw new InvalidDataException("DOCX original media/formatting part changed unexpectedly.");
            }
        }
        var afterSource = await NIRAFileIntegrity.HashAsync(source, CancellationToken.None).ConfigureAwait(false);
        var afterOutput = await NIRAFileIntegrity.HashAsync(destination, CancellationToken.None).ConfigureAwait(false);
        bool preserved = string.Equals(afterSource.Sha256, expected, StringComparison.OrdinalIgnoreCase);
        bool structure = savedBreaks == oldBreaks + 1 && savedTables == originalTables && savedImages == originalImages;
        if (!preserved || !structure)
            throw new InvalidDataException("Post-write document preservation check failed; inspect the newly created output.");
        return new NIRACapabilityHandlerResult
        {
            ChangedSystemState = true,
            Summary = "Verified native Word hard page break inserted in a new DOCX; source preserved.",
            Output = $"Source={source}\nDestination={destination}\nSourceSHA256={afterSource.Sha256}\n" +
                $"DestinationSHA256={afterOutput.Sha256}\nSHA256={afterOutput.Sha256}\nAnchorParagraph={anchorText}\n" +
                $"ManualPageBreaksBefore={oldBreaks}\nManualPageBreaksAfter={savedBreaks}\n" +
                $"Tables={savedTables}\nEmbeddedMediaParts={savedImages}\n" +
                "PostWriteVerified=True\nSourceUnchanged=True\nOverwroteExisting=False\n" +
                "VisualPaginationVerified=False"
        };
    }
}

public sealed class NIRADocxPageBreakInsertCapabilityHandler : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor { get; } = new()
    {
        Id = NIRACapabilityIds.DocxPageBreakInsert,
        DefaultRisk = NIRACapabilityRisk.Modify,
        Description = "Insert exactly one native Word page break after a UNIQUE body paragraph into a NEW DOCX copy. Source remains unchanged; requires fresh SHA-256. Preserves images/captions/tables/header/footer and page layout; saved break count is verified (not visual page count).",
        Parameters = [
            NIRADocx.Param("source", true, "Existing .docx source; never modified."),
            NIRADocx.Param("destination", true, "NEW, nonexisting .docx output in authorized folder."),
            NIRADocx.Param("afterParagraph", true, "Exact unique full text of one top-level body paragraph (not table text)."),
            NIRADocx.Param("expectedSourceSha256", true, "Current source SHA-256 from docx.inspect or filesystem.hash.")
        ]
    };
    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest request) => NIRACapabilityRisk.Modify;
    public Task<NIRACapabilityHandlerResult> ExecuteAsync(NIRACapabilityRequest request,
        CancellationToken cancellationToken = default) => NIRADocx.PageBreakInsertAsync(request, cancellationToken);
}
