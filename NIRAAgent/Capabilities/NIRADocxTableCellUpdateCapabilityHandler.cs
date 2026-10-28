using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

namespace NIRAAgent.Capabilities;

// R44B: verified, single-cell Word table edit. Strict row/table identity,
// compare-and-swap old value, and new-document-only publication.
internal static partial class NIRADocx
{
    private static string CellText44B(XElement cell) =>
        string.Concat(cell.Descendants(W + "t").Select(node => node.Value));

    private static string[] TableCells44B(XElement row) =>
        row.Elements(W + "tc").Select(CellText44B).ToArray();

    private static void CheckSimpleTable44B(XElement table, int expectedColumns)
    {
        XElement[] rows = table.Elements(W + "tr").ToArray();
        if (rows.Length is < 2 or > 200 ||
            rows.Any(row => row.Elements(W + "tc").Count() != expectedColumns ||
                row.Descendants(W + "gridSpan").Any() ||
                row.Descendants(W + "vMerge").Any() ||
                row.Descendants(W + "tbl").Any() ||
                row.Elements(W + "tc").Any(cell =>
                    cell.Elements(W + "p").Count() != 1 ||
                    cell.Elements().Any(x => x.Name != W + "p" && x.Name != W + "tcPr"))))
            throw new InvalidOperationException(
                "R44B supports simple, nonmerged, nonnested tables with 2-200 rows only.");
    }

    private static XElement SimpleText44B(XElement cell)
    {
        XElement paragraph = cell.Elements(W + "p").Single();
        XElement[] runs = paragraph.Elements(W + "r").ToArray();
        if (paragraph.Elements().Any(x => x.Name != W + "pPr" && x.Name != W + "r") ||
            runs.Length != 1 ||
            runs[0].Elements().Any(x => x.Name != W + "rPr" && x.Name != W + "t") ||
            runs[0].Elements(W + "t").Count() != 1 ||
            paragraph.Descendants(W + "drawing").Any() ||
            paragraph.Descendants(W + "fldChar").Any() ||
            paragraph.Descendants(W + "instrText").Any())
            throw new InvalidOperationException(
                "Target cell has mixed runs, drawing, field, or unsupported markup. Nothing written.");
        return runs[0].Element(W + "t")!;
    }

    // Copying the package without changing any other parts is independently
    // verified from decompressed bytes, including media, footer and headers.
    private static void VerifyPreservedOtherParts44B(string source, string destination)
    {
        using ZipArchive original = ZipFile.OpenRead(source);
        using ZipArchive updated = ZipFile.OpenRead(destination);
        ValidatePackage(original);
        ValidatePackage(updated);
        if (original.Entries.Count != updated.Entries.Count)
            throw new InvalidDataException("DOCX package part count was changed.");
        foreach (ZipArchiveEntry oldPart in original.Entries)
        {
            if (oldPart.FullName == DocumentPart) continue;
            ZipArchiveEntry? newPart = updated.GetEntry(oldPart.FullName);
            if (newPart is null || oldPart.Length != newPart.Length)
                throw new InvalidDataException("An unrelated Word package part was changed.");
            using Stream oldStream = oldPart.Open();
            using Stream newStream = newPart.Open();
            if (!SHA256.HashData(oldStream).AsSpan().SequenceEqual(SHA256.HashData(newStream)))
                throw new InvalidDataException("An unrelated Word package part differs from the source.");
        }
    }

