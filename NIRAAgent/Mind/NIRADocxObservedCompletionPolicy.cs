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
                NIRACapabilityIds.DocxPageLayout or NIRACapabilityIds.DocxTableStyle)
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
        bool Identical(string field) =>
            !string.IsNullOrEmpty(original.GetValueOrDefault(field)) &&
            string.Equals(original.GetValueOrDefault(field),saved.GetValueOrDefault(field),StringComparison.Ordinal);
        if (new[] { "Paragraphs","Headings","Tables","SectionCount","Paper","Orientation",
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
