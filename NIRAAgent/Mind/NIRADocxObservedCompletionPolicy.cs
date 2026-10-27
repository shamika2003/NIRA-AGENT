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
