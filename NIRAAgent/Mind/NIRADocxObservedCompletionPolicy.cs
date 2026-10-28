using System.Text;
using System.Text.Json;
using NIRAAgent.Capabilities;

namespace NIRAAgent.Mind;

/// <summary>
/// A bounded, mechanically reported PARTIAL handoff after an independent
/// reviewer exhausts its correction budget. Never marks an objective complete.
/// It only reports facts independently carried by the trusted, same-run
/// append receipt plus successful inspections of BOTH exact document paths.
/// No filesystem access, user-text parsing, model approvals, or new actions.
/// </summary>
public static class NIRADocxObservedCompletionPolicy
{
    public static string? FromResults(
        IReadOnlyDictionary<string, NIRACapabilityRequest> requests,
        IReadOnlyDictionary<string, NIRACapabilityResult> results)
    {
        if (requests.Count == 0 || results.Count == 0 ||
            requests.Count != results.Count) return null;

        NIRACapabilityRequest? appendRequest = null;
        Dictionary<string, string>? appendData = null;
        var inspections = new Dictionary<string, Dictionary<string, string>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var pair in requests)
        {
            if (!results.TryGetValue(pair.Key, out var result) ||
                result.CapabilityId != pair.Value.CapabilityId ||
                result.Status != NIRACapabilityResultStatus.Succeeded ||
                result.OutcomeUncertain || !result.AuditRecorded && result.ChangedSystemState)
                return null;

            switch (result.CapabilityId)
            {
                case NIRACapabilityIds.DocxAppend:
                    if (appendData is not null || !result.ChangedSystemState ||
                        result.Risk != NIRACapabilityRisk.Modify) return null;
                    appendRequest = pair.Value;
                    appendData = Fields(result.Output);
                    break;
                case NIRACapabilityIds.DocxInspect:
                    if (result.ChangedSystemState ||
                        result.Risk != NIRACapabilityRisk.Observe) return null;
                    var info = Fields(result.Output);
                    if (!info.TryGetValue("Path", out string? path) ||
                        !IsDocxPath(path) || !inspections.TryAdd(path, info)) return null;
                    break;
                case NIRACapabilityIds.FileHash:
                    if (result.ChangedSystemState ||
                        result.Risk != NIRACapabilityRisk.Observe) return null;
                    break;
                default:
                    return null; // No cross-domain or unproven side effects.
            }
        }

        if (appendRequest is null || appendData is null || inspections.Count != 2 ||
            !TryString(appendRequest.Arguments, "source", out string source) ||
            !TryString(appendRequest.Arguments, "destination", out string destination) ||
            !TryString(appendRequest.Arguments, "expectedSourceSha256", out string expected) ||
            !IsDocxPath(source) || !IsDocxPath(destination) ||
            source.Equals(destination, StringComparison.OrdinalIgnoreCase) ||
            !appendData.TryGetValue("Source", out string? receiptSource) ||
            !appendData.TryGetValue("Destination", out string? receiptDestination) ||
            !source.Equals(receiptSource, StringComparison.OrdinalIgnoreCase) ||
            !destination.Equals(receiptDestination, StringComparison.OrdinalIgnoreCase) ||
            !appendData.TryGetValue("SourceSHA256", out string? sourceHash) ||
            !appendData.TryGetValue("DestinationSHA256", out string? destinationHash) ||
            !ValidHash(sourceHash) || !ValidHash(destinationHash) ||
            !sourceHash.Equals(expected, StringComparison.OrdinalIgnoreCase) ||
            appendData.GetValueOrDefault("SourceUnchanged") != "True" ||
            appendData.GetValueOrDefault("OverwroteExisting") != "False" ||
            !inspections.TryGetValue(source, out var inspectedSource) ||
            !inspections.TryGetValue(destination, out var inspectedDestination) ||
            !sourceHash.Equals(inspectedSource.GetValueOrDefault("SHA256"), StringComparison.OrdinalIgnoreCase) ||
            !destinationHash.Equals(inspectedDestination.GetValueOrDefault("SHA256"), StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(inspectedSource.GetValueOrDefault("Paragraphs"), out int sourceParagraphs) ||
            !int.TryParse(inspectedDestination.GetValueOrDefault("Paragraphs"), out int newParagraphs) ||
            sourceParagraphs < 0 || newParagraphs <= sourceParagraphs)
            return null;

        var text = new StringBuilder();
        text.AppendLine("The DOCX operation succeeded, but my independent final-answer review did not confirm every requested detail. Here are the operations I can verify:");
        text.AppendLine();
        text.AppendLine($"- **Created new copy:** `{destination}`");
        text.AppendLine($"- **Original inspected:** `{source}`");
        text.AppendLine($"- **Original SHA-256:** `{sourceHash}`");
        text.AppendLine($"- **New file SHA-256:** `{destinationHash}`");
        text.AppendLine($"- **Paragraphs:** original {sourceParagraphs}; new copy {newParagraphs}");
        text.AppendLine("- **Original unchanged / no existing file overwritten:** confirmed by the successful DOCX append receipt, with a matching inspection hash for the original.");
        text.Append("Both documents were inspected successfully. This is a tool-verified partial report, not confirmation that every formatting or content requirement was met.");
        return text.ToString();
    }