    internal static async Task<NIRACapabilityHandlerResult> TableCellUpdate44BAsync(
        NIRACapabilityRequest request, CancellationToken cancellationToken)
    {
        string source = GetPath(request, "source");
        string destination = GetPath(request, "destination");
        NIRAFileIntegrity.Different(source, destination);
        string expected = NIRACapabilityArguments.RequireString(request, "expectedSourceSha256", 64);
        if (expected.Length != 64 || expected.Any(x => !Uri.IsHexDigit(x)))
            throw new ArgumentException("expectedSourceSha256 must be a fresh 64-character SHA-256.");
        string[] headers = RequiredText(request, "headerTsv", 1000).Split('\t');
        if (headers.Length is < 2 or > 10 || headers.Any(x =>
                string.IsNullOrWhiteSpace(x) || x.Contains('\n') || x.Contains('\r')) ||
            headers.Distinct(StringComparer.Ordinal).Count() != headers.Length)
            throw new ArgumentException("headerTsv needs 2-10 unique exact TAB-separated header cells.");
        string rowKey = RequiredText(request, "rowKey", 800);
        string columnHeader = RequiredText(request, "columnHeader", 160);
        string oldValue = RequiredText(request, "expectedOldValue", 800);
        string newValue = RequiredText(request, "newValue", 800);
        if (new[] { rowKey, columnHeader, oldValue, newValue }.Any(x =>
                x != x.Trim() || x.Contains('\t') || x.Contains('\n') || x.Contains('\r')))
            throw new ArgumentException("Cell selectors and values must be nonblank single-line text, without surrounding whitespace.");
        if (newValue == oldValue)
            throw new ArgumentException("newValue must differ from expectedOldValue; no change requested.");
        int targetColumn = Array.IndexOf(headers, columnHeader);
        if (targetColumn < 1)
            throw new ArgumentException("columnHeader must name exactly one NON-KEY column in headerTsv.");

        RequireValidSource(source);
        NIRAFileIntegrity.RejectLinkedPaths(destination);
        NIRAFileIntegrity.RequireNewDestination(destination);
        var sourceHash = await NIRAFileIntegrity.HashAsync(source, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(sourceHash.Sha256, expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Source hash is stale; no output written.");

        XDocument updated;
        int tableCount, rowCount, rowIndex;
        using (FileStream file = new(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (ZipArchive archive = new(file, ZipArchiveMode.Read))
        {
            updated = OpenDocument(archive);
            XElement body = updated.Root!.Element(W + "body")!;
            XElement[] tables = body.Elements(W + "tbl").Where(table =>
                table.Elements(W + "tr").FirstOrDefault() is XElement head &&
                TableCells44B(head).SequenceEqual(headers, StringComparer.Ordinal)).ToArray();
            if (tables.Length != 1)
                throw new InvalidOperationException(
                    $"Table header must identify one unique top-level table; found {tables.Length}.");
            XElement table = tables[0];
            CheckSimpleTable44B(table, headers.Length);
            tableCount = body.Descendants(W + "tbl").Count();
            XElement[] rows = table.Elements(W + "tr").ToArray();
            rowCount = rows.Length;
            XElement[] found = rows.Skip(1).Where(row =>
                CellText44B(row.Elements(W + "tc").First()) == rowKey).ToArray();
            if (found.Length != 1)
                throw new InvalidOperationException(
                    $"rowKey must match exactly one data row in the first column; found {found.Length}.");
            XElement selected = found[0];
            rowIndex = Array.IndexOf(rows, selected); // 0-based including header
            XElement cell = selected.Elements(W + "tc").ElementAt(targetColumn);
            XElement text = SimpleText44B(cell);
            if (text.Value != oldValue)
                throw new IOException("Target cell does not match expectedOldValue; no output written.");
            text.Value = newValue;
            text.SetAttributeValue(XNamespace.Xml + "space", "preserve");
        }
        var recheck = await NIRAFileIntegrity.HashAsync(source, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(recheck.Sha256, expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Source changed during editing; nothing written.");
        cancellationToken.ThrowIfCancellationRequested();
        PublishNew(destination, stage => Repackage(source, stage, updated), updated);
        string verification = VerifyPublishedDocument(destination, updated);
        VerifyPreservedOtherParts44B(source, destination);

        // Reopen and assert the exact target row/cell against the published file.
        using (ZipArchive savedZip = ZipFile.OpenRead(destination))
        {
            XDocument saved = OpenDocument(savedZip);
            XElement[] sameTable = saved.Root!.Element(W + "body")!.Elements(W + "tbl")
                .Where(t => t.Elements(W + "tr").FirstOrDefault() is XElement head &&
                    TableCells44B(head).SequenceEqual(headers, StringComparer.Ordinal)).ToArray();
            if (sameTable.Length != 1 || saved.Descendants(W + "tbl").Count() != tableCount ||
                sameTable[0].Elements(W + "tr").Count() != rowCount ||
                TableCells44B(sameTable[0].Elements(W + "tr").ElementAt(rowIndex))[targetColumn] != newValue)
                throw new InvalidDataException("Saved DOCX cell/row/table verification failed.");
        }
        var sourceAfter = await NIRAFileIntegrity.HashAsync(source, CancellationToken.None).ConfigureAwait(false);
        var destAfter = await NIRAFileIntegrity.HashAsync(destination, CancellationToken.None).ConfigureAwait(false);
        if (!string.Equals(sourceAfter.Sha256, expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Source changed externally; review newly created output.");
        return new NIRACapabilityHandlerResult
        {
            ChangedSystemState = true,
            Summary = "R44B: exactly one existing Word table cell updated in a verified NEW DOCX; original preserved.",
            Output = $"Source={source}\nSourceSHA256={sourceAfter.Sha256}\nDestination={destination}\n" +
                $"DestinationSHA256={destAfter.Sha256}\nTableCount={tableCount}\n" +
                $"TableRows={rowCount}\nRowIndex={rowIndex}\nRowKey={rowKey}\n" +
                $"ColumnHeader={columnHeader}\nOldValue={oldValue}\nNewValue={newValue}\n" +
                $"UpdatedCellJson={JsonSerializer.Serialize(newValue)}\n" +
                $"{verification}\nOtherPackagePartsPreserved=True\n" +
                "SourceUnchanged=True\nOverwroteExisting=False"
        };
    }
}

public sealed class NIRADocxTableCellUpdateCapabilityHandler : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor { get; } = new()
    {
        Id = NIRACapabilityIds.DocxTableCellUpdate,
        DefaultRisk = NIRACapabilityRisk.Modify,
        Description = "Change precisely one simple DOCX table data cell in a NEW copy. Requires a unique exact header, first-column row key, target column, expected old value, source SHA-256 and unused destination. Preserves original text styling, all unrelated Word parts, images, caption, header, footer and page layout.",
        Parameters = [
            NIRADocx.Param("source", true, "Existing source .docx; read-only."),
            NIRADocx.Param("destination", true, "NEW nonexisting .docx target."),
            NIRADocx.Param("headerTsv", true, "Exact unique header cells separated by TAB (JSON \\t)."),
            NIRADocx.Param("rowKey", true, "Exact unique value in the FIRST column identifying the data row."),
            NIRADocx.Param("columnHeader", true, "Exact header of the non-key cell to update."),
            NIRADocx.Param("expectedOldValue", true, "Exact existing cell text; fail if changed."),
            NIRADocx.Param("newValue", true, "Replacement nonempty text (one line, max 800 characters)."),
            NIRADocx.Param("expectedSourceSha256", true, "Fresh observed SHA-256 of source .docx.")
        ]
    };
    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest request) => NIRACapabilityRisk.Modify;
    public Task<NIRACapabilityHandlerResult> ExecuteAsync(NIRACapabilityRequest request,
        CancellationToken cancellationToken = default) =>
        NIRADocx.TableCellUpdate44BAsync(request, cancellationToken);
}
