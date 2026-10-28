using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;

namespace NIRAAgent.Capabilities;

// R44A: add data rows to one uniquely identified existing Word table.
// A copy of the original document is produced; only document.xml changes.
// New rows inherit existing data-row appearance including alternate shading.
internal static partial class NIRADocx
{
    internal static async Task<NIRACapabilityHandlerResult> TableRowsAppendAsync(
        NIRACapabilityRequest request, CancellationToken cancellationToken)
    {
        string source = GetPath(request, "source");
        string destination = GetPath(request, "destination");
        NIRAFileIntegrity.Different(source, destination);
        string expected = NIRACapabilityArguments.RequireString(request, "expectedSourceSha256", 64);
        if (expected.Length != 64 || expected.Any(ch => !Uri.IsHexDigit(ch)))
            throw new ArgumentException("expectedSourceSha256 must be a fresh 64-character SHA-256.");
        string[] headers = RequiredText(request, "headerTsv", 1000).Split('\t');
        if (headers.Length is < 2 or > 10 || headers.Any(h =>
            string.IsNullOrWhiteSpace(h) || h.Contains('\n') || h.Contains('\r')))
            throw new ArgumentException("headerTsv requires 2-10 exact, TAB-separated header cells.");
        string rowsTsv = RequiredText(request, "rowsTsv", 12000)
            .Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');
        string[] lines = rowsTsv.Split('\n');
        if (lines.Length is < 1 or > 20)
            throw new ArgumentException("rowsTsv requires 1-20 new data rows.");
        string[][] appended = lines.Select(line => line.Split('\t')).ToArray();
        if (appended.Any(row => row.Length != headers.Length || row.Any(cell =>
            string.IsNullOrWhiteSpace(cell) || cell.Length > 800)))
            throw new ArgumentException("Each new row must have the exact header column count and nonempty cells (max 800 characters each).");

        RequireValidSource(source);
        NIRAFileIntegrity.RejectLinkedPaths(destination);
        NIRAFileIntegrity.RequireNewDestination(destination);
        var before = await NIRAFileIntegrity.HashAsync(source, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(before.Sha256, expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Source SHA-256 is stale; nothing written.");

        XDocument updated;
        int rowsBefore;
        int tableCount;
        using (FileStream file = new(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (ZipArchive archive = new(file, ZipArchiveMode.Read))
        {
            updated = OpenDocument(archive);
            XElement body = updated.Root!.Element(W + "body")!;
            XElement[] tables = body.Elements(W + "tbl").Where(table =>
            {
                XElement? first = table.Elements(W + "tr").FirstOrDefault();
                if (first is null) return false;
                string[] cells = first.Elements(W + "tc").Select(cell =>
                    string.Concat(cell.Descendants(W + "t").Select(t => t.Value))).ToArray();
                return cells.SequenceEqual(headers, StringComparer.Ordinal);
            }).ToArray();
            if (tables.Length != 1)
                throw new InvalidOperationException(
                    $"Header must identify exactly one top-level table (found {tables.Length}). Nothing written.");
            XElement table = tables[0];
            XElement[] originalRows = table.Elements(W + "tr").ToArray();
            rowsBefore = originalRows.Length;
            tableCount = body.Descendants(W + "tbl").Count();
            if (rowsBefore is < 2 or > 200 || rowsBefore + appended.Length > 200)
                throw new InvalidOperationException("Supported simple tables must have 2-200 rows after appending.");
            if (originalRows.Any(row => row.Elements(W + "tc").Count() != headers.Length ||
                row.Descendants(W + "gridSpan").Any() ||
                row.Descendants(W + "vMerge").Any() ||
                row.Descendants(W + "tbl").Any() ||
                row.Elements(W + "tc").Any(cell => cell.Elements(W + "p").Count() != 1 ||
                    cell.Elements().Any(x => x.Name != W + "p" && x.Name != W + "tcPr"))))
                throw new InvalidOperationException("Merged, nested or complex table structure is unsupported; nothing written.");

            // Select the last EXISTING data row of the same alternating parity.
            // This preserves the source table's exact stripe/unshaded pattern.
            for (int i = 0; i < appended.Length; i++)
            {
                int newIndex = rowsBefore + i;
                int templateIndex = Enumerable.Range(1, rowsBefore - 1)
                    .Where(index => index % 2 == newIndex % 2)
                    .DefaultIfEmpty(rowsBefore - 1).Last();
                XElement clone = new(originalRows[templateIndex]);
                XElement[] cells = clone.Elements(W + "tc").ToArray();
                for (int column = 0; column < cells.Length; column++)
                {
                    XElement paragraph = cells[column].Elements(W + "p").Single();
                    // Refuse to clone a row with drawings, links, fields or split text;
                    // otherwise replacing its content could drop invisible data.
                    if (paragraph.Elements().Any(x => x.Name != W + "pPr" && x.Name != W + "r") ||
                        paragraph.Elements(W + "r").Count() != 1 ||
                        paragraph.Descendants(W + "t").Count() != 1 ||
                        paragraph.Descendants(W + "drawing").Any() ||
                        paragraph.Descendants(W + "fldChar").Any() ||
                        paragraph.Descendants(W + "instrText").Any())
                        throw new InvalidOperationException("Data-row template has complex cell content; nothing written.");
                    XElement text = paragraph.Descendants(W + "t").Single();
                    text.Value = appended[i][column];
                    text.SetAttributeValue(XNamespace.Xml + "space", "preserve");
                }
                table.Add(clone);
            }
        }
        var recheck = await NIRAFileIntegrity.HashAsync(source, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(recheck.Sha256, expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Source changed during preparation; nothing written.");
        cancellationToken.ThrowIfCancellationRequested();
        PublishNew(destination, stage => Repackage(source, stage, updated), updated);

        string verified = VerifyPublishedDocument(destination, updated);
        using (FileStream file = new(destination, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (ZipArchive archive = new(file, ZipArchiveMode.Read))
        {
            XDocument stored = OpenDocument(archive);
            XElement[] matches = stored.Root!.Element(W + "body")!.Elements(W + "tbl")
                .Where(table => table.Elements(W + "tr").FirstOrDefault() is XElement row &&
                    row.Elements(W + "tc").Select(cell =>
                        string.Concat(cell.Descendants(W + "t").Select(t => t.Value)))
                        .SequenceEqual(headers, StringComparer.Ordinal)).ToArray();
            if (matches.Length != 1 ||
                matches[0].Elements(W + "tr").Count() != rowsBefore + appended.Length ||
                stored.Descendants(W + "tbl").Count() != tableCount)
                throw new InvalidDataException("Saved table structure failed independent verification.");
            XElement[] rows = matches[0].Elements(W + "tr").ToArray();
            for (int i = 0; i < appended.Length; i++)
            {
                string[] saved = rows[rowsBefore + i].Elements(W + "tc").Select(cell =>
                    string.Concat(cell.Descendants(W + "t").Select(t => t.Value))).ToArray();
                if (!saved.SequenceEqual(appended[i], StringComparer.Ordinal))
                    throw new InvalidDataException("Saved table cell text failed exact verification.");
            }
        }
        var sourceAfter = await NIRAFileIntegrity.HashAsync(source, CancellationToken.None).ConfigureAwait(false);
        var output = await NIRAFileIntegrity.HashAsync(destination, CancellationToken.None).ConfigureAwait(false);
        if (!string.Equals(sourceAfter.Sha256, expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Source changed externally; inspect the new output.");
        return new NIRACapabilityHandlerResult
        {
            ChangedSystemState = true,
            Summary = "Verified data rows appended to one existing Word table in a NEW DOCX copy; source preserved.",
            Output = $"Source={source}\nSourceSHA256={sourceAfter.Sha256}\nDestination={destination}\n" +
                $"DestinationSHA256={output.Sha256}\nTableCount={tableCount}\n" +
                $"TableRowsBefore={rowsBefore}\nTableRowsAppended={appended.Length}\n" +
                $"TableRowsAfter={rowsBefore + appended.Length}\n" +
                $"LastRowCellsJson={JsonSerializer.Serialize(appended[^1])}\n{verified}\n" +
                "SourceUnchanged=True\nOverwroteExisting=False"
        };
    }
}

public sealed class NIRADocxTableRowsAppendCapabilityHandler : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor { get; } = new()
    {
        Id = NIRACapabilityIds.DocxTableRowsAppend,
        DefaultRisk = NIRACapabilityRisk.Modify,
        Description = "Append 1-20 rows to a uniquely identified simple existing Word table in a NEW DOCX copy; keeps header formatting, alternating data-row appearance, images, headers, footer, PAGE field and page layout. Requires fresh SHA-256, exact table header and a nonexisting destination; independently verifies saved rows.",
        Parameters = [
            NIRADocx.Param("source", true, "Existing source .docx (never modified)."),
            NIRADocx.Param("destination", true, "NEW nonexisting .docx output path in authorized folder."),
            NIRADocx.Param("headerTsv", true, "Exact table header cells joined with real TAB characters (JSON \\t)."),
            NIRADocx.Param("rowsTsv", true, "1-20 new data rows as TSV, real TAB between cells and newline between rows."),
            NIRADocx.Param("expectedSourceSha256", true, "Fresh SHA-256 of the source from filesystem.docx.inspect or filesystem.hash.")
        ]
    };
    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest request) => NIRACapabilityRisk.Modify;
    public Task<NIRACapabilityHandlerResult> ExecuteAsync(NIRACapabilityRequest request,
        CancellationToken cancellationToken = default) =>
        NIRADocx.TableRowsAppendAsync(request, cancellationToken);
}
