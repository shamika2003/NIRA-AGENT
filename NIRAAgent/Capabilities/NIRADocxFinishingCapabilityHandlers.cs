using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace NIRAAgent.Capabilities;

// R47: one bounded native Word finishing release: templates, deterministic
// multi-document batch, and structured reports. No Word automation or network.
internal static partial class NIRADocx
{
    private static readonly Regex Marker47 = new(@"\{\{([A-Za-z][A-Za-z0-9_]{0,39})\}\}",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private const int MaxTemplateValue47 = 1000;
    private const int MaxBatchJobs47 = 12;

    private static bool IsEditableXmlPart47(string path) =>
        path == DocumentPart ||
        (path.StartsWith("word/header", StringComparison.Ordinal) && path.EndsWith(".xml", StringComparison.Ordinal)) ||
        (path.StartsWith("word/footer", StringComparison.Ordinal) && path.EndsWith(".xml", StringComparison.Ordinal));

    private static Dictionary<string, XDocument> ReadEditableParts47(ZipArchive archive)
    {
        XDocument main = OpenDocument(archive);
        Dictionary<string, XDocument> parts = new(StringComparer.Ordinal)
        { [DocumentPart] = main };
        foreach (var entry in archive.Entries.Where(e => e.FullName != DocumentPart && IsEditableXmlPart47(e.FullName)))
        {
            using Stream input = entry.Open();
            XDocument xml = LoadXml(input);
            if (xml.Root?.Name != W + "hdr" && xml.Root?.Name != W + "ftr")
                throw new InvalidDataException("Unsupported header/footer XML part.");
            parts.Add(entry.FullName, xml);
        }
        return parts;
    }

    private static string[] FindTokens47(IReadOnlyDictionary<string, XDocument> parts)
    {
        HashSet<string> tokens = new(StringComparer.Ordinal);
        foreach (XDocument xml in parts.Values)
        {
            foreach (XElement p in xml.Descendants(W + "p"))
            {
                string joined = string.Concat(p.Descendants(W + "t").Select(t => t.Value));
                foreach (Match match in Marker47.Matches(joined))
                {
                    bool single = p.Descendants(W + "t").Any(t => t.Value.Contains(match.Value, StringComparison.Ordinal));
                    if (!single)
                        throw new InvalidOperationException($"Template placeholder {match.Value} spans multiple Word runs; join it into one run before using R47.");
                    tokens.Add(match.Groups[1].Value);
                }
                // Never silently accept a typo such as {{ CLIENT }} or {{CLIENT.
                if (joined.Contains("{{", StringComparison.Ordinal) &&
                    Marker47.Replace(joined, "").Contains("{{", StringComparison.Ordinal))
                    throw new InvalidOperationException("Malformed template placeholder; use {{KEY}} in a single Word text run.");
            }
        }
        return tokens.OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }

    private static Dictionary<string, string> ParseValues47(JsonElement json, string label)
    {
        if (json.ValueKind != JsonValueKind.Object) throw new ArgumentException(label + " must be a JSON object.");
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        foreach (var property in json.EnumerateObject())
        {
            if (values.Count >= 40 || !Regex.IsMatch(property.Name, "^[A-Za-z][A-Za-z0-9_]{0,39}$", RegexOptions.CultureInvariant) ||
                property.Value.ValueKind != JsonValueKind.String)
                throw new ArgumentException("Template values require 1-40 valid keys with plain text string values.");
            string value = property.Value.GetString()!;
            if (value.Length > MaxTemplateValue47 || value.Contains('\n') || value.Contains('\r') ||
                value.Contains('\t') || value.Contains("{{", StringComparison.Ordinal))
                throw new ArgumentException("Template values must be one-line text of at most 1000 characters, without placeholder syntax.");
            XmlConvert.VerifyXmlChars(value);
            if (!values.TryAdd(property.Name, value))
                throw new ArgumentException("Repeated placeholder key.");
        }
        if (values.Count == 0) throw new ArgumentException("At least one template value is required.");
        return values;
    }

    private static Dictionary<string, string> ReadValuesJson47(NIRACapabilityRequest request)
    {
        string text = RequiredText(request, "valuesJson", 16000);
        using JsonDocument data = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 5 });
        return ParseValues47(data.RootElement, "valuesJson");
    }

    private static Dictionary<string, XDocument> FilledParts47(
        IReadOnlyDictionary<string, XDocument> source, IReadOnlyList<string> required,
        Dictionary<string, string> values, out int occurrences)
    {
        if (!required.OrderBy(s => s, StringComparer.Ordinal).SequenceEqual(values.Keys.OrderBy(s => s, StringComparer.Ordinal)))
            throw new InvalidOperationException("Values must provide exactly the placeholders inspected in the template. Nothing written.");
        Dictionary<string, XDocument> updated = source.ToDictionary(p => p.Key,
            p => new XDocument(p.Value), StringComparer.Ordinal);
        // C# forbids capturing an out parameter in a lambda (CS1628).
        // Count matches in a local and assign the out parameter after replacement.
        int replacementCount = 0;
        foreach (XDocument xml in updated.Values)
            foreach (XElement element in xml.Descendants(W + "t"))
            {
                string old = element.Value;
                string changed = Marker47.Replace(old, match =>
                {
                    replacementCount++;
                    return values[match.Groups[1].Value];
                });
                if (changed != old) element.Value = changed;
            }
        occurrences = replacementCount;
        if (occurrences == 0 || FindTokens47(updated).Length != 0)
            throw new InvalidOperationException("Template was not fully populated.");
        return updated;
    }

    private static void CopyPackageParts47(string source, string destination,
        IReadOnlyDictionary<string, XDocument> edited)
    {
        using ZipArchive input = ZipFile.OpenRead(source);
        ValidatePackage(input);
        using FileStream target = new(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using (ZipArchive output = new(target, ZipArchiveMode.Create, leaveOpen: true))
            foreach (ZipArchiveEntry item in input.Entries)
            {
                if (edited.TryGetValue(item.FullName, out XDocument? replacement))
                    WriteXml(output, item.FullName, replacement);
                else
                {
                    ZipArchiveEntry copied = output.CreateEntry(item.FullName, CompressionLevel.Optimal);
                    using Stream from = item.Open();
                    using Stream to = copied.Open();
                    from.CopyTo(to);
                }
            }
        target.Flush(true);
    }

    private static void VerifyTemplateFile47(string source, string output,
        IReadOnlyDictionary<string, XDocument> expected)
    {
        using ZipArchive original = ZipFile.OpenRead(source);
        using ZipArchive candidate = ZipFile.OpenRead(output);
        ValidatePackage(original);
        ValidatePackage(candidate);
        if (original.Entries.Count != candidate.Entries.Count)
            throw new InvalidDataException("DOCX entry count changed unexpectedly.");
        foreach (ZipArchiveEntry entry in original.Entries)
        {
            ZipArchiveEntry? savedEntry = candidate.GetEntry(entry.FullName);
            if (savedEntry == null) throw new InvalidDataException("Missing package part: " + entry.FullName);
            if (expected.TryGetValue(entry.FullName, out XDocument? intended))
            {
                using Stream stream = savedEntry.Open();
                XDocument actual = LoadXml(stream);
                if (!XNode.DeepEquals(intended.Root, actual.Root))
                    throw new InvalidDataException("Saved template XML failed exact content verification.");
            }
            else
            {
                if (entry.Length != savedEntry.Length)
                    throw new InvalidDataException("Unrelated DOCX package part changed.");
                using Stream a = entry.Open();
                using Stream b = savedEntry.Open();
                if (!SHA256.HashData(a).AsSpan().SequenceEqual(SHA256.HashData(b)))
                    throw new InvalidDataException("Unrelated DOCX part bytes changed.");
            }
        }
        if (FindTokens47(ReadEditableParts47(candidate)).Length != 0)
            throw new InvalidDataException("Saved document still contains template placeholders.");
    }

    private static async Task<string> SourceHash47(string source, string expected, CancellationToken ct)
    {
        if (expected.Length != 64 || expected.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("A fresh expectedSourceSha256 is required.");
        var actual = await NIRAFileIntegrity.HashAsync(source, ct).ConfigureAwait(false);
        if (!string.Equals(actual.Sha256, expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Template SHA-256 changed; no output written.");
        return actual.Sha256;
    }

    internal static async Task<NIRACapabilityHandlerResult> TemplateInspect47Async(
        NIRACapabilityRequest request, CancellationToken ct)
    {
        string source = GetPath(request, "path");
        RequireValidSource(source);
        var observed = await NIRAFileIntegrity.HashAsync(source, ct).ConfigureAwait(false);
        string[] tokens;
        using (ZipArchive archive = ZipFile.OpenRead(source))
            tokens = FindTokens47(ReadEditableParts47(archive));
        return new NIRACapabilityHandlerResult
        {
            ChangedSystemState = false,
            Summary = "Read-only R47 DOCX placeholder inspection (main document, tables, headers and footers).",
            Output = $"Path={source}\nSHA256={observed.Sha256}\nPlaceholdersJson={JsonSerializer.Serialize(tokens)}\n" +
                     $"PlaceholderCount={tokens.Length}\nNoFilesModified=True"
        };
    }

    internal static async Task<NIRACapabilityHandlerResult> TemplateFill47Async(
        NIRACapabilityRequest request, CancellationToken ct)
    {
        string source = GetPath(request, "source");
        string destination = GetPath(request, "destination");
        string expected = NIRACapabilityArguments.RequireString(request, "expectedSourceSha256", 64);
        NIRAFileIntegrity.Different(source, destination);
        RequireValidSource(source);
        NIRAFileIntegrity.RejectLinkedPaths(destination);
        NIRAFileIntegrity.RequireNewDestination(destination);
        Dictionary<string, string> values = ReadValuesJson47(request);
        await SourceHash47(source, expected, ct).ConfigureAwait(false);
        Dictionary<string, XDocument> edited;
        int replaced;
        using (ZipArchive archive = ZipFile.OpenRead(source))
        {
            var original = ReadEditableParts47(archive);
            edited = FilledParts47(original, FindTokens47(original), values, out replaced);
        }
        await SourceHash47(source, expected, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        // Private stage, followed by exact saved XML and unrelated ZIP-byte audit.
        string stage = destination + ".nira-r47-stage-" + Guid.NewGuid().ToString("N") + ".tmp";
        bool published = false;
        string? publishedHash = null;
        try
        {
            CopyPackageParts47(source, stage, edited);
            VerifyTemplateFile47(source, stage, edited);
            await SourceHash47(source, expected, ct).ConfigureAwait(false);
            NIRAFileIntegrity.RequireNewDestination(destination);
            publishedHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(stage)));
            File.Move(stage, destination, overwrite: false);
            published = true;
            VerifyTemplateFile47(source, destination, edited);
        }
        catch
        {
            if (published && File.Exists(destination) && publishedHash != null &&
                string.Equals(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(destination))),
                    publishedHash, StringComparison.OrdinalIgnoreCase))
                File.Delete(destination);
            throw;
        }
        finally { if (File.Exists(stage)) File.Delete(stage); }
        var after = await NIRAFileIntegrity.HashAsync(source, CancellationToken.None).ConfigureAwait(false);
        var output = await NIRAFileIntegrity.HashAsync(destination, CancellationToken.None).ConfigureAwait(false);
        if (!string.Equals(after.Sha256, expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Source externally changed after publication. Review the output.");
        return new NIRACapabilityHandlerResult
        {
            ChangedSystemState = true,
            Summary = "R47 template populated as one NEW DOCX, saved XML and unrelated package parts verified.",
            Output = $"Source={source}\nSourceSHA256={after.Sha256}\nDestination={destination}\nDestinationSHA256={output.Sha256}\n" +
                $"ReplacedPlaceholders={replaced}\nPlaceholderKeys={values.Count}\nHeaderFooterAndTablesPreserved=True\n" +
                "PostWriteVerified=True\nSourceUnchanged=True\nOverwroteExisting=False\nVisualPaginationVerified=False"
        };
    }

    private sealed record BatchItem47(string FileName, Dictionary<string, string> Values);

    private static List<BatchItem47> ParseJobs47(string jobsJson)
    {
        using JsonDocument root = JsonDocument.Parse(jobsJson, new JsonDocumentOptions { MaxDepth = 8 });
        if (root.RootElement.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("jobsJson must be an array.");
        List<BatchItem47> jobs = [];
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement job in root.RootElement.EnumerateArray())
        {
            if (jobs.Count >= MaxBatchJobs47 || job.ValueKind != JsonValueKind.Object ||
                !job.TryGetProperty("fileName", out JsonElement name) || name.ValueKind != JsonValueKind.String ||
                !job.TryGetProperty("values", out JsonElement values))
                throw new ArgumentException("Batch requires 1-12 {fileName,values} records.");
            string file = name.GetString() ?? "";
            if (file.Length > 90 || !Regex.IsMatch(file, "^[A-Za-z0-9][A-Za-z0-9._-]*\\.docx$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
                file.Contains("..", StringComparison.Ordinal) || !names.Add(file))
                throw new ArgumentException("Batch names must be unique simple .docx filenames without path separators.");
            jobs.Add(new BatchItem47(file, ParseValues47(values, "job.values")));
        }
        if (jobs.Count == 0) throw new ArgumentException("At least one batch record is needed.");
        return jobs;
    }

    internal static async Task<NIRACapabilityHandlerResult> TemplateBatch47Async(
        NIRACapabilityRequest request, CancellationToken ct)
    {
        string source = GetPath(request, "source");
        string directory = NIRAFileIntegrity.PathArgument(request, "destinationDirectory");
        string expected = NIRACapabilityArguments.RequireString(request, "expectedSourceSha256", 64);
        string raw = RequiredText(request, "jobsJson", 30000);
        List<BatchItem47> jobs = ParseJobs47(raw);
        RequireValidSource(source);
        NIRAFileIntegrity.RejectLinkedPaths(directory);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("Output directory must already exist.");
        string fullDirectory = Path.GetFullPath(directory);
        string[] outputs = jobs.Select(j => Path.Combine(fullDirectory, j.FileName)).ToArray();
        foreach (string output in outputs)
        {
            NIRAFileIntegrity.Different(source, output);
            NIRAFileIntegrity.RejectLinkedPaths(output);
            NIRAFileIntegrity.RequireNewDestination(output);
        }
        await SourceHash47(source, expected, ct).ConfigureAwait(false);
        Dictionary<string, XDocument> original;
        string[] keys;
        using (ZipArchive zip = ZipFile.OpenRead(source))
        {
            original = ReadEditableParts47(zip);
            keys = FindTokens47(original);
        }
        List<(string Stage, string Output, Dictionary<string, XDocument> Parts, int Count)> staged = [];
        List<(string Path, string Sha)> published = [];
        try
        {
            foreach (var (job, index) in jobs.Select((job, index) => (job, index)))
            {
                ct.ThrowIfCancellationRequested();
                string output = outputs[index];
                var filled = FilledParts47(original, keys, job.Values, out int count);
                string stage = output + ".nira-r47-stage-" + Guid.NewGuid().ToString("N") + ".tmp";
                staged.Add((stage, output, filled, count));
                CopyPackageParts47(source, stage, filled);
                VerifyTemplateFile47(source, stage, filled);
            }
            await SourceHash47(source, expected, ct).ConfigureAwait(false);
            // Preflight everything once more, then create-only publication.
            foreach (string output in outputs) NIRAFileIntegrity.RequireNewDestination(output);
            ct.ThrowIfCancellationRequested();
            foreach (var item in staged)
            {
                string sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(item.Stage)));
                File.Move(item.Stage, item.Output, overwrite: false);
                published.Add((item.Output, sha));
                VerifyTemplateFile47(source, item.Output, item.Parts);
            }
            await SourceHash47(source, expected, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Roll back only files created by THIS batch, and only if bytes
            // still match our own publication. Never delete an external edit.
            foreach (var item in published)
            {
                if (File.Exists(item.Path) &&
                    string.Equals(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(item.Path))), item.Sha,
                        StringComparison.OrdinalIgnoreCase))
                    File.Delete(item.Path);
            }
            throw;
        }
        finally
        {
            foreach (var item in staged)
                if (File.Exists(item.Stage)) File.Delete(item.Stage);
        }
        string files = JsonSerializer.Serialize(published.Select(p => new { path = p.Path, sha256 = p.Sha }));
        return new NIRACapabilityHandlerResult
        {
            ChangedSystemState = true,
            Summary = $"R47 batch generated and verified {jobs.Count} genuine DOCX documents with one preflight and guarded publication.",
            Output = $"Source={source}\nSourceSHA256={expected}\nDestinationDirectory={fullDirectory}\n" +
                $"CreatedDocuments={jobs.Count}\nTotalSubstitutions={staged.Sum(x => x.Count)}\nOutputFilesJson={files}\n" +
                "PostWriteVerified=True\nSourceUnchanged=True\nOverwroteExisting=False\nVisualPaginationVerified=False"
        };
    }

    private static XElement ReportTable47(string tsv)
    {
        string[] lines = tsv.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n').Split('\n');
        if (lines.Length is < 2 or > 40) throw new ArgumentException("Report tables require 2-40 TSV rows.");
        string[][] rows = lines.Select(x => x.Split('\t')).ToArray();
        int columns = rows[0].Length;
        if (columns is < 2 or > 8 || rows.Any(r => r.Length != columns ||
            r.Any(c => c.Length is < 1 or > 250 || c.Contains('\n') || c.Contains('\r'))))
            throw new ArgumentException("Report table requires 2-8 uniform columns, nonempty cells <=250 characters.");
        XElement table = new(W + "tbl",
            new XElement(W + "tblPr",
                new XElement(W + "tblW", new XAttribute(W + "w", "5000"), new XAttribute(W + "type", "pct")),
                new XElement(W + "tblBorders", new[] { "top", "left", "bottom", "right", "insideH", "insideV" }
                    .Select(edge => new XElement(W + edge, new XAttribute(W + "val", "single"),
                        new XAttribute(W + "sz", "6"), new XAttribute(W + "color", "D4E2F0"))))),
            new XElement(W + "tblGrid", Enumerable.Range(0, columns)
                .Select(_ => new XElement(W + "gridCol", new XAttribute(W + "w", 9000 / columns)))));
        for (int i = 0; i < rows.Length; i++)
        {
            XElement tr = new(W + "tr");
            if (i == 0) tr.Add(new XElement(W + "trPr", new XElement(W + "tblHeader", new XAttribute(W + "val", "true"))));
            foreach (string content in rows[i])
            {
                XElement? rpr = i == 0 ? new XElement(W + "rPr", new XElement(W + "b"),
                    new XElement(W + "color", new XAttribute(W + "val", "FFFFFF"))) : null;
                XElement cell = new(W + "tc");
                if (i == 0) cell.Add(new XElement(W + "tcPr", new XElement(W + "shd",
                    new XAttribute(W + "fill", "17365D"))));
                else if (i % 2 == 0) cell.Add(new XElement(W + "tcPr", new XElement(W + "shd",
                    new XAttribute(W + "fill", "EAF2F8"))));
                cell.Add(new XElement(W + "p", new XElement(W + "r", rpr,
                    new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), content))));
                tr.Add(cell);
            }
            table.Add(tr);
        }
        return table;
    }

    internal static async Task<NIRACapabilityHandlerResult> ReportCreate47Async(
        NIRACapabilityRequest request, CancellationToken ct)
    {
        string path = GetPath(request, "path");
        string title = RequiredText(request, "title", 160).Trim();
        string sections = RequiredText(request, "sectionsJson", 26000);
        NIRAFileIntegrity.RejectLinkedPaths(path);
        NIRAFileIntegrity.RequireNewDestination(path);
        if (title.Contains('\n') || title.Contains('\r')) throw new ArgumentException("Report title must be one line.");
        using JsonDocument json = JsonDocument.Parse(sections, new JsonDocumentOptions { MaxDepth = 6 });
        if (json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.GetArrayLength() is < 1 or > 12)
            throw new ArgumentException("Report sectionsJson requires 1-12 sections.");
        XElement body = new(W + "body");
        XElement titleParagraph = Paragraph(title, 1);
        body.Add(titleParagraph);
        var titleRun = titleParagraph.Element(W + "r")!;
        titleRun.AddFirst(new XElement(W + "rPr", new XElement(W + "b"),
            new XElement(W + "color", new XAttribute(W + "val", "17365D")),
            new XElement(W + "sz", new XAttribute(W + "val", "44"))));
        titleParagraph.Element(W + "pPr")!.Add(new XElement(W + "jc", new XAttribute(W + "val", "center")));
        int tableCount = 0;
        foreach (JsonElement section in json.RootElement.EnumerateArray())
        {
            if (section.ValueKind != JsonValueKind.Object ||
                !section.TryGetProperty("heading", out JsonElement h) || h.ValueKind != JsonValueKind.String ||
                !section.TryGetProperty("body", out JsonElement b) || b.ValueKind != JsonValueKind.String)
                throw new ArgumentException("Each report section needs heading and body strings.");
            string heading = h.GetString() ?? "", content = b.GetString() ?? "";
            if (heading.Length is < 1 or > 140 || content.Length is < 1 or > 3000 ||
                heading.Contains('\n') || heading.Contains('\r'))
                throw new ArgumentException("Report headings must be 1-140 characters; body 1-3000.");
            XmlConvert.VerifyXmlChars(heading); XmlConvert.VerifyXmlChars(content);
            XElement headingNode = Paragraph(heading, 2);
            headingNode.Element(W + "pPr")!.Add(new XElement(W + "spacing", new XAttribute(W + "before", "220"),
                new XAttribute(W + "after", "120")));
            body.Add(headingNode);
            foreach (string paragraph in content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(paragraph)) continue;
                XElement text = Paragraph(paragraph);
                text.AddFirst(new XElement(W + "pPr", new XElement(W + "spacing", new XAttribute(W + "after", "100"),
                    new XAttribute(W + "line", "276"), new XAttribute(W + "lineRule", "auto"))));
                body.Add(text);
            }
            if (section.TryGetProperty("tableTsv", out JsonElement t) && t.ValueKind != JsonValueKind.Null)
            {
                if (t.ValueKind != JsonValueKind.String || t.GetString()!.Length > 8000)
                    throw new ArgumentException("tableTsv must be at most 8000 characters.");
                XmlConvert.VerifyXmlChars(t.GetString()!);
                body.Add(ReportTable47(t.GetString()!));
                tableCount++;
            }
        }
        body.Add(new XElement(W + "sectPr",
            new XElement(W + "pgSz", new XAttribute(W + "w", 11906), new XAttribute(W + "h", 16838)),
            new XElement(W + "pgMar", new XAttribute(W + "top", 1134), new XAttribute(W + "right", 1134),
                new XAttribute(W + "bottom", 1134), new XAttribute(W + "left", 1134))));
        XDocument result = new(new XElement(W + "document", new XAttribute(XNamespace.Xmlns + "w", W), body));
        ct.ThrowIfCancellationRequested();
        PublishNew(path, stage => NewPackage(stage, result), result);
        string verification = VerifyPublishedDocument(path, result);
        var written = await NIRAFileIntegrity.HashAsync(path, CancellationToken.None).ConfigureAwait(false);
        return new NIRACapabilityHandlerResult
        {
            ChangedSystemState = true,
            Summary = "R47 multi-section professional report generated as a genuine new DOCX with styled tables and A4 portrait layout.",
            Output = $"Destination={path}\nDestinationSHA256={written.Sha256}\nReportSections={json.RootElement.GetArrayLength()}\n" +
                $"ReportTables={tableCount}\nPaper=A4\nOrientation=portrait\n{verification}\nOverwroteExisting=False\nVisualPaginationVerified=False"
        };
    }
}

