using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;

namespace NIRAAgent.Capabilities;

// R44D: remove 1-20 uniquely keyed data rows in a single, verified new document.
// No in-place updates; unrelated ZIP parts and surviving table rows are preserved.
internal static partial class NIRADocx
{
    internal static async Task<NIRACapabilityHandlerResult> TableRowsDelete44DAsync(
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
            throw new ArgumentException("headerTsv requires 2-10 unique exact TAB-separated headers.");
        string[] keys = RequiredText(request, "rowKeys", 12000)
            .Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n').Split('\n');
        if (keys.Length is < 1 or > 20 ||
            keys.Any(key => string.IsNullOrWhiteSpace(key) || key != key.Trim() ||
                key.Length > 800 || key.Contains('\t')) ||
            keys.Distinct(StringComparer.Ordinal).Count() != keys.Length)
            throw new ArgumentException("rowKeys must contain 1-20 different exact first-column keys, one per line.");

        RequireValidSource(source);
        NIRAFileIntegrity.RejectLinkedPaths(destination);
        NIRAFileIntegrity.RequireNewDestination(destination);
        var before = await NIRAFileIntegrity.HashAsync(source, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(before.Sha256, expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Source SHA-256 is stale; no rows removed.");

        XDocument updated;
        int rowsBefore, tableCount;
        string[][] survivors;
        using (FileStream file = new(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (ZipArchive archive = new(file, ZipArchiveMode.Read))
        {
            updated = OpenDocument(archive);
            XElement body = updated.Root!.Element(W + "body")!;
            XElement[] matching = body.Elements(W + "tbl").Where(table =>
                table.Elements(W + "tr").FirstOrDefault() is XElement head &&
                TableCells44B(head).SequenceEqual(headers, StringComparer.Ordinal)).ToArray();
            if (matching.Length != 1)
                throw new InvalidOperationException($"Expected one exact table header, found {matching.Length}.");
            XElement table = matching[0];
            CheckSimpleTable44B(table, headers.Length);
            XElement[] rows = table.Elements(W + "tr").ToArray();
            rowsBefore = rows.Length;
            tableCount = body.Descendants(W + "tbl").Count();
            if (rowsBefore - keys.Length < 2)
                throw new InvalidOperationException("Deletion would remove the last data row; nothing changed.");
            var indices = new HashSet<int>();
            foreach (string key in keys)
            {
                int[] found = Enumerable.Range(1, rows.Length - 1)
                    .Where(index => CellText44B(rows[index].Elements(W + "tc").First()) == key)
                    .ToArray();
                if (found.Length != 1)
                    throw new InvalidOperationException($"Row key '{key}' matched {found.Length} rows, not exactly one.");
                indices.Add(found[0]);
            }
            survivors = rows.Where((row, index) => !indices.Contains(index))
                .Select(TableCells44B).ToArray();
            foreach (int index in indices.OrderByDescending(i => i))
                rows[index].Remove();
        }
        var recheck = await NIRAFileIntegrity.HashAsync(source, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(recheck.Sha256, expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Source changed during deletion preparation; nothing written.");
        cancellationToken.ThrowIfCancellationRequested();
        PublishNew(destination, stage => Repackage(source, stage, updated), updated);
        string verified = VerifyPublishedDocument(destination, updated);
        VerifyPreservedOtherParts44B(source, destination);
        using (ZipArchive archive = ZipFile.OpenRead(destination))
        {
            XDocument saved = OpenDocument(archive);
            XElement[] tables = saved.Root!.Element(W + "body")!.Elements(W + "tbl").Where(table =>
                table.Elements(W + "tr").FirstOrDefault() is XElement head &&
                TableCells44B(head).SequenceEqual(headers, StringComparer.Ordinal)).ToArray();
            if (tables.Length != 1 || saved.Descendants(W + "tbl").Count() != tableCount)
                throw new InvalidDataException("Saved table structure was not preserved.");
            string[][] savedRows = tables[0].Elements(W + "tr").Select(TableCells44B).ToArray();
            if (savedRows.Length != survivors.Length ||
                savedRows.Where((row, index) => !row.SequenceEqual(survivors[index], StringComparer.Ordinal)).Any())
                throw new InvalidDataException("Saved rows differ from the expected surviving rows.");
        }
        var sourceAfter = await NIRAFileIntegrity.HashAsync(source, CancellationToken.None).ConfigureAwait(false);
        var output = await NIRAFileIntegrity.HashAsync(destination, CancellationToken.None).ConfigureAwait(false);
        if (!string.Equals(sourceAfter.Sha256, expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Source changed externally; inspect new destination.");
        return new NIRACapabilityHandlerResult {
            ChangedSystemState = true,
            Summary = "R44D: selected Word table data rows deleted in ONE verified NEW DOCX copy; source preserved.",
            Output = $"Source={source}\nSourceSHA256={sourceAfter.Sha256}\nDestination={destination}\n" +
                $"DestinationSHA256={output.Sha256}\nTableCount={tableCount}\nTableRowsBefore={rowsBefore}\n" +
                $"TableRowsDeleted={keys.Length}\nTableRowsAfter={survivors.Length}\n" +
                $"DeletedRowKeysJson={JsonSerializer.Serialize(keys)}\n{verified}\n" +
                "OtherPackagePartsPreserved=True\nSourceUnchanged=True\nOverwroteExisting=False"
        };
    }
}

public sealed class NIRADocxTableRowsDeleteCapabilityHandler : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor { get; } = new() {
        Id = NIRACapabilityIds.DocxTableRowsDelete,
        DefaultRisk = NIRACapabilityRisk.Modify,
        Description = "Remove 1-20 exact, uniquely identified simple table data rows from one existing table in a new DOCX. Preserves header, surviving rows, styling, images, footer and all other package parts. Requires fresh source SHA and unused destination; refuses ambiguous keys and deleting all data rows.",
        Parameters = [
            NIRADocx.Param("source", true, "Existing, read-only source .docx."),
            NIRADocx.Param("destination", true, "New nonexisting .docx destination."),
            NIRADocx.Param("headerTsv", true, "Exact table headers joined by TAB characters."),
            NIRADocx.Param("rowKeys", true, "1-20 exact first-column data row keys, one per line."),
            NIRADocx.Param("expectedSourceSha256", true, "Freshly observed source SHA-256.")
        ]
    };
    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest request) => NIRACapabilityRisk.Modify;
    public Task<NIRACapabilityHandlerResult> ExecuteAsync(NIRACapabilityRequest request,
        CancellationToken cancellationToken = default) =>
        NIRADocx.TableRowsDelete44DAsync(request, cancellationToken);
}
