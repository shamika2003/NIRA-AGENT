using System.IO.Compression;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace NIRAAgent.Capabilities;

// R45: one atomic, audited copy-on-write DOCX table transaction.
// A single execution can compare-and-swap cells, remove rows and append rows.
// Every selector and old value is checked before any publication occurs.
internal static partial class NIRADocx
{
    private sealed record TableEdit45(string RowKey, string Column, string OldValue,
        string NewValue, int RowIndex, int ColumnIndex);

    private static string[][] ParseTableLines45(string? input, int maximumLines, int columns, string label)
    {
        if (string.IsNullOrEmpty(input)) return [];
        string[] lines = input.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n').Split('\n');
        if (lines.Length is < 1 || lines.Length > maximumLines)
            throw new ArgumentException($"{label} must contain at most {maximumLines} lines.");
        string[][] parsed = lines.Select(line => line.Split('\t')).ToArray();
        if (parsed.Any(row => row.Length != columns || row.Any(cell =>
            string.IsNullOrWhiteSpace(cell) || cell.Length > 800 ||
            cell != cell.Trim() || cell.Contains('\r') || cell.Contains('\n'))))
            throw new ArgumentException($"{label} requires exactly {columns} nonempty TAB-separated values per line, maximum 800 characters per value.");
        try { foreach (string cell in parsed.SelectMany(row => row)) XmlConvert.VerifyXmlChars(cell); }
        catch (XmlException) { throw new ArgumentException($"{label} contains invalid XML characters."); }
        return parsed;
    }

    private static string[] ParseRowKeys45(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return [];
        string[] keys = raw.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n').Split('\n');
        if (keys.Length > 20 || keys.Any(key => string.IsNullOrWhiteSpace(key) ||
            key != key.Trim() || key.Length > 800 || key.Contains('\t')) ||
            keys.Distinct(StringComparer.Ordinal).Count() != keys.Length)
            throw new ArgumentException("deleteRowKeys requires 1-20 distinct exact first-column row keys.");
        try { foreach (string key in keys) XmlConvert.VerifyXmlChars(key); }
        catch (XmlException) { throw new ArgumentException("deleteRowKeys contains invalid XML characters."); }
        return keys;
    }

    // Match the existing odd/even row fill for surviving and appended rows after
    // deletion, leaving all other paragraph, run, cell and package properties intact.
    private static void ApplyStripe45(XElement row, XElement template)
    {
        XElement[] targetCells = row.Elements(W + "tc").ToArray();
        XElement[] sampleCells = template.Elements(W + "tc").ToArray();
        for (int i = 0; i < targetCells.Length; i++)
        {
            XElement? sampleShade = sampleCells[i].Element(W + "tcPr")?.Element(W + "shd");
            XElement? properties = targetCells[i].Element(W + "tcPr");
            if (sampleShade is null && properties is null) continue;
            if (properties is null)
            {
                properties = new XElement(W + "tcPr");
                targetCells[i].AddFirst(properties);
            }
            XElement? currentShade = properties.Element(W + "shd");
            if (sampleShade is null) currentShade?.Remove();
            else if (currentShade != null) currentShade.ReplaceWith(new XElement(sampleShade));
            else
            {
                // Word tcPr has schema-defined ordering. Insert shading before
                // later cell layout properties rather than blindly appending.
                string[] following = ["noWrap", "tcMar", "textDirection", "tcFitText",
                    "vAlign", "hideMark", "headers", "cellIns", "cellDel", "cellMerge"];
                XElement? successor = properties.Elements().FirstOrDefault(e =>
                    following.Contains(e.Name.LocalName, StringComparer.Ordinal));
                if (successor != null) successor.AddBeforeSelf(new XElement(sampleShade));
                else properties.Add(new XElement(sampleShade));
            }
        }
    }