public sealed class NIRADocxTemplateInspectCapabilityHandler : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor { get; } = new()
    {
        Id = NIRACapabilityIds.DocxTemplateInspect, DefaultRisk = NIRACapabilityRisk.Observe,
        Description = "Read real DOCX template placeholders from body, tables, headers and footers with fresh SHA-256; no modification.",
        Parameters = [ NIRADocx.Param("path", true, "Existing .docx template file") ]
    };
    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest request) => NIRACapabilityRisk.Observe;
    public Task<NIRACapabilityHandlerResult> ExecuteAsync(NIRACapabilityRequest request,
        CancellationToken cancellationToken = default) => NIRADocx.TemplateInspect47Async(request, cancellationToken);
}

public sealed class NIRADocxTemplateFillCapabilityHandler : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor { get; } = new()
    {
        Id = NIRACapabilityIds.DocxTemplateFill, DefaultRisk = NIRACapabilityRisk.Modify,
        Description = "Fill {{KEY}} placeholders in an existing DOCX template (including tables/header/footer) into ONE new file. Strict mapping, fresh SHA, no overwrite, verified bytes.",
        Parameters = [ NIRADocx.Param("source", true, "Existing .docx template"),
            NIRADocx.Param("destination", true, "New unused .docx file"),
            NIRADocx.Param("expectedSourceSha256", true, "Fresh SHA-256 from template.inspect"),
            NIRADocx.Param("valuesJson", true, "JSON object mapping exact placeholder keys to one-line text values. Example: {\"CLIENT\":\"Asha\"}") ]
    };
    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest request) => NIRACapabilityRisk.Modify;
    public Task<NIRACapabilityHandlerResult> ExecuteAsync(NIRACapabilityRequest request,
        CancellationToken cancellationToken = default) => NIRADocx.TemplateFill47Async(request, cancellationToken);
}