    // R47.2: The finishing workflow's four real, audited native operations form
    // one verifiable objective. Do not let a subsequent narrative reviewer
    // discard a fully observed report/template/fill/batch transaction.
    // This method never trusts the model's prose, never reads or writes files,
    // and requires the same-run capability receipts, exact bound arguments,
    // fresh source hash, new targets, and every saved destination hash.
    public static string? FromVerifiedR47WorkflowResults(
        IReadOnlyDictionary<string, NIRACapabilityRequest> requests,
        IReadOnlyDictionary<string, NIRACapabilityResult> results)
    {
        if (requests.Count < 4 || requests.Count != results.Count) return null;
        var operations = new Dictionary<string,
            (NIRACapabilityRequest Request, Dictionary<string, string> Receipt)>(StringComparer.Ordinal);
        foreach (var pair in requests)
        {
            if (!results.TryGetValue(pair.Key, out var result) ||
                pair.Value.CapabilityId != result.CapabilityId ||
                result.Status != NIRACapabilityResultStatus.Succeeded || result.OutcomeUncertain)
                return null;
            bool observe = result.CapabilityId is NIRACapabilityIds.DocxTemplateInspect or
                NIRACapabilityIds.DocxInspect or NIRACapabilityIds.FileHash;
            bool write = result.CapabilityId is NIRACapabilityIds.DocxReportCreate or
                NIRACapabilityIds.DocxTemplateFill or NIRACapabilityIds.DocxTemplateBatch;
            if (!observe && !write) return null;
            if (write && (!result.ChangedSystemState || !result.AuditRecorded ||
                          result.Risk != NIRACapabilityRisk.Modify)) return null;
            if (observe && (result.ChangedSystemState || result.Risk != NIRACapabilityRisk.Observe))
                return null;
            if (!operations.TryAdd(result.CapabilityId, (pair.Value, Fields(result.Output))) &&
                !observe) return null;
        }
        if (!operations.TryGetValue(NIRACapabilityIds.DocxReportCreate, out var report) ||
            !operations.TryGetValue(NIRACapabilityIds.DocxTemplateInspect, out var inspection) ||
            !operations.TryGetValue(NIRACapabilityIds.DocxTemplateFill, out var fill) ||
            !operations.TryGetValue(NIRACapabilityIds.DocxTemplateBatch, out var batch))
            return null;
        if (!TryString(inspection.Request.Arguments, "path", out string templatePath) ||
            !IsDocxPath(templatePath) ||
            !string.Equals(inspection.Receipt.GetValueOrDefault("Path"), templatePath,
                StringComparison.OrdinalIgnoreCase) ||
            inspection.Receipt.GetValueOrDefault("NoFilesModified") != "True" ||
            !ValidHash(inspection.Receipt.GetValueOrDefault("SHA256"))) return null;
        string templateHash = inspection.Receipt["SHA256"];
        if (!int.TryParse(inspection.Receipt.GetValueOrDefault("PlaceholderCount"), out int placeholderCount) ||
            placeholderCount < 1 || placeholderCount > 40) return null;
        if (!TryString(report.Request.Arguments, "path", out string reportPath) ||
            !IsDocxPath(reportPath) ||
            !Same(reportPath, report.Receipt.GetValueOrDefault("Destination")) ||
            !ValidHash(report.Receipt.GetValueOrDefault("DestinationSHA256")) ||
            !ValidWrite(report.Receipt) ||
            !int.TryParse(report.Receipt.GetValueOrDefault("ReportSections"), out int reportSections) ||
            reportSections < 1 || reportSections > 12) return null;
        // The report may itself be the new template, as in the R47 test.
        if (Same(reportPath, templatePath) &&
            !SameHash(report.Receipt.GetValueOrDefault("DestinationSHA256"), templateHash)) return null;
        if (!TryString(fill.Request.Arguments, "source", out string fillSource) ||
            !TryString(fill.Request.Arguments, "destination", out string singlePath) ||
            !TryString(fill.Request.Arguments, "expectedSourceSha256", out string fillExpected) ||
            !Same(fillSource, templatePath) || !IsDocxPath(singlePath) ||
            !SameHash(fillExpected, templateHash) ||
            !Same(fill.Receipt.GetValueOrDefault("Source"), templatePath) ||
            !Same(fill.Receipt.GetValueOrDefault("Destination"), singlePath) ||
            !SameHash(fill.Receipt.GetValueOrDefault("SourceSHA256"), templateHash) ||
            !ValidHash(fill.Receipt.GetValueOrDefault("DestinationSHA256")) ||
            !int.TryParse(fill.Receipt.GetValueOrDefault("ReplacedPlaceholders"), out int replaced) ||
            replaced < placeholderCount ||
            !int.TryParse(fill.Receipt.GetValueOrDefault("PlaceholderKeys"), out int keys) ||
            keys != placeholderCount ||
            fill.Receipt.GetValueOrDefault("HeaderFooterAndTablesPreserved") != "True" ||
            !ValidWrite(fill.Receipt)) return null;
        if (!TryString(batch.Request.Arguments, "source", out string batchSource) ||
            !TryString(batch.Request.Arguments, "destinationDirectory", out string outputDirectory) ||
            !TryString(batch.Request.Arguments, "expectedSourceSha256", out string batchExpected) ||
            !Same(batchSource, templatePath) || !SameHash(batchExpected, templateHash) ||
            !Same(batch.Receipt.GetValueOrDefault("Source"), templatePath) ||
            !SameHash(batch.Receipt.GetValueOrDefault("SourceSHA256"), templateHash) ||
            !Same(batch.Receipt.GetValueOrDefault("DestinationDirectory"), outputDirectory) ||
            !ValidWrite(batch.Receipt) ||
            !int.TryParse(batch.Receipt.GetValueOrDefault("CreatedDocuments"), out int created) ||
            created is < 1 or > 12) return null;
        var files = new List<(string Path, string Hash)>();
        try
        {
            using JsonDocument declared = JsonDocument.Parse(
                batch.Request.Arguments.GetProperty("jobsJson").GetString() ?? "");
            using JsonDocument actual = JsonDocument.Parse(batch.Receipt["OutputFilesJson"]);
            if (declared.RootElement.ValueKind != JsonValueKind.Array ||
                actual.RootElement.ValueKind != JsonValueKind.Array ||
                declared.RootElement.GetArrayLength() != created ||
                actual.RootElement.GetArrayLength() != created) return null;
            string fullDirectory = Path.GetFullPath(outputDirectory);
            for (int i = 0; i < created; i++)
            {
                string name = declared.RootElement[i].GetProperty("fileName").GetString() ?? "";
                string path = actual.RootElement[i].GetProperty("path").GetString() ?? "";
                string sha = actual.RootElement[i].GetProperty("sha256").GetString() ?? "";
                if (name.Length is < 6 or > 90 || name.Contains('/') || name.Contains('\\') ||
                    name.Contains("..", StringComparison.Ordinal) || !name.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) ||
                    !IsDocxPath(path) || !Same(Path.Combine(fullDirectory, name), path) ||
                    !ValidHash(sha)) return null;
                files.Add((path, sha));
            }
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or
            InvalidOperationException or KeyNotFoundException or System.IO.IOException)
        {
            return null;
        }
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { templatePath, singlePath };
        if (paths.Count != 2) return null;
        if (!paths.Add(reportPath) && !Same(reportPath, templatePath)) return null;
        if (files.Any(file => !paths.Add(file.Path))) return null;

        // Validate optional subsequent independent observations when available.
        foreach (var pair in requests)
        {
            if (pair.Value.CapabilityId is not (NIRACapabilityIds.DocxInspect or NIRACapabilityIds.FileHash))
                continue;
            if (!TryString(pair.Value.Arguments, "path", out string path) || !paths.Contains(path)) return null;
            var observed = Fields(results[pair.Key].Output);
            if (!Same(observed.GetValueOrDefault("Path"), path)) return null;
            string? expectedHash = Same(path, templatePath) ? templateHash :
                Same(path, singlePath) ? fill.Receipt["DestinationSHA256"] :
                Same(path, reportPath) ? report.Receipt["DestinationSHA256"] :
                files.First(file => Same(file.Path, path)).Hash;
            string field = pair.Value.CapabilityId == NIRACapabilityIds.DocxInspect ? "SHA256" : "Sha256";
            if (!SameHash(expectedHash, observed.GetValueOrDefault(field))) return null;
            if (pair.Value.CapabilityId == NIRACapabilityIds.FileHash &&
                observed.GetValueOrDefault("ReadComplete") != "True") return null;
        }
        var response = new StringBuilder();
        response.AppendLine("**R47 Word finishing workflow: completed and verified by audited native DOCX tools.**");
        response.AppendLine();
        response.AppendLine($"- **Report/template created:** `{reportPath}` (SHA-256 `{report.Receipt["DestinationSHA256"]}`; {reportSections} section(s)).");
        response.AppendLine($"- **Placeholder inspection:** {placeholderCount} unique placeholder(s); template SHA-256 `{templateHash}`.");
        response.AppendLine($"- **Single template fill:** `{singlePath}` (SHA-256 `{fill.Receipt["DestinationSHA256"]}`; {replaced} replacement(s)).");
        response.AppendLine($"- **Batch generation:** {created} verified new document(s):");
        foreach (var file in files)
            response.AppendLine($"  - `{file.Path}` — SHA-256 `{file.Hash}`");
        response.AppendLine("- **Original template preserved:** verified by both fill and batch receipts.");
        response.AppendLine("- **Post-write verification:** passed for report, single fill and batch; no existing destination overwritten.");
        response.Append("This verifies saved OpenXML, tool receipts and file hashes; visual appearance and pagination in Microsoft Word were not checked.");
        return response.ToString();