    internal static async Task<NIRACapabilityHandlerResult> TableTransaction45Async(
        NIRACapabilityRequest request, CancellationToken cancellationToken)
    {
        string source = GetPath(request, "source");
        string destination = GetPath(request, "destination");
        NIRAFileIntegrity.Different(source, destination);
        string expected = NIRACapabilityArguments.RequireString(request, "expectedSourceSha256", 64);
        if (expected.Length != 64 || expected.Any(ch => !Uri.IsHexDigit(ch)))
            throw new ArgumentException("expectedSourceSha256 requires a freshly observed 64-character digest.");
        string[] headers = RequiredText(request, "headerTsv", 1000).Split('\t');
        if (headers.Length is < 2 or > 10 ||
            headers.Any(h => string.IsNullOrWhiteSpace(h) || h != h.Trim() ||
                h.Contains('\n') || h.Contains('\r')) ||
            headers.Distinct(StringComparer.Ordinal).Count() != headers.Length)
            throw new ArgumentException("headerTsv requires 2-10 exact unique TAB-separated headers.");
        string[][] updates = ParseTableLines45(
            NIRACapabilityArguments.GetOptionalRawString(request, "updatesTsv", 16000), 25, 4, "updatesTsv");
        string[] deletions = ParseRowKeys45(
            NIRACapabilityArguments.GetOptionalRawString(request, "deleteRowKeys", 12000));
        string[][] additions = ParseTableLines45(
            NIRACapabilityArguments.GetOptionalRawString(request, "appendRowsTsv", 16000), 20,
            headers.Length, "appendRowsTsv");
        if (updates.Length + deletions.Length + additions.Length == 0)
            throw new ArgumentException("Supply at least one cell update, row deletion or appended row.");
        if (updates.Any(entry => entry[2] == entry[3] || Array.IndexOf(headers, entry[1]) < 1))
            throw new ArgumentException("Updates must change the old value and target an exact NON-KEY column.");
        if (updates.Select(entry => entry[0] + "\u0000" + entry[1])
            .Distinct(StringComparer.Ordinal).Count() != updates.Length)
            throw new ArgumentException("Duplicate cell targets are not allowed.");
        if (updates.Any(entry => deletions.Contains(entry[0], StringComparer.Ordinal)))
            throw new ArgumentException("Cannot update a cell in a row that is also being deleted.");
        if (additions.Select(row => row[0]).Distinct(StringComparer.Ordinal).Count() != additions.Length)
            throw new ArgumentException("Appended first-column row keys must be distinct.");

        RequireValidSource(source);
        NIRAFileIntegrity.RejectLinkedPaths(destination);
        NIRAFileIntegrity.RequireNewDestination(destination);
        var originalHash = await NIRAFileIntegrity.HashAsync(source, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(originalHash.Sha256, expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Source hash is stale; transaction was not performed.");

        XDocument updated;
        int beforeRows, tableCount;
        string[][] intendedRows;
        var changes = new List<TableEdit45>();
        using (FileStream input = new(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (ZipArchive zip = new(input, ZipArchiveMode.Read))
        {
            updated = OpenDocument(zip);
            XElement body = updated.Root!.Element(W + "body")!;
            XElement[] tables = body.Elements(W + "tbl").Where(tbl =>
                tbl.Elements(W + "tr").FirstOrDefault() is XElement header &&
                TableCells44B(header).SequenceEqual(headers, StringComparer.Ordinal)).ToArray();
            if (tables.Length != 1)
                throw new InvalidOperationException($"Expected exactly one table matching header; found {tables.Length}.");
            XElement table = tables[0];
            CheckSimpleTable44B(table, headers.Length);
            XElement[] rows = table.Elements(W + "tr").ToArray();
            beforeRows = rows.Length;
            tableCount = body.Descendants(W + "tbl").Count();
            if (beforeRows - deletions.Length + additions.Length is < 2 or > 200)
                throw new InvalidOperationException("A transaction must retain a data row and 2-200 total table rows.");
            var keyMap = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 1; i < rows.Length; i++)
            {
                string key = CellText44B(rows[i].Elements(W + "tc").First());
                if (!keyMap.TryAdd(key, i))
                    throw new InvalidOperationException("First-column row keys are not unique; transaction refused.");
            }
            if (deletions.Any(key => !keyMap.ContainsKey(key)) ||
                updates.Any(entry => !keyMap.ContainsKey(entry[0])) ||
                additions.Any(row => keyMap.ContainsKey(row[0])))
                throw new InvalidOperationException("Row keys must exist uniquely for edits/deletes and be new for appends.");

            // Validate all old cells AND all template cells before changing XML.
            foreach (string[] entry in updates)
            {
                int index = keyMap[entry[0]];
                int column = Array.IndexOf(headers, entry[1]);
                XElement text = SimpleText44B(rows[index].Elements(W + "tc").ElementAt(column));
                if (text.Value != entry[2])
                    throw new IOException($"Stale old value for row '{entry[0]}' / column '{entry[1]}'.");
                changes.Add(new TableEdit45(entry[0], entry[1], entry[2], entry[3], index, column));
            }
            // Append copies from the original table's safe simple-text templates.
            foreach (XElement row in rows.Skip(1))
                foreach (XElement cell in row.Elements(W + "tc"))
                    _ = SimpleText44B(cell);

            foreach (TableEdit45 change in changes)
            {
                XElement text = SimpleText44B(rows[change.RowIndex].Elements(W + "tc")
                    .ElementAt(change.ColumnIndex));
                text.Value = change.NewValue;
                text.SetAttributeValue(XNamespace.Xml + "space", "preserve");
            }
            foreach (string key in deletions)
                rows[keyMap[key]].Remove();

            XElement[] surviving = table.Elements(W + "tr").ToArray();
            // Ensure that alternating row colors remain correct after middle-row deletions.
            if (deletions.Length > 0)
                for (int i = 1; i < surviving.Length; i++)
                {
                    int templateIndex = Enumerable.Range(1, rows.Length - 1)
                        .Where(index => index % 2 == i % 2)
                        .DefaultIfEmpty(1).First();
                    ApplyStripe45(surviving[i], rows[templateIndex]);
                }
            for (int i = 0; i < additions.Length; i++)
            {
                int finalIndex = surviving.Length + i;
                int templateIndex = Enumerable.Range(1, rows.Length - 1)
                    .Where(index => index % 2 == finalIndex % 2)
                    .DefaultIfEmpty(1).Last();
                XElement newRow = new(rows[templateIndex]);
                ApplyStripe45(newRow, rows[templateIndex]);
                XElement[] cells = newRow.Elements(W + "tc").ToArray();
                for (int c = 0; c < cells.Length; c++)
                {
                    XElement text = SimpleText44B(cells[c]);
                    text.Value = additions[i][c];
                    text.SetAttributeValue(XNamespace.Xml + "space", "preserve");
                }
                table.Add(newRow);
            }
            intendedRows = table.Elements(W + "tr").Select(TableCells44B).ToArray();
        }
        var recheck = await NIRAFileIntegrity.HashAsync(source, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(recheck.Sha256, expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Source changed while preparing transaction; nothing written.");
        cancellationToken.ThrowIfCancellationRequested();
        PublishNew(destination, staged => Repackage(source, staged, updated), updated);
        string verification = VerifyPublishedDocument(destination, updated);
        VerifyPreservedOtherParts44B(source, destination);
        using (ZipArchive savedZip = ZipFile.OpenRead(destination))
        {
            XDocument saved = OpenDocument(savedZip);
            XElement body = saved.Root!.Element(W + "body")!;
            XElement[] tables = body.Elements(W + "tbl").Where(tbl =>
                tbl.Elements(W + "tr").FirstOrDefault() is XElement head &&
                TableCells44B(head).SequenceEqual(headers, StringComparer.Ordinal)).ToArray();
            if (tables.Length != 1 || saved.Descendants(W + "tbl").Count() != tableCount)
                throw new InvalidDataException("Saved DOCX table identity changed.");
            string[][] observedRows = tables[0].Elements(W + "tr").Select(TableCells44B).ToArray();
            if (observedRows.Length != intendedRows.Length ||
                observedRows.Where((row, i) => !row.SequenceEqual(intendedRows[i], StringComparer.Ordinal)).Any())
                throw new InvalidDataException("Saved table rows differ from the verified intended transaction.");
        }
        var finalSource = await NIRAFileIntegrity.HashAsync(source, CancellationToken.None).ConfigureAwait(false);
        var finalDestination = await NIRAFileIntegrity.HashAsync(destination, CancellationToken.None).ConfigureAwait(false);
        if (!string.Equals(finalSource.Sha256, expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Source changed externally after the write; investigate before continuing.");
        return new NIRACapabilityHandlerResult
        {
            ChangedSystemState = true,
            Summary = "R45: one verified DOCX table transaction committed cell edits, row deletions and row appends together.",
            Output = $"Source={source}\nSourceSHA256={finalSource.Sha256}\nDestination={destination}\n" +
                $"DestinationSHA256={finalDestination.Sha256}\nTableCount={tableCount}\n" +
                $"TableRowsBefore={beforeRows}\nTableRowsAfter={intendedRows.Length}\n" +
                $"UpdatedCellsCount={changes.Count}\nDeletedRowsCount={deletions.Length}\n" +
                $"AppendedRowsCount={additions.Length}\n" +
                $"UpdatedCellsJson={JsonSerializer.Serialize(changes.Select(x => new { x.RowKey, x.Column, x.OldValue, x.NewValue }))}\n" +
                $"DeletedRowKeysJson={JsonSerializer.Serialize(deletions)}\n" +
                $"AppendedRowsJson={JsonSerializer.Serialize(additions)}\n" +
                $"LastRowCellsJson={JsonSerializer.Serialize(intendedRows[^1])}\n" +
                $"{verification}\nOtherPackagePartsPreserved=True\n" +
                "SourceUnchanged=True\nOverwroteExisting=False"
        };
    }
}

public sealed class NIRADocxTableTransactionCapabilityHandler : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor { get; } = new()
    {
        Id = NIRACapabilityIds.DocxTableTransaction,
        DefaultRisk = NIRACapabilityRisk.Modify,
        Description = "R45 one-call atomic Word table transaction: compare-and-swap 1-25 cells, delete up to 20 keyed data rows and append up to 20 rows, all in ONE NEW DOCX. At least one action required. Validates ALL old values and unique keys before writing; checks source SHA, preserves all other OpenXML parts, verifies saved result. Prefer over several sequential table tools.",
        Parameters = [
            NIRADocx.Param("source", true, "Existing, read-only DOCX source."),
            NIRADocx.Param("destination", true, "NEW unused destination .docx; never overwrite."),
            NIRADocx.Param("headerTsv", true, "Exact table header cells joined by real TABs."),
            NIRADocx.Param("expectedSourceSha256", true, "Freshly observed SHA-256 of source."),
            NIRADocx.Param("updatesTsv", false, "Optional 1-25 lines: rowKey TAB column TAB expectedOldValue TAB newValue."),
            NIRADocx.Param("deleteRowKeys", false, "Optional 1-20 exact first-column keys, one per line."),
            NIRADocx.Param("appendRowsTsv", false, "Optional 1-20 new TSV data rows; first-column keys must be new.")
        ]
    };
    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest request) => NIRACapabilityRisk.Modify;
    public Task<NIRACapabilityHandlerResult> ExecuteAsync(NIRACapabilityRequest request,
        CancellationToken cancellationToken = default) =>
        NIRADocx.TableTransaction45Async(request, cancellationToken);
}