public sealed class NIRADocxTemplateBatchCapabilityHandler : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor { get; } = new()
    {
        Id = NIRACapabilityIds.DocxTemplateBatch, DefaultRisk = NIRACapabilityRisk.Modify,
        Description = "Produce 1-12 verified DOCX files from one template in an explicitly authorized EXISTING destinationDirectory. Validate all outputs before write. Names cannot contain paths. Rollback only unchanged files created by this operation.",
        Parameters = [ NIRADocx.Param("source", true, "Existing .docx template"),
            NIRADocx.Param("destinationDirectory", true, "EXISTING output folder; all generated files remain inside this one authorized folder."),
            NIRADocx.Param("expectedSourceSha256", true, "Fresh template SHA-256"),
            NIRADocx.Param("jobsJson", true, "JSON array up to 12: [{\"fileName\":\"client-01.docx\",\"values\":{\"CLIENT\":\"Asha\"}}]") ]
    };
    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest request) => NIRACapabilityRisk.Modify;
    public Task<NIRACapabilityHandlerResult> ExecuteAsync(NIRACapabilityRequest request,
        CancellationToken cancellationToken = default) => NIRADocx.TemplateBatch47Async(request, cancellationToken);
}

public sealed class NIRADocxReportCreateCapabilityHandler : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor { get; } = new()
    {
        Id = NIRACapabilityIds.DocxReportCreate, DefaultRisk = NIRACapabilityRisk.Modify,
        Description = "Create one genuine A4 Word report with title, 1-12 structured sections, paragraphs and optional blue-header TSV tables. No existing source or Word automation; save as NEW .docx.",
        Parameters = [ NIRADocx.Param("path", true, "New unused .docx destination"),
            NIRADocx.Param("title", true, "Report title"),
            NIRADocx.Param("sectionsJson", true, "JSON array of {heading,body,optional tableTsv} (1-12 sections)") ]
    };
    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest request) => NIRACapabilityRisk.Modify;
    public Task<NIRACapabilityHandlerResult> ExecuteAsync(NIRACapabilityRequest request,
        CancellationToken cancellationToken = default) => NIRADocx.ReportCreate47Async(request, cancellationToken);
}