        static bool Same(string? a, string? b) => !string.IsNullOrWhiteSpace(a) &&
            string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        static bool SameHash(string? a, string? b) => ValidHash(a) && ValidHash(b) && Same(a, b);
        static bool ValidWrite(Dictionary<string, string> receipt) =>
            receipt.GetValueOrDefault("PostWriteVerified") == "True" &&
            receipt.GetValueOrDefault("OverwroteExisting") == "False" &&
            receipt.GetValueOrDefault("VisualPaginationVerified") == "False" &&
            (!receipt.ContainsKey("SourceUnchanged") || receipt["SourceUnchanged"] == "True");
    }


    // R48: verify an ENTIRE one-shot studio document from a single audited tool
    // receipt. No model-invented completion, no unverified or mixed side effects.
    public static string? FromVerifiedStudioResults(
        IReadOnlyDictionary<string, NIRACapabilityRequest> requests,
        IReadOnlyDictionary<string, NIRACapabilityResult> results)
    {
        if (requests.Count != 1 || results.Count != 1) return null;
        var pair = requests.Single();
        if (!results.TryGetValue(pair.Key, out var result) ||
            pair.Value.CapabilityId != NIRACapabilityIds.DocxStudioCreate ||
            result.CapabilityId != NIRACapabilityIds.DocxStudioCreate ||
            result.Status != NIRACapabilityResultStatus.Succeeded ||
            result.Risk != NIRACapabilityRisk.Modify ||
            !result.ChangedSystemState || !result.AuditRecorded || result.OutcomeUncertain ||
            !TryString(pair.Value.Arguments, "path", out string path) || !IsDocxPath(path))
            return null;
        var receipt = Fields(result.Output);
        if (!string.Equals(path, receipt.GetValueOrDefault("Destination"), StringComparison.OrdinalIgnoreCase) ||
            !ValidHash(receipt.GetValueOrDefault("DestinationSHA256")) ||
            receipt.GetValueOrDefault("PostWriteVerified") != "True" ||
            receipt.GetValueOrDefault("OverwroteExisting") != "False" ||
            receipt.GetValueOrDefault("VisualPaginationVerified") != "False") return null;
        int blocks;
        try
        {
            if (!TryString(pair.Value.Arguments, "blocksJson", out string blocksText)) return null;
            using JsonDocument doc = JsonDocument.Parse(blocksText);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            blocks = doc.RootElement.GetArrayLength();
        }
        catch (JsonException) { return null; }
        if (blocks is < 1 or > 80 ||
            !int.TryParse(receipt.GetValueOrDefault("StudioBlocks"), out int reported) || reported != blocks ||
            !int.TryParse(receipt.GetValueOrDefault("StudioTables"), out int tables) || tables < 0 ||
            !int.TryParse(receipt.GetValueOrDefault("StudioImages"), out int images) || images is < 0 or > 1 ||
            !int.TryParse(receipt.GetValueOrDefault("StudioHeadings"), out int headings) || headings < 0) return null;
        var reply = new StringBuilder();
        reply.AppendLine("**R48 Word Studio: document created and verified.**");
        reply.AppendLine($"- **New Word file:** `{path}`");
        reply.AppendLine($"- **SHA-256:** `{receipt["DestinationSHA256"]}`");
        reply.AppendLine($"- **Document structure:** {blocks} content blocks; {headings} headings; {tables} tables; {images} embedded image(s).");
        reply.AppendLine($"- **Page design:** {receipt.GetValueOrDefault("Paper")} {receipt.GetValueOrDefault("Orientation")}, {receipt.GetValueOrDefault("MarginMm")} mm margins; accent #{receipt.GetValueOrDefault("AccentHex")}.");
        reply.AppendLine("- **Verification:** saved OpenXML and embedded media inspected; no existing file overwritten.");
        reply.Append("Visual page appearance and rendered pagination have not been checked in Word.");
        return reply.ToString();
    }

    // R46: audited single-write DOCX styling receipt. It is safe to deliver
    // without an additional independent model reviewer only for an explicit
    // single-tool user request and no outstanding non-DOCX work. Every fact
    // here must be bound to same-run audited capability requests/results.
    public static string? FromVerifiedProfessionalResults(
        IReadOnlyDictionary<string, NIRACapabilityRequest> requests,
        IReadOnlyDictionary<string, NIRACapabilityResult> results)
    {
        if (requests.Count == 0 || requests.Count != results.Count) return null;
        NIRACapabilityRequest? writeRequest = null;
        Dictionary<string, string>? receipt = null;
        foreach (var pair in requests)
        {
            if (!results.TryGetValue(pair.Key, out var result) ||
                pair.Value.CapabilityId != result.CapabilityId || result.OutcomeUncertain)
                return null;
            if (result.CapabilityId == NIRACapabilityIds.DocxProfessionalFormat)
            {
                if (writeRequest != null || !result.ChangedSystemState ||
                    result.Status != NIRACapabilityResultStatus.Succeeded ||
                    result.Risk != NIRACapabilityRisk.Modify || !result.AuditRecorded)
                    return null;
                writeRequest = pair.Value;
                receipt = Fields(result.Output);
            }
            else if (result.CapabilityId is NIRACapabilityIds.DocxInspect or
                NIRACapabilityIds.FileHash or NIRACapabilityIds.DocxContentCompare)
            {
                if (result.ChangedSystemState || result.Risk != NIRACapabilityRisk.Observe)
                    return null;
            }
            else return null;
        }
        if (writeRequest is null || receipt is null ||
            !TryString(writeRequest.Arguments, "source", out string source) ||
            !TryString(writeRequest.Arguments, "destination", out string destination) ||
            !TryString(writeRequest.Arguments, "expectedSourceSha256", out string expected) ||
            !IsDocxPath(source) || !IsDocxPath(destination) ||
            source.Equals(destination, StringComparison.OrdinalIgnoreCase) ||
            receipt.GetValueOrDefault("Source") != source ||
            receipt.GetValueOrDefault("Destination") != destination ||
            receipt.GetValueOrDefault("PostWriteVerified") != "True" ||
            receipt.GetValueOrDefault("SourceUnchanged") != "True" ||
            receipt.GetValueOrDefault("OverwroteExisting") != "False" ||
            receipt.GetValueOrDefault("SourceAndDestinationContentIdentical") != "True" ||
            receipt.GetValueOrDefault("TablesPreserved") != "True" ||
            receipt.GetValueOrDefault("SectionsPreserved") != "True" ||
            receipt.GetValueOrDefault("OtherPackagePartsPreserved") != "True" ||
            !receipt.TryGetValue("SourceSHA256", out string? sourceSha) ||
            !receipt.TryGetValue("DestinationSHA256", out string? destinationSha) ||
            !ValidHash(sourceSha) || !ValidHash(destinationSha) ||
            !string.Equals(sourceSha, expected, StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(receipt.GetValueOrDefault("VerifiedParagraphs"), out int paragraphs) ||
            !int.TryParse(receipt.GetValueOrDefault("VerifiedHeadings"), out int headings) ||
            !int.TryParse(receipt.GetValueOrDefault("VerifiedTables"), out int tables) ||
            !int.TryParse(receipt.GetValueOrDefault("FormattedParagraphs"), out int formatted) ||
            !int.TryParse(receipt.GetValueOrDefault("SkippedComplexParagraphs"), out int skipped) ||
            paragraphs < 0 || headings < 0 || tables < 0 || formatted < 1 || skipped < 0)
            return null;
        var answer = new StringBuilder();
        answer.AppendLine("The professional Word formatting was saved as a NEW, independently verified DOCX.");
        answer.AppendLine($"- **Source:** `{source}`");
        answer.AppendLine($"- **New file:** `{destination}`");
        answer.AppendLine($"- **Original SHA-256:** `{sourceSha}`");
        answer.AppendLine($"- **New SHA-256:** `{destinationSha}`");
        answer.AppendLine($"- **Profile:** {receipt.GetValueOrDefault("Profile")}; accent #{receipt.GetValueOrDefault("AccentHex")}");
        answer.AppendLine($"- **Formatted:** {formatted} body paragraphs; {skipped} complex paragraphs skipped or partially formatted.");
        answer.AppendLine($"- **Saved contents:** {paragraphs} nonempty paragraphs, {headings} headings, {tables} tables.");
        answer.AppendLine("- **Integrity:** all text, complete tables, sections, media and other package parts preserved; source unchanged; nothing overwritten.");
        answer.Append("This verifies the saved OpenXML and hashes, NOT the final visual appearance or pagination in Microsoft Word.");
        return answer.ToString();
    }

    // R42C: if cognition exhausts its rounds after a successful DOCX formatting
    // action, deliver the verified facts from that tool instead of saying no
    // result exists. Fail closed on unverified writes or unrelated state changes.
    // An earlier read-only inspection of a future destination may fail; that
    // cannot invalidate the modifying tool's OWN post-write verification.
    public static string? FromVerifiedFormattingResults(
        IReadOnlyDictionary<string, NIRACapabilityRequest> requests,
        IReadOnlyDictionary<string, NIRACapabilityResult> results)
    {
        if (requests.Count == 0 || requests.Count != results.Count) return null;
        NIRACapabilityRequest? writeRequest = null;
        NIRACapabilityResult? writeResult = null;
        Dictionary<string, string>? receipt = null;
        foreach (var pair in requests)
        {
            if (!results.TryGetValue(pair.Key, out var result) ||
                result.CapabilityId != pair.Value.CapabilityId || result.OutcomeUncertain)
                return null;
            if (result.CapabilityId is NIRACapabilityIds.DocxParagraphFormat or
                NIRACapabilityIds.DocxPageLayout or NIRACapabilityIds.DocxTableStyle or
                NIRACapabilityIds.DocxTableTransaction)
            {
                if (writeResult is not null ||
                    result.Status != NIRACapabilityResultStatus.Succeeded ||
                    result.Risk != NIRACapabilityRisk.Modify ||
                    !result.ChangedSystemState || !result.AuditRecorded)
                    return null;
                writeRequest = pair.Value;
                writeResult = result;
                receipt = Fields(result.Output);
            }
            else if (result.CapabilityId is NIRACapabilityIds.FileHash or
                NIRACapabilityIds.DocxInspect)
            {
                // Failed *observations* are fine. No other modifying work is.
                if (result.ChangedSystemState || result.Risk != NIRACapabilityRisk.Observe)
                    return null;
            }
            else return null;
        }
        if (writeRequest is null || writeResult is null || receipt is null ||
            receipt.GetValueOrDefault("PostWriteVerified") != "True" ||
            receipt.GetValueOrDefault("SourceUnchanged") != "True" ||
            receipt.GetValueOrDefault("OverwroteExisting") != "False" ||
            !TryString(writeRequest.Arguments, "source", out string source) ||
            !TryString(writeRequest.Arguments, "destination", out string destination) ||
            !TryString(writeRequest.Arguments, "expectedSourceSha256", out string expected) ||
            !IsDocxPath(source) || !IsDocxPath(destination) ||
            source.Equals(destination, StringComparison.OrdinalIgnoreCase) ||
            receipt.GetValueOrDefault("Source") != source ||
            receipt.GetValueOrDefault("Destination") != destination ||
            !receipt.TryGetValue("SourceSHA256", out string? sourceHash) ||
            !receipt.TryGetValue("DestinationSHA256", out string? destinationHash) ||
            !ValidHash(sourceHash) || !ValidHash(destinationHash) ||
            !sourceHash.Equals(expected, StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(receipt.GetValueOrDefault("VerifiedParagraphs"), out int paragraphs) ||
            !int.TryParse(receipt.GetValueOrDefault("VerifiedHeadings"), out int headings) ||
            !int.TryParse(receipt.GetValueOrDefault("VerifiedTables"), out int tables) ||
            paragraphs < 0 || headings < 0 || tables < 0)
            return null;
        string operation = writeResult.CapabilityId switch
        {
            NIRACapabilityIds.DocxPageLayout => "page layout",
            NIRACapabilityIds.DocxParagraphFormat => "paragraph formatting",
            NIRACapabilityIds.DocxTableTransaction => "multi-operation table transaction",
            _ => "table styling"
        };
        var reply = new StringBuilder();
        reply.AppendLine($"The Word {operation} operation created a new file and verified its saved document XML. An earlier inspection failed, so I am reporting only the confirmed results:");
        reply.AppendLine($"- **Source:** `{source}`");
        reply.AppendLine($"- **New output:** `{destination}`");
        reply.AppendLine($"- **Source SHA-256:** `{sourceHash}`");
        reply.AppendLine($"- **Output SHA-256:** `{destinationHash}`");
        reply.AppendLine($"- **Verified document contents:** {paragraphs} nonempty paragraphs, {headings} headings, {tables} tables.");
        if (writeResult.CapabilityId == NIRACapabilityIds.DocxPageLayout)
            reply.AppendLine($"- **Page setup:** {receipt.GetValueOrDefault("Paper")}, {receipt.GetValueOrDefault("Orientation")}, uniform {receipt.GetValueOrDefault("MarginMm")} mm margins.");
        else if (writeResult.CapabilityId == NIRACapabilityIds.DocxParagraphFormat)
            reply.AppendLine($"- **Paragraph formatting:** {receipt.GetValueOrDefault("Alignment")}, {receipt.GetValueOrDefault("FontSizePt")} pt, bold={receipt.GetValueOrDefault("Bold")}.");
        else if (writeResult.CapabilityId == NIRACapabilityIds.DocxTableTransaction)
            reply.AppendLine($"- **Atomic table changes:** {receipt.GetValueOrDefault("UpdatedCellsCount")} cell updates; {receipt.GetValueOrDefault("DeletedRowsCount")} row deletions; {receipt.GetValueOrDefault("AppendedRowsCount")} appended rows; {receipt.GetValueOrDefault("TableRowsBefore")} → {receipt.GetValueOrDefault("TableRowsAfter")} total rows.");
        else
            reply.AppendLine($"- **Table colors:** header #{receipt.GetValueOrDefault("HeaderFillHex")}, alternate rows #{receipt.GetValueOrDefault("StripeFillHex")}.");
        reply.Append("The original remains unchanged and no existing file was overwritten. This verifies the saved OpenXML structure, not its final visual pagination in Microsoft Word.");
        return reply.ToString();
    }

    // R43A.1: certificate for the actual native header/footer/page-number write.
    // This is derived entirely from same-run audited tool receipts plus fresh
    // read-only inspection of BOTH documents. No user-text inference, no disk
    // reads, and no claim of visual pagination in Microsoft Word.
    public static string? FromVerifiedHeaderFooterResults(
        IReadOnlyDictionary<string, NIRACapabilityRequest> requests,
        IReadOnlyDictionary<string, NIRACapabilityResult> results)
    {
        if (requests.Count < 4 || requests.Count != results.Count) return null;
        NIRACapabilityRequest? editRequest = null;
        Dictionary<string, string>? edit = null;
        var inspections = new Dictionary<string, Dictionary<string, string>>(
            StringComparer.OrdinalIgnoreCase);
        var hashes = new Dictionary<string, Dictionary<string, string>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var pair in requests)
        {
            if (!results.TryGetValue(pair.Key, out NIRACapabilityResult? result) ||
                result.CapabilityId != pair.Value.CapabilityId ||
                result.Status != NIRACapabilityResultStatus.Succeeded ||
                result.OutcomeUncertain) return null;
            if (result.CapabilityId == NIRACapabilityIds.DocxHeaderFooter)
            {
                if (editRequest != null || result.Risk != NIRACapabilityRisk.Modify ||
                    !result.ChangedSystemState || !result.AuditRecorded) return null;
                editRequest = pair.Value;
                edit = Fields(result.Output);
            }
            else if (result.CapabilityId is NIRACapabilityIds.DocxInspect or
                NIRACapabilityIds.FileHash)
            {
                if (result.Risk != NIRACapabilityRisk.Observe || result.ChangedSystemState ||
                    !TryString(pair.Value.Arguments, "path", out string observedPath) ||
                    !IsDocxPath(observedPath)) return null;
                Dictionary<string, string> data = Fields(result.Output);
                if (!data.TryGetValue("Path", out string? actualPath) ||
                    !string.Equals(observedPath, actualPath, StringComparison.OrdinalIgnoreCase)) return null;
                var target = result.CapabilityId == NIRACapabilityIds.DocxInspect
                    ? inspections : hashes;
                if (!target.TryAdd(actualPath, data)) return null;
            }
            else return null; // Do not conceal unrelated work or mutations.
        }
        if (editRequest == null || edit == null ||
            !TryString(editRequest.Arguments, "source", out string source) ||
            !TryString(editRequest.Arguments, "destination", out string destination) ||
            !TryString(editRequest.Arguments, "expectedSourceSha256", out string expected) ||
            !IsDocxPath(source) || !IsDocxPath(destination) ||
            source.Equals(destination, StringComparison.OrdinalIgnoreCase) ||
            !inspections.TryGetValue(source, out var original) ||
            !inspections.TryGetValue(destination, out var saved) ||
            inspections.Count != 2 || hashes.Count is < 1 or > 2 ||
            !hashes.TryGetValue(source, out var observedHash) ||
            hashes.Keys.Any(path => !path.Equals(source, StringComparison.OrdinalIgnoreCase) &&
                                    !path.Equals(destination, StringComparison.OrdinalIgnoreCase)) ||
            !ValidHash(expected) ||
            !string.Equals(edit.GetValueOrDefault("Source"), source, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(edit.GetValueOrDefault("Destination"), destination, StringComparison.OrdinalIgnoreCase) ||
            !ValidHash(edit.GetValueOrDefault("SourceSHA256")) ||
            !ValidHash(edit.GetValueOrDefault("DestinationSHA256")) ||
            !string.Equals(edit.GetValueOrDefault("SourceSHA256"), expected, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(original.GetValueOrDefault("SHA256"), expected, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(observedHash.GetValueOrDefault("Sha256"), expected, StringComparison.OrdinalIgnoreCase) ||
            observedHash.GetValueOrDefault("ReadComplete") != "True" ||
            (hashes.TryGetValue(destination, out var outputHash) &&
                (outputHash.GetValueOrDefault("ReadComplete") != "True" ||
                 !string.Equals(outputHash.GetValueOrDefault("Sha256"),
                     edit.GetValueOrDefault("DestinationSHA256"), StringComparison.OrdinalIgnoreCase))) ||
            !string.Equals(saved.GetValueOrDefault("SHA256"), edit.GetValueOrDefault("DestinationSHA256"), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(expected, saved.GetValueOrDefault("SHA256"), StringComparison.OrdinalIgnoreCase) ||
            edit.GetValueOrDefault("PostWriteVerified") != "True" ||
            edit.GetValueOrDefault("SourceUnchanged") != "True" ||
            edit.GetValueOrDefault("OverwroteExisting") != "False" ||
            saved.GetValueOrDefault("SectionCount") != "1" ||
            original.GetValueOrDefault("SectionCount") != "1" ||
            !SameNumber("Paragraphs") || !SameNumber("Headings") ||
            !SameNumber("Tables") || !SameValue("Paper") ||
            !SameValue("Orientation") ||
            !SameValue("MarginTopMm") || !SameValue("MarginBottomMm") ||
            !SameValue("MarginLeftMm") || !SameValue("MarginRightMm")) return null;

        bool SameNumber(string field) =>
            int.TryParse(original.GetValueOrDefault(field), out int a) && a >= 0 &&
            int.TryParse(saved.GetValueOrDefault(field), out int b) && a == b;
        bool SameValue(string field) =>
            !string.IsNullOrWhiteSpace(original.GetValueOrDefault(field)) &&
            string.Equals(original.GetValueOrDefault(field), saved.GetValueOrDefault(field), StringComparison.Ordinal);

        string? requestHeader = OptionalString(editRequest.Arguments, "headerText");
        string? requestFooter = OptionalString(editRequest.Arguments, "footerText");
        string? requestedPages = OptionalString(editRequest.Arguments, "pageNumbers");
        if (requestedPages is not (null or "true" or "false")) return null;
        bool pageNumbers = requestedPages == "true";
        if (requestHeader is null && requestFooter is null && !pageNumbers) return null;
        if (original.GetValueOrDefault("HeaderPresent") != "False" ||
            original.GetValueOrDefault("FooterPresent") != "False" ||
            original.GetValueOrDefault("PageNumberField") != "False" ||
            saved.GetValueOrDefault("HeaderPresent") != (requestHeader is null ? "False" : "True") ||
            saved.GetValueOrDefault("FooterPresent") != (requestFooter is null && !pageNumbers ? "False" : "True") ||
            saved.GetValueOrDefault("PageNumberField") != (pageNumbers ? "True" : "False")) return null;
        if (requestHeader is not null &&
            !StringFieldMatches(saved.GetValueOrDefault("HeaderTextJson"), requestHeader, false)) return null;
        if (requestFooter is not null &&
            !StringFieldMatches(saved.GetValueOrDefault("FooterTextJson"), requestFooter, pageNumbers)) return null;
        if (pageNumbers && requestFooter is null &&
            !StringFieldMatches(saved.GetValueOrDefault("FooterTextJson"), "Page ", true, false)) return null;

        var report = new StringBuilder();
        report.AppendLine("The Word header/footer operation created a NEW DOCX. The tool confirmed the saved OpenXML, and a separate inspection confirmed the exact output hash and document fields:");
        report.AppendLine($"- **Original:** `{source}`");
        report.AppendLine($"- **New document:** `{destination}`");
        report.AppendLine($"- **Original SHA-256:** `{expected}`");
        report.AppendLine($"- **New SHA-256:** `{saved["SHA256"]}`");
        report.AppendLine($"- **Header:** {(requestHeader ?? "none")}; present={saved["HeaderPresent"]}.");
        report.AppendLine($"- **Footer:** {(requestFooter ?? "none")}; present={saved["FooterPresent"]}.");
        report.AppendLine($"- **Automatic Word PAGE field:** {saved["PageNumberField"]}.");
        report.AppendLine($"- **Saved document:** {saved["Paragraphs"]} paragraphs, {saved["Headings"]} headings, {saved["Tables"]} table(s), {saved["Paper"]} {saved["Orientation"]}.");
        report.AppendLine($"- **Margins (mm):** top {saved["MarginTopMm"]}, right {saved["MarginRightMm"]}, bottom {saved["MarginBottomMm"]}, left {saved["MarginLeftMm"]}.");
        report.Append("The source SHA-256 was unchanged and no existing file was overwritten. The structure and field presence were verified in the saved DOCX; actual page rendering was not inspected in Microsoft Word.");
        return report.ToString();
    }


    // R43D.1: A native page-break insertion is verified from the audited write
    // and independent inspection of BOTH exact documents. No disk access or
    // model-authored "passed" claims. Missing/uncertain evidence fails closed.
    public static string? FromVerifiedPageBreakResults(
        IReadOnlyDictionary<string, NIRACapabilityRequest> requests,
        IReadOnlyDictionary<string, NIRACapabilityResult> results)
    {
        if (requests.Count < 3 || requests.Count != results.Count) return null;
        NIRACapabilityRequest? writeRequest = null;
        Dictionary<string, string>? write = null;
        var inspected = new Dictionary<string, Dictionary<string, string>>(
            StringComparer.OrdinalIgnoreCase);
        var hashes = new Dictionary<string, Dictionary<string, string>>(
            StringComparer.OrdinalIgnoreCase);
        var earlierFailedWrites = new List<NIRACapabilityRequest>();
        foreach (var item in requests)
        {
            if (!results.TryGetValue(item.Key, out var result) ||
                result.CapabilityId != item.Value.CapabilityId) return null;
            // R43D.2: one failed first attempt may have raced with an early
            // destination inspection. Its effect is NOT certified. A subsequent
            // independently verified CreateNew on the same destination proves
            // that an earlier attempt did not leave that destination file.
            // Keep strict requirements on the completed write and both reads.
            if (result.CapabilityId == NIRACapabilityIds.DocxPageBreakInsert &&
                result.Status == NIRACapabilityResultStatus.Failed &&
                !result.ChangedSystemState &&
                result.Risk == NIRACapabilityRisk.Modify)
            {
                if (earlierFailedWrites.Count > 0) return null;
                earlierFailedWrites.Add(item.Value);
                continue;
            }
            if (result.Status != NIRACapabilityResultStatus.Succeeded ||
                result.OutcomeUncertain) return null;
            if (result.CapabilityId == NIRACapabilityIds.DocxPageBreakInsert)
            {
                if (writeRequest != null || result.Risk != NIRACapabilityRisk.Modify ||
                    !result.ChangedSystemState || !result.AuditRecorded) return null;
                writeRequest = item.Value;
                write = Fields(result.Output);
            }
            else if (result.CapabilityId is NIRACapabilityIds.DocxInspect or
                     NIRACapabilityIds.FileHash)
            {
                if (result.Risk != NIRACapabilityRisk.Observe || result.ChangedSystemState ||
                    !TryString(item.Value.Arguments, "path", out string observedPath) ||
                    !IsDocxPath(observedPath)) return null;
                var evidence = Fields(result.Output);
                if (!string.Equals(evidence.GetValueOrDefault("Path"), observedPath,
                        StringComparison.OrdinalIgnoreCase)) return null;
                var target = result.CapabilityId == NIRACapabilityIds.DocxInspect
                    ? inspected : hashes;
                if (!target.TryAdd(observedPath, evidence)) return null;
            }
            else return null; // Never certify mixed or unrelated actions.
        }

        if (writeRequest is null || write is null || inspected.Count != 2 ||
            !TryString(writeRequest.Arguments, "source", out string source) ||
            !TryString(writeRequest.Arguments, "destination", out string destination) ||
            !TryString(writeRequest.Arguments, "afterParagraph", out string anchor) ||
            !TryString(writeRequest.Arguments, "expectedSourceSha256", out string expected) ||
            !IsDocxPath(source) || !IsDocxPath(destination) ||
            source.Equals(destination, StringComparison.OrdinalIgnoreCase) ||
            !ValidHash(expected) || !inspected.TryGetValue(source, out var original) ||
            !inspected.TryGetValue(destination, out var saved) ||
            hashes.Keys.Any(path => !path.Equals(source, StringComparison.OrdinalIgnoreCase) &&
                                   !path.Equals(destination, StringComparison.OrdinalIgnoreCase)) ||
            !ValidHash(write.GetValueOrDefault("SourceSHA256")) ||
            !ValidHash(write.GetValueOrDefault("DestinationSHA256")) ||
            !string.Equals(write.GetValueOrDefault("Source"), source, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(write.GetValueOrDefault("Destination"), destination, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(write.GetValueOrDefault("SourceSHA256"), expected, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(original.GetValueOrDefault("SHA256"), expected, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(saved.GetValueOrDefault("SHA256"), write.GetValueOrDefault("DestinationSHA256"), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(saved.GetValueOrDefault("SHA256"), expected, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(write.GetValueOrDefault("AnchorParagraph"), anchor, StringComparison.Ordinal) ||
            write.GetValueOrDefault("PostWriteVerified") != "True" ||
            write.GetValueOrDefault("SourceUnchanged") != "True" ||
            write.GetValueOrDefault("OverwroteExisting") != "False" ||
            write.GetValueOrDefault("VisualPaginationVerified") != "False") return null;

        if (earlierFailedWrites.Any(failed =>
            !TryString(failed.Arguments, "source", out string failedSource) ||
            !TryString(failed.Arguments, "destination", out string failedDestination) ||
            !string.Equals(failedSource, source, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(failedDestination, destination, StringComparison.OrdinalIgnoreCase)))
            return null;

        foreach (var hash in hashes)
        {
            string required = hash.Key.Equals(source, StringComparison.OrdinalIgnoreCase)
                ? expected : saved["SHA256"];
            if (hash.Value.GetValueOrDefault("ReadComplete") != "True" ||
                !string.Equals(hash.Value.GetValueOrDefault("Sha256"), required,
                    StringComparison.OrdinalIgnoreCase)) return null;
        }
        if (!int.TryParse(original.GetValueOrDefault("ManualPageBreaks"), out int before) || before < 0 ||
            !int.TryParse(saved.GetValueOrDefault("ManualPageBreaks"), out int after) || after != before + 1 ||
            !int.TryParse(write.GetValueOrDefault("ManualPageBreaksBefore"), out int writeBefore) || writeBefore != before ||
            !int.TryParse(write.GetValueOrDefault("ManualPageBreaksAfter"), out int writeAfter) || writeAfter != after ||
            !int.TryParse(original.GetValueOrDefault("Tables"), out int tables) || tables < 0 ||
            !int.TryParse(saved.GetValueOrDefault("Tables"), out int savedTables) || savedTables != tables ||
            !int.TryParse(write.GetValueOrDefault("Tables"), out int writeTables) || writeTables != tables ||
            !int.TryParse(original.GetValueOrDefault("EmbeddedMediaParts"), out int images) || images < 0 ||
            !int.TryParse(saved.GetValueOrDefault("EmbeddedMediaParts"), out int savedImages) || savedImages != images ||
            !int.TryParse(write.GetValueOrDefault("EmbeddedMediaParts"), out int writeImages) || writeImages != images)
            return null;

        bool Equal(string field) =>
            !string.IsNullOrWhiteSpace(original.GetValueOrDefault(field)) &&
            string.Equals(original.GetValueOrDefault(field), saved.GetValueOrDefault(field),
                StringComparison.Ordinal);
        // Paragraphs counts NONEMPTY paragraphs; the page-break node adds no text.
        if (new[] { "Paragraphs", "Headings", "SectionCount", "Paper", "Orientation",
                    "MarginTopMm", "MarginRightMm", "MarginBottomMm", "MarginLeftMm",
                    "HeaderPresent", "FooterPresent", "PageNumberField",
                    "HeaderTextJson", "FooterTextJson" }
                .Where(field => original.ContainsKey(field) || saved.ContainsKey(field))
                .Any(field => !Equal(field))) return null;

        var report = new StringBuilder();
        report.AppendLine("NIRA inserted a native Word hard page break into a NEW DOCX. The audited write and separate source/output inspections agree:");
        report.AppendLine($"- **Original:** `{source}`");
        report.AppendLine($"- **New document:** `{destination}`");
        report.AppendLine($"- **Source SHA-256:** `{expected}`");
        report.AppendLine($"- **Output SHA-256:** `{saved["SHA256"]}`");
        report.AppendLine($"- **Inserted after:** {anchor}");
        report.AppendLine($"- **Manual page breaks:** {before} → {after} (exactly one added).");
        report.AppendLine($"- **Preserved:** {tables} table(s), {images} embedded media part(s); " +
            $"header={saved.GetValueOrDefault("HeaderPresent")}, footer={saved.GetValueOrDefault("FooterPresent")}, " +
            $"PAGE field={saved.GetValueOrDefault("PageNumberField")}; " +
            $"{saved.GetValueOrDefault("Paper")} {saved.GetValueOrDefault("Orientation")}.");
        report.AppendLine($"- **Saved body:** {saved["Paragraphs"]} nonempty paragraphs, {saved["Headings"]} headings, {saved["SectionCount"]} section(s).");
        report.AppendLine($"- **Verified margins (mm):** top {saved["MarginTopMm"]}, right {saved["MarginRightMm"]}, bottom {saved["MarginBottomMm"]}, left {saved["MarginLeftMm"]}.");
        report.AppendLine("- **Post-write verification:** True; source unchanged=True; existing file overwritten=False.");
        report.Append("The source remained unchanged, no existing output was overwritten, and the saved OpenXML was checked. Actual visual pagination must still be checked by opening the file in Microsoft Word.");
        return report.ToString();
    }

    // R43D.3: Restrict direct delivery to an explicitly requested page-break
    // capability and its full, same-run audited evidence. Earlier narrative
    // reviewer objections and their drafts are NOT execution evidence.
    // This helper is pure, and never touches the filesystem or grants access.
    public static string? FromExplicitVerifiedPageBreakTask(
        string userRequest,
        IReadOnlyDictionary<string, NIRACapabilityRequest> requests,
        IReadOnlyDictionary<string, NIRACapabilityResult> results,
        bool unrelatedOrPendingWork)
    {
        if (unrelatedOrPendingWork ||
            string.IsNullOrWhiteSpace(userRequest) ||
            !userRequest.Contains(NIRACapabilityIds.DocxPageBreakInsert,
                StringComparison.OrdinalIgnoreCase))
            return null;
        return FromVerifiedPageBreakResults(requests, results);
    }

    // R43B: independently verified inline-image completion, restricted to one
    // audited modification and exact matching source/destination observations.
    // A successful mutation alone is never a completion certificate.
    public static string? FromVerifiedImageInsertionResults(
        IReadOnlyDictionary<string, NIRACapabilityRequest> requests,
        IReadOnlyDictionary<string, NIRACapabilityResult> results)
    {
        if (requests.Count < 4 || requests.Count != results.Count) return null;
        NIRACapabilityRequest? write = null;
        Dictionary<string,string>? edit = null;
        var inspections = new Dictionary<string,Dictionary<string,string>>(StringComparer.OrdinalIgnoreCase);
        var hashes = new Dictionary<string,Dictionary<string,string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in requests)
        {
            if (!results.TryGetValue(pair.Key, out var result) ||
                result.CapabilityId != pair.Value.CapabilityId ||
                result.Status != NIRACapabilityResultStatus.Succeeded || result.OutcomeUncertain)
                return null;
            if (result.CapabilityId == NIRACapabilityIds.DocxImageInsert)
            {
                if (write != null || result.Risk != NIRACapabilityRisk.Modify ||
                    !result.ChangedSystemState || !result.AuditRecorded) return null;
                write = pair.Value;
                edit = Fields(result.Output);
            }
            else if (result.CapabilityId is NIRACapabilityIds.DocxInspect or NIRACapabilityIds.FileHash)
            {
                if (result.Risk != NIRACapabilityRisk.Observe || result.ChangedSystemState ||
                    !TryString(pair.Value.Arguments, "path", out string path) ||
                    !IsDocxPath(path)) return null;
                var observed = Fields(result.Output);
                if (!string.Equals(path, observed.GetValueOrDefault("Path"), StringComparison.OrdinalIgnoreCase))
                    return null;
                var collection = result.CapabilityId == NIRACapabilityIds.DocxInspect
                    ? inspections : hashes;
                if (!collection.TryAdd(path, observed)) return null;
            }
            else return null;
        }
        if (write == null || edit == null ||
            !TryString(write.Arguments,"source",out string source) ||
            !TryString(write.Arguments,"destination",out string destination) ||
            !TryString(write.Arguments,"imagePath",out string imagePath) ||
            !TryString(write.Arguments,"afterParagraph",out string anchor) ||
            !TryString(write.Arguments,"altText",out string alt) ||
            !TryString(write.Arguments,"expectedSourceSha256",out string expected) ||
            !IsDocxPath(source) || !IsDocxPath(destination) ||
            source.Equals(destination,StringComparison.OrdinalIgnoreCase) ||
            !inspections.TryGetValue(source,out var original) ||
            !inspections.TryGetValue(destination,out var saved) ||
            inspections.Count != 2 || hashes.Count is < 1 or > 2 ||
            !hashes.TryGetValue(source,out var observedHash) ||
            hashes.Keys.Any(path=> !path.Equals(source,StringComparison.OrdinalIgnoreCase) &&
                                   !path.Equals(destination,StringComparison.OrdinalIgnoreCase)) ||
            !ValidHash(expected) ||
            !string.Equals(original.GetValueOrDefault("SHA256"),expected,StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(observedHash.GetValueOrDefault("Sha256"),expected,StringComparison.OrdinalIgnoreCase) ||
            observedHash.GetValueOrDefault("ReadComplete") != "True" ||
            !string.Equals(edit.GetValueOrDefault("Source"),source,StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(edit.GetValueOrDefault("Destination"),destination,StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(edit.GetValueOrDefault("SourceSHA256"),expected,StringComparison.OrdinalIgnoreCase) ||
            !ValidHash(edit.GetValueOrDefault("DestinationSHA256")) ||
            !string.Equals(saved.GetValueOrDefault("SHA256"),edit.GetValueOrDefault("DestinationSHA256"),StringComparison.OrdinalIgnoreCase) ||
            !ValidHash(edit.GetValueOrDefault("ImageSHA256")) ||
            !string.Equals(edit.GetValueOrDefault("ImagePath"),imagePath,StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(edit.GetValueOrDefault("ImageAltText"),alt,StringComparison.Ordinal) ||
            !string.Equals(edit.GetValueOrDefault("AnchorParagraph"),anchor,StringComparison.Ordinal) ||
            edit.GetValueOrDefault("PostWriteVerified") != "True" ||
            edit.GetValueOrDefault("SourceUnchanged") != "True" ||
            edit.GetValueOrDefault("OverwroteExisting") != "False") return null;
        if (hashes.TryGetValue(destination,out var outputHash) &&
            (outputHash.GetValueOrDefault("ReadComplete") != "True" ||
             !string.Equals(outputHash.GetValueOrDefault("Sha256"),saved.GetValueOrDefault("SHA256"),StringComparison.OrdinalIgnoreCase)))
            return null;
        bool captionRequested = TryString(write.Arguments, "captionText", out string captionText);
        if (captionRequested &&
            (edit.GetValueOrDefault("CaptionPresent") != "True" ||
             !string.Equals(edit.GetValueOrDefault("CaptionText"),captionText.Trim(),StringComparison.Ordinal))) return null;
        if (!captionRequested && edit.GetValueOrDefault("CaptionPresent") != "False") return null;
        if (!int.TryParse(original.GetValueOrDefault("Paragraphs"),out int originalParagraphs) ||
            !int.TryParse(saved.GetValueOrDefault("Paragraphs"),out int savedParagraphs) ||
            savedParagraphs != originalParagraphs + (captionRequested ? 1 : 0)) return null;
        bool Identical(string field) =>
            !string.IsNullOrEmpty(original.GetValueOrDefault(field)) &&
            string.Equals(original.GetValueOrDefault(field),saved.GetValueOrDefault(field),StringComparison.Ordinal);
        if (new[] { "Headings","Tables","SectionCount","Paper","Orientation",
                    "MarginTopMm","MarginRightMm","MarginBottomMm","MarginLeftMm",
                    "HeaderPresent","FooterPresent","PageNumberField","HeaderTextJson","FooterTextJson" }
                .Where(field=> original.ContainsKey(field) || saved.ContainsKey(field))
                .Any(field=> !Identical(field)) ||
            !int.TryParse(original.GetValueOrDefault("EmbeddedMediaParts"),out int beforeImages) ||
            !int.TryParse(saved.GetValueOrDefault("EmbeddedMediaParts"),out int afterImages) ||
            !int.TryParse(edit.GetValueOrDefault("EmbeddedMediaParts"),out int reportedImages) ||
            beforeImages < 0 || afterImages != beforeImages + 1 || reportedImages != afterImages)
            return null;
        var reply = new StringBuilder();
        reply.AppendLine("The native Word image was embedded in a NEW DOCX copy and verified from the actual saved image bytes and DrawingML relationship:");
        reply.AppendLine($"- **Original:** `{source}`");
        reply.AppendLine($"- **New document:** `{destination}`");
        reply.AppendLine($"- **Original SHA-256:** `{expected}`");
        reply.AppendLine($"- **Output SHA-256:** `{saved["SHA256"]}`");
        reply.AppendLine($"- **Image:** `{imagePath}`, {edit.GetValueOrDefault("ImageType")}, {edit.GetValueOrDefault("ImagePixels")} pixels, {edit.GetValueOrDefault("ImageWidthMm")} mm wide.");
        reply.AppendLine($"- **Inserted after:** {anchor}; **alternative text:** {alt}.");
        reply.AppendLine($"- **Embedded image parts:** {beforeImages} → {afterImages}.");
        if (captionRequested) reply.AppendLine($"- **Formatted caption:** {captionText.Trim()}; alignment {edit.GetValueOrDefault("CaptionAlignment")}; caption gap {edit.GetValueOrDefault("CaptionGapPt")} pt.");
        reply.AppendLine($"- **Image spacing:** before {edit.GetValueOrDefault("SpaceBeforePt")} pt; after {edit.GetValueOrDefault("SpaceAfterPt")} pt.");
        reply.AppendLine($"- **Preserved content:** {saved["Paragraphs"]} nonempty paragraphs, {saved["Headings"]} headings, {saved["Tables"]} table(s); {saved.GetValueOrDefault("Paper")} {saved.GetValueOrDefault("Orientation")}.");
        reply.Append("The original SHA-256 stayed unchanged. The new DOCX media and structure are verified; the visual page rendering must still be checked in Word.");
        return reply.ToString();
    }

    private static string? OptionalString(JsonElement args, string name)
    {
        if (args.ValueKind != JsonValueKind.Object ||
            !args.TryGetProperty(name, out var item) || item.ValueKind != JsonValueKind.String)
            return null;
        string? text = item.GetString()?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static bool StringFieldMatches(string? jsonValue, string expected,
        bool withPage, bool exactPrefix = true)
    {
        if (string.IsNullOrWhiteSpace(jsonValue)) return false;
        try
        {
            string? actual = JsonSerializer.Deserialize<string>(jsonValue);
            if (actual is null) return false;
            if (!withPage) return string.Equals(actual, expected, StringComparison.Ordinal);
            if (!actual.StartsWith(expected, StringComparison.Ordinal)) return false;
            string suffix = actual[expected.Length..];
            if (exactPrefix && !suffix.StartsWith(" | Page ", StringComparison.Ordinal)) return false;
            return (exactPrefix ? suffix[8..] : suffix).Length > 0 &&
                (exactPrefix ? suffix[8..] : suffix).All(char.IsDigit);
        }
        catch (JsonException) { return false; }
    }

    private static Dictionary<string, string> Fields(string raw)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in (raw ?? "").Split('\n'))
        {
            if (line.TrimEnd('\r') == "Preview:") break;
            int equals = line.IndexOf('=');
            if (equals <= 0) continue;
            string key = line[..equals].Trim();
            if (fields.ContainsKey(key)) return new Dictionary<string, string>(StringComparer.Ordinal);
            fields.Add(key, line[(equals + 1)..].Trim());
        }
        return fields;
    }

    private static bool TryString(JsonElement args, string name, out string value)
    {
        value = string.Empty;
        if (args.ValueKind != JsonValueKind.Object ||
            !args.TryGetProperty(name, out JsonElement item) ||
            item.ValueKind != JsonValueKind.String) return false;
        value = (item.GetString() ?? "").Trim();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool IsDocxPath(string path) =>
        path.Length is > 7 and <= 32760 &&
        char.IsLetter(path[0]) && path[1] == ':' && path[2] == '\\' &&
        path.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) &&
        !path.Contains('\n') && !path.Contains('\r') && !path.Contains('`');

    private static bool ValidHash(string? hash) =>
        hash is { Length: 64 } && hash.All(Uri.IsHexDigit);
}
