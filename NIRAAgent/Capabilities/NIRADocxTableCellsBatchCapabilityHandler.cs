using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;

namespace NIRAAgent.Capabilities;

// R44C: apply 1-25 distinct exact cell edits in ONE copy-on-write transaction.
// Every proposed cell must match its observed old value BEFORE anything is saved.
internal static partial class NIRADocx
{
    private sealed record CellChange44C(string RowKey, string ColumnHeader,
        string ExpectedOldValue, string NewValue, int RowIndex, int ColumnIndex);

    internal static async Task<NIRACapabilityHandlerResult> TableCellsBatch44CAsync(
        NIRACapabilityRequest request, CancellationToken cancellationToken)
    {
        string source = GetPath(request, "source");
        string destination = GetPath(request, "destination");
        NIRAFileIntegrity.Different(source, destination);
        string expected = NIRACapabilityArguments.RequireString(request, "expectedSourceSha256", 64);
        if (expected.Length != 64 || expected.Any(ch => !Uri.IsHexDigit(ch)))
            throw new ArgumentException("expectedSourceSha256 must be a fresh 64-character SHA-256.");
        string[] headers = RequiredText(request, "headerTsv", 1000).Split('\t');
        if (headers.Length is < 2 or > 10 ||
            headers.Any(x => string.IsNullOrWhiteSpace(x) || x.Contains('\n') || x.Contains('\r')) ||
            headers.Distinct(StringComparer.Ordinal).Count() != headers.Length)
            throw new ArgumentException("headerTsv requires 2-10 different exact TAB-separated headers.");
        string raw = RequiredText(request, "updatesTsv", 16000);
        string[] lines = raw.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n').Split('\n');
        if (lines.Length is < 1 or > 25)
            throw new ArgumentException("updatesTsv must contain 1-25 lines.");
        string[][] entries = lines.Select(line => line.Split('\t')).ToArray();
        if (entries.Any(parts => parts.Length != 4 || parts.Any(value =>
            string.IsNullOrWhiteSpace(value) || value.Length > 800 ||
            value != value.Trim() || value.Contains('\r') || value.Contains('\n'))))
            throw new ArgumentException("Each line must have four nonempty TAB-separated values (row key, column, old, new), max 800 characters each.");
        if (entries.Any(parts => parts[2] == parts[3]))
            throw new ArgumentException("Each new cell value must differ from its expected old value.");
        var identity = new HashSet<string>(StringComparer.Ordinal);
        foreach (string[] entry in entries)
        {
            if (!identity.Add(entry[0] + "\0" + entry[1]))
                throw new ArgumentException("Duplicate cell targets are not allowed in a batch.");
            if (Array.IndexOf(headers, entry[1]) < 1)
                throw new ArgumentException("Every column must name an exact non-key header cell.");
        }

        RequireValidSource(source);
        NIRAFileIntegrity.RejectLinkedPaths(destination);
        NIRAFileIntegrity.RequireNewDestination(destination);
        var before = await NIRAFileIntegrity.HashAsync(source, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(before.Sha256, expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Source SHA-256 is stale; no batch changes made.");

        XDocument updated;
        int rowCount, tableCount;
        var changes = new List<CellChange44C>();
        using (FileStream file = new(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (ZipArchive archive = new(file, ZipArchiveMode.Read))
        {
            updated = OpenDocument(archive);
            XElement body = updated.Root!.Element(W + "body")!;
            XElement[] matches = body.Elements(W + "tbl").Where(table =>
                table.Elements(W + "tr").FirstOrDefault() is XElement heading &&
                TableCells44B(heading).SequenceEqual(headers, StringComparer.Ordinal)).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException($"Expected one exact table header; found {matches.Length}.");
            XElement table = matches[0];
            CheckSimpleTable44B(table, headers.Length);
            XElement[] rows = table.Elements(W + "tr").ToArray();
            rowCount = rows.Length;
            tableCount = body.Descendants(W + "tbl").Count();

            // Validate all old values before the first in-memory mutation.
            foreach (string[] entry in entries)
            {
                int column = Array.IndexOf(headers, entry[1]);
                int[] rowMatches = Enumerable.Range(1, rows.Length - 1)
                    .Where(index => CellText44B(rows[index].Elements(W + "tc").First()) == entry[0])
                    .ToArray();
                if (rowMatches.Length != 1)
                    throw new InvalidOperationException(
                        $"Each row key must match exactly one data row; {entry[0]} matched {rowMatches.Length}.");
                XElement target = rows[rowMatches[0]].Elements(W + "tc").ElementAt(column);
                XElement text = SimpleText44B(target);
                if (text.Value != entry[2])
                    throw new IOException($"Expected old value mismatch for row '{entry[0]}', column '{entry[1]}'.");
                changes.Add(new CellChange44C(entry[0], entry[1], entry[2], entry[3],
                    rowMatches[0], column));
            }
            foreach (CellChange44C change in changes)
            {
                XElement target = rows[change.RowIndex].Elements(W + "tc").ElementAt(change.ColumnIndex);
                XElement text = SimpleText44B(target);
                text.Value = change.NewValue;
                text.SetAttributeValue(XNamespace.Xml + "space", "preserve");
            }
        }
        var recheck = await NIRAFileIntegrity.HashAsync(source, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(recheck.Sha256, expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Source changed before publishing; nothing written.");
        cancellationToken.ThrowIfCancellationRequested();
        PublishNew(destination, stage => Repackage(source, stage, updated), updated);
        string verified = VerifyPublishedDocument(destination, updated);
        VerifyPreservedOtherParts44B(source, destination);
        using (ZipArchive archive = ZipFile.OpenRead(destination))
        {
            XDocument saved = OpenDocument(archive);
            XElement[] matching = saved.Root!.Element(W + "body")!.Elements(W + "tbl").Where(table =>
                table.Elements(W + "tr").FirstOrDefault() is XElement first &&
                TableCells44B(first).SequenceEqual(headers, StringComparer.Ordinal)).ToArray();
            if (matching.Length != 1 || saved.Descendants(W + "tbl").Count() != tableCount ||
                matching[0].Elements(W + "tr").Count() != rowCount)
                throw new InvalidDataException("Batch output table structure is not preserved.");
            XElement[] savedRows = matching[0].Elements(W + "tr").ToArray();
            foreach (CellChange44C change in changes)
            {
                string observed = CellText44B(savedRows[change.RowIndex].Elements(W + "tc")
                    .ElementAt(change.ColumnIndex));
                if (observed != change.NewValue)
                    throw new InvalidDataException("The saved batch edit did not match intended data.");
            }
        }
        var sourceAfter = await NIRAFileIntegrity.HashAsync(source, CancellationToken.None).ConfigureAwait(false);
        var output = await NIRAFileIntegrity.HashAsync(destination, CancellationToken.None).ConfigureAwait(false);
        if (!string.Equals(sourceAfter.Sha256, expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Source was externally modified; inspect the new file.");
        return new NIRACapabilityHandlerResult {
            ChangedSystemState = true,
            Summary = "R44C: all requested Word table cell changes committed in ONE verified new DOCX copy.",
            Output = $"Source={source}\nSourceSHA256={sourceAfter.Sha256}\nDestination={destination}\n" +
                $"DestinationSHA256={output.Sha256}\nTableCount={tableCount}\nTableRows={rowCount}\n" +
                $"UpdatedCellsCount={changes.Count}\nUpdatedCellsJson={JsonSerializer.Serialize(changes.Select(change => new { change.RowKey, change.ColumnHeader, change.ExpectedOldValue, change.NewValue, change.RowIndex, change.ColumnIndex }))}\n" +
                $"{verified}\nOtherPackagePartsPreserved=True\nSourceUnchanged=True\nOverwroteExisting=False"
        };
    }
}

public sealed class NIRADocxTableCellsBatchCapabilityHandler : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor { get; } = new() {
        Id = NIRACapabilityIds.DocxTableCellsBatch,
        DefaultRisk = NIRACapabilityRisk.Modify,
        Description = "Update 1-25 distinct keyed table cells in ONE new DOCX copy, using exact TSV row keys, column headers, old values and new values. Entire batch rejects stale SHA, ambiguous keys, duplicate targets, mismatching old values or existing destination; verifies saved output and preserves other package parts.",
        Parameters = [
            NIRADocx.Param("source", true, "Existing, read-only .docx source."),
            NIRADocx.Param("destination", true, "New, nonexisting .docx destination."),
            NIRADocx.Param("headerTsv", true, "Exact table headers separated by TAB characters."),
            NIRADocx.Param("updatesTsv", true, "1-25 lines, each: rowKey TAB columnHeader TAB expectedOldValue TAB newValue."),
            NIRADocx.Param("expectedSourceSha256", true, "Freshly observed source SHA-256.")
        ]
    };
    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest request) => NIRACapabilityRisk.Modify;
    public Task<NIRACapabilityHandlerResult> ExecuteAsync(NIRACapabilityRequest request,
        CancellationToken cancellationToken = default) =>
        NIRADocx.TableCellsBatch44CAsync(request, cancellationToken);
}
