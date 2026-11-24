using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;
using Nira.Core;

namespace Nira.Infrastructure;

/// <summary>
/// Revision tools always publish a NEW file, never overwrite the source. The source
/// remains a complete, versioned rollback copy. The model chooses edits and names;
/// the host validates paths, optimistic SHA-256, formats and post-save evidence.
/// </summary>
public static class ProductivityRevisions
{
    private const long MaximumInputBytes = 48L * 1024 * 1024;
    private static readonly Regex CellAddress = new("^[A-Za-z]{1,3}[1-9][0-9]{0,6}$",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static ToolReceipt ReviseWorkbook(string source, string destination,
        string expectedSha256, JsonElement edits)
    {
        ToolReceipt? invalid = Preflight(source, destination, ".xlsx", expectedSha256);
        if (invalid is not null) return invalid;
        if (edits.ValueKind != JsonValueKind.Array || edits.GetArrayLength() is < 1 or > 200)
            return ToolReceipt.Fail("REVISION_SHAPE", "Supply between 1 and 200 cell revisions.");
        string temporary = TemporaryFor(destination, ".xlsx");
        try
        {
            using (var workbook = new XLWorkbook(source))
            {
                var touched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (JsonElement edit in edits.EnumerateArray())
                {
                    if (edit.ValueKind != JsonValueKind.Object ||
                        !RequiredText(edit, "sheet", out string sheet) ||
                        !RequiredText(edit, "cell", out string address) ||
                        !CellAddress.IsMatch(address))
                        return ToolReceipt.Fail("CELL_REVISION", "Each revision needs a valid sheet and A1 cell address.");
                    IXLWorksheet? worksheet = workbook.Worksheets.FirstOrDefault(w =>
                        string.Equals(w.Name, sheet, StringComparison.OrdinalIgnoreCase));
                    if (worksheet is null) return ToolReceipt.Fail("SHEET_NOT_FOUND", "The specified sheet does not exist.");
                    if (!touched.Add(worksheet.Name + "!" + address.ToUpperInvariant()))
                        return ToolReceipt.Fail("CELL_REVISION", "A cell may be revised only once per operation.");
                    bool isValue = edit.TryGetProperty("value", out JsonElement value);
                    bool isFormula = edit.TryGetProperty("formula", out JsonElement formulaValue);
                    if (isValue == isFormula)
                        return ToolReceipt.Fail("CELL_REVISION", "Supply exactly one of value or formula.");
                    // Reject an ambiguous literal formula before any output can
                    // be published. The model must use the typed `formula` field
                    // to request an executable expression. A caller may opt in
                    // to intentionally literal formula-looking text explicitly.
                    bool literalFormulaText = edit.TryGetProperty("literalFormulaText", out JsonElement literalMode) &&
                        literalMode.ValueKind == JsonValueKind.True;
                    if (literalFormulaText && (!isValue || value.ValueKind != JsonValueKind.String ||
                        !(value.GetString() ?? "").TrimStart().StartsWith("=", StringComparison.Ordinal)))
                        return ToolReceipt.Fail("CELL_LITERAL_MODE",
                            "literalFormulaText=true is valid only with a string value beginning '='; " +
                            "remove this flag for actual formulas or other values.");
                    IXLCell cell = worksheet.Cell(address);
                    if (isFormula)
                    {
                        if (formulaValue.ValueKind != JsonValueKind.String)
                            return ToolReceipt.Fail("CELL_FORMULA", "Formula must be text.");
                        string formula = (formulaValue.GetString() ?? "").Trim().TrimStart('=');
                        if (!SafeFormula(formula))
                            return ToolReceipt.Fail("CELL_FORMULA", "Invalid or external-reference formula refused.");
                        cell.FormulaA1 = formula;
                    }
                    else
                    {
                        switch (value.ValueKind)
                        {
                            case JsonValueKind.String:
                                string text = value.GetString() ?? "";
                                if (text.Length > 12000) return ToolReceipt.Fail("CELL_REVISION", "Cell text too long.");
                                if (text.TrimStart().StartsWith("=", StringComparison.Ordinal) && !literalFormulaText)
                                    return ToolReceipt.Fail("FORMULA_AS_TEXT",
                                        "This value starts with '=' and would be stored as literal text, NOT as an Excel formula. " +
                                        "To write a working formula use the 'formula' property (for example " +
                                        "formula: 'SUM(B2:B5)'), not 'value'. To intentionally store literal text, " +
                                        "use 'value' together with literalFormulaText: true.");
                                cell.SetValue(text); // Explicit literal strings never execute formulas.
                                break;
                            case JsonValueKind.Number:
                                if (!value.TryGetDouble(out double number) || !double.IsFinite(number))
                                    return ToolReceipt.Fail("CELL_REVISION", "Cell number must be finite.");
                                cell.SetValue(number);
                                break;
                            case JsonValueKind.True: cell.SetValue(true); break;
                            case JsonValueKind.False: cell.SetValue(false); break;
                            default:
                                return ToolReceipt.Fail("CELL_REVISION", "Value must be text, a finite number or a boolean.");
                        }
                    }
                    if (edit.TryGetProperty("numberFormat", out var numberFormat))
                    {
                        string? format = numberFormat.ValueKind == JsonValueKind.String
                            ? numberFormat.GetString() : null;
                        if (string.IsNullOrEmpty(format) || format.Length > 120)
                            return ToolReceipt.Fail("CELL_FORMAT", "Number format must be nonempty text <=120 characters.");
                        cell.Style.NumberFormat.Format = format;
                    }
                    if (edit.TryGetProperty("bold", out var bold))
                    {
                        if (bold.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                            return ToolReceipt.Fail("CELL_FORMAT", "Bold must be a boolean.");
                        cell.Style.Font.Bold = bold.GetBoolean();
                    }
                }
                workbook.SaveAs(temporary);
            }
            if (!WorkbookEditsMatch(temporary, edits))
                return ToolReceipt.Fail("REVISION_VERIFY", "The candidate Excel revision failed read-back validation.");
            int formulaCells = edits.EnumerateArray().Count(edit => edit.TryGetProperty("formula", out _));
            return PublishRevision(source, destination, temporary, expectedSha256,
                "xlsx", edits.GetArrayLength(), path => WorkbookEditsMatch(path, edits),
                verifiedFormulaCells: formulaCells);
        }
        catch (Exception error) when (CanReportFailure(error) && !File.Exists(destination))
        {
            return ToolReceipt.Fail("WORKBOOK_REVISION_FAILED", "Excel revision could not be completed: " + error.GetType().Name);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static ToolReceipt ReviseDocument(string source, string destination,
        string expectedSha256, JsonElement operations)
    {
        ToolReceipt? invalid = Preflight(source, destination, ".docx", expectedSha256);
        if (invalid is not null) return invalid;
        if (operations.ValueKind != JsonValueKind.Array || operations.GetArrayLength() is < 1 or > 60)
            return ToolReceipt.Fail("REVISION_SHAPE", "Supply between 1 and 60 document revisions.");
        string temporary = TemporaryFor(destination, ".docx");
        try
        {
            File.Copy(source, temporary, overwrite: false);
            using (var document = WordprocessingDocument.Open(temporary, true))
            {
                // Open XML may expose a missing main document part or document.
                // Guard both once before editing; this proves the reference is
                // non-null for the entire revision and the final Save call.
                W.Document? wordDocument = document.MainDocumentPart?.Document;
                if (wordDocument is null || wordDocument.Body is not W.Body body)
                    return ToolReceipt.Fail("DOCUMENT_EMPTY", "Word document has no body.");
                foreach (JsonElement operation in operations.EnumerateArray())
                {
                    if (operation.ValueKind != JsonValueKind.Object ||
                        !RequiredText(operation, "kind", out string kind))
                        return ToolReceipt.Fail("DOCUMENT_REVISION", "Each revision needs a kind.");
                    if (kind is "replaceParagraph" or "replaceParagraphWithTable")
                    {
                        if (!operation.TryGetProperty("paragraphIndex", out var indexValue) ||
                            !indexValue.TryGetInt32(out int index) || index < 1 || index > 10000 ||
                            !RequiredText(operation, "expectedText", out string expectedText))
                            return ToolReceipt.Fail("DOCUMENT_REVISION", "Replacement needs a 1-based paragraphIndex and expectedText.");
                        W.Paragraph? paragraph = body.Elements<W.Paragraph>().Skip(index - 1).FirstOrDefault();
                        if (paragraph is null || paragraph.InnerText != expectedText)
                            return ToolReceipt.Fail("STALE_PARAGRAPH", "The selected paragraph no longer matches expected text.");
                        if (kind == "replaceParagraph")
                        {
                            if (!RequiredText(operation, "text", out string replacement) || replacement.Length > 12000)
                                return ToolReceipt.Fail("DOCUMENT_REVISION", "Replacement text must be <=12000 characters.");
                            W.Text[] allText = paragraph.Descendants<W.Text>().ToArray();
                            if (allText.Length != 1 || paragraph.Descendants<W.Run>().Count() != 1 ||
                                paragraph.Descendants<W.FieldChar>().Any() ||
                                paragraph.Descendants<W.Hyperlink>().Any())
                                return ToolReceipt.Fail("COMPLEX_PARAGRAPH", "Replacing a multi-run/field paragraph could remove formatting; refused.");
                            allText[0].Text = replacement;
                            allText[0].Space = SpaceProcessingModeValues.Preserve;
                        }
                        else
                        {
                            if (!operation.TryGetProperty("rows", out var rows) ||
                                !TryTable(rows, out W.Table? table))
                                return ToolReceipt.Fail("DOCUMENT_TABLE", "Provide 1-100 rows of 1-12 strings to create a table.");
                            paragraph.InsertAfterSelf(table!);
                            paragraph.Remove();
                        }
                    }
                    else if (kind is "appendParagraph" or "appendHeading")
                    {
                        if (!RequiredText(operation, "text", out string content) || content.Length > 12000)
                            return ToolReceipt.Fail("DOCUMENT_REVISION", "Append text must be <=12000 characters.");
                        var paragraph = new W.Paragraph();
                        var run = new W.Run();
                        if (kind == "appendHeading")
                            run.Append(new W.RunProperties(new W.Bold(), new W.FontSize { Val = "28" }));
                        run.Append(new W.Text(content) { Space = SpaceProcessingModeValues.Preserve });
                        paragraph.Append(run);
                        AppendBeforeSection(body, paragraph);
                    }
                    else if (kind == "appendTable")
                    {
                        if (!operation.TryGetProperty("rows", out var rows) ||
                            !TryTable(rows, out W.Table? table))
                            return ToolReceipt.Fail("DOCUMENT_TABLE", "Provide 1-100 rows of 1-12 strings to create a table.");
                        AppendBeforeSection(body, table!);
                    }
                    else
                        return ToolReceipt.Fail("DOCUMENT_REVISION", "Unsupported document revision kind.");
                }
                wordDocument.Save();
            }
            // Reopening both candidate and published document checks OpenXML validity;
            // the exact candidate bytes are also compared after the exclusive rename.
            if (!DocumentOpens(temporary))
                return ToolReceipt.Fail("REVISION_VERIFY", "The candidate Word revision failed read-back validation.");
            return PublishRevision(source, destination, temporary, expectedSha256,
                "docx", operations.GetArrayLength(), DocumentOpens);
        }
        catch (Exception error) when (CanReportFailure(error) && !File.Exists(destination))
        {
            return ToolReceipt.Fail("DOCUMENT_REVISION_FAILED", "Word revision could not be completed: " + error.GetType().Name);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static ToolReceipt PublishRevision(string source, string destination,
        string temporary, string originalSha, string format, int revisions, Func<string, bool> verifies,
        int verifiedFormulaCells = 0)
    {
        // The source is never opened writable. An external change before publication
        // aborts without touching it. Exclusive rename refuses existing outputs.
        if (!string.Equals(Digest(source), originalSha, StringComparison.OrdinalIgnoreCase))
            return ToolReceipt.Fail("STALE_SOURCE", "The source changed during revision; no output was published.");
        string candidateSha = Digest(temporary);
        File.Move(temporary, destination, overwrite: false);
        if (!string.Equals(Digest(destination), candidateSha, StringComparison.OrdinalIgnoreCase) ||
            !verifies(destination))
            throw new IOException("Published revision could not be independently verified; reconciliation required.");
        if (!string.Equals(Digest(source), originalSha, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Source changed while publishing a revision; reconcile both files.");
        return ToolReceipt.Ok("Revised a new version, reopened it and verified original was preserved", new {
            sourcePath = source, outputPath = destination, sourceSha256 = originalSha.ToLowerInvariant(),
            outputSha256 = candidateSha, format, revisions, sourceUnchanged = true,
            contentVerified = true, originalRetainedAsBackup = true,
            verifiedFormulaCells, formulaTypeVerified = verifiedFormulaCells > 0
        }, changedState: true);
    }

    private static ToolReceipt? Preflight(string source, string dest, string ext, string expected)
    {
        if (!string.Equals(Path.GetExtension(source), ext, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetExtension(dest), ext, StringComparison.OrdinalIgnoreCase))
            return ToolReceipt.Fail("FORMAT_UNSUPPORTED", "Source and output must both be " + ext);
        if (Path.GetFullPath(source).Equals(Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase) ||
            File.Exists(dest) || Directory.Exists(dest))
            return ToolReceipt.Fail("ALREADY_EXISTS", "Revision requires a new unused output path; source is never overwritten.");
        if (!File.Exists(source)) return ToolReceipt.Fail("SOURCE_NOT_FOUND", "Source document does not exist.");
        if (!Directory.Exists(Path.GetDirectoryName(dest)))
            return ToolReceipt.Fail("PARENT_NOT_FOUND", "Create the output parent directory first.");
        if (expected.Length != 64 || expected.Any(c => !Uri.IsHexDigit(c)))
            return ToolReceipt.Fail("SOURCE_SHA", "Supply the actual 64-character SHA-256 from filesystem.sha256.");
        if (new FileInfo(source).Length > MaximumInputBytes)
            return ToolReceipt.Fail("SOURCE_TOO_LARGE", "Source exceeds 48 MiB.");
        if (!string.Equals(Digest(source), expected, StringComparison.OrdinalIgnoreCase))
            return ToolReceipt.Fail("STALE_SOURCE", "Source SHA-256 differs; inspect and hash the current file before revising.");
        ToolReceipt inspected = ProductivityDocuments.Inspect(source);
        return inspected.Success ? null : inspected;
    }

    private static bool WorkbookEditsMatch(string path, JsonElement edits)
    {
        using var workbook = new XLWorkbook(path);
        foreach (JsonElement edit in edits.EnumerateArray())
        {
            string name = edit.GetProperty("sheet").GetString()!;
            IXLWorksheet? worksheet = workbook.Worksheets.FirstOrDefault(w =>
                string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase));
            if (worksheet is null) return false;
            IXLCell cell = worksheet.Cell(edit.GetProperty("cell").GetString()!);
            if (edit.TryGetProperty("formula", out var expr))
            {
                if (!cell.HasFormula || !string.Equals(cell.FormulaA1.TrimStart('='),
                    (expr.GetString() ?? "").Trim().TrimStart('='), StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            else
            {
                JsonElement value = edit.GetProperty("value");
                if (cell.HasFormula) return false;
                bool equal = value.ValueKind switch {
                    JsonValueKind.String => cell.GetString() == value.GetString(),
                    JsonValueKind.Number => value.TryGetDouble(out double n) &&
                        Math.Abs(cell.GetDouble() - n) <= Math.Max(1e-10, Math.Abs(n) * 1e-12),
                    JsonValueKind.True => cell.GetBoolean(),
                    JsonValueKind.False => !cell.GetBoolean(),
                    _ => false
                };
                if (!equal) return false;
            }
            if (edit.TryGetProperty("numberFormat", out var fmt) &&
                cell.Style.NumberFormat.Format != fmt.GetString()) return false;
            if (edit.TryGetProperty("bold", out var bold) &&
                cell.Style.Font.Bold != bold.GetBoolean()) return false;
        }
        return true;
    }

    private static bool DocumentOpens(string path)
    {
        using var document = WordprocessingDocument.Open(path, false);
        return document.MainDocumentPart?.Document?.Body is not null;
    }

    private static bool SafeFormula(string value) => value.Length is > 0 and <= 1000 &&
        !value.Contains('[') && !value.Contains(']') && !value.Contains('|') &&
        !value.Contains("http", StringComparison.OrdinalIgnoreCase) &&
        !value.Contains("WEBSERVICE(", StringComparison.OrdinalIgnoreCase) &&
        !value.Contains("HYPERLINK(", StringComparison.OrdinalIgnoreCase) &&
        !value.Contains("DDE", StringComparison.OrdinalIgnoreCase);

    private static bool RequiredText(JsonElement obj, string property, out string value)
    {
        value = "";
        if (!obj.TryGetProperty(property, out var node) || node.ValueKind != JsonValueKind.String)
            return false;
        value = node.GetString() ?? "";
        return !string.IsNullOrWhiteSpace(value);
    }

    private static void AppendBeforeSection(W.Body body, OpenXmlElement child)
    {
        W.SectionProperties? section = body.Elements<W.SectionProperties>().FirstOrDefault();
        if (section is null) body.AppendChild(child);
        else body.InsertBefore(child, section);
    }

    private static bool TryTable(JsonElement data, out W.Table? table)
    {
        table = null;
        if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() is < 1 or > 100) return false;
        var candidate = new W.Table();
        candidate.Append(new W.TableProperties(new W.TableBorders(
            new W.TopBorder { Val = W.BorderValues.Single, Size = 4, Color = "CBD5E1" },
            new W.BottomBorder { Val = W.BorderValues.Single, Size = 4, Color = "CBD5E1" },
            new W.LeftBorder { Val = W.BorderValues.Single, Size = 4, Color = "CBD5E1" },
            new W.RightBorder { Val = W.BorderValues.Single, Size = 4, Color = "CBD5E1" },
            new W.InsideHorizontalBorder { Val = W.BorderValues.Single, Size = 4, Color = "CBD5E1" },
            new W.InsideVerticalBorder { Val = W.BorderValues.Single, Size = 4, Color = "CBD5E1" })));
        int? width = null;
        int rowNumber = 0;
        foreach (JsonElement row in data.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() is < 1 or > 12 ||
                (width is not null && row.GetArrayLength() != width)) return false;
            width ??= row.GetArrayLength();
            var tableRow = new W.TableRow();
            if (rowNumber++ == 0) tableRow.Append(new W.TableRowProperties(new W.TableHeader()));
            foreach (JsonElement value in row.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.String) return false;
                string text = value.GetString() ?? "";
                if (text.Length > 2000) return false;
                var run = new W.Run();
                if (rowNumber == 1) run.Append(new W.RunProperties(new W.Bold()));
                run.Append(new W.Text(text) { Space = SpaceProcessingModeValues.Preserve });
                tableRow.Append(new W.TableCell(new W.Paragraph(run)));
            }
            candidate.Append(tableRow);
        }
        table = candidate;
        return true;
    }

    private static string TemporaryFor(string destination, string ext) =>
        Path.Combine(Path.GetDirectoryName(destination)!, ".nira-revision-" + Guid.NewGuid().ToString("N") + ext);
    private static string Digest(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
    private static bool CanReportFailure(Exception error) => error is IOException or
        UnauthorizedAccessException or ArgumentException or InvalidOperationException or
        InvalidDataException or FormatException or NotSupportedException or System.Xml.XmlException;
}

public sealed class ProductivityReviseWorkbookTool(WorkspacePathResolver paths) : IAgentTool
{
    public ToolDescriptor Descriptor { get; } = new("productivity.revise_workbook",
        "Revise an EXISTING Excel .xlsx into a NEW version without changing the source. " +
        "First inspect/read and hash the source with filesystem.sha256. Choose an unused outputPath, " +
        "then send edits to existing sheets/cells. IMPORTANT: an executable Excel formula " +
        "MUST use the 'formula' field, e.g. formula:'SUM(B2:B5)'. Sending " +
        "value:'=SUM(B2:B5)' is NOT a formula and is rejected. " +
        "To deliberately store formula-looking text, set literalFormulaText:true with the value. " +
        "Supports number formats and bold formatting. Original is retained as rollback copy; " +
        "the new file is reopened and hashed. Files must be in workspace.",
        ToolRisk.WriteWorkspace, JsonToolSchema.Schema("""
        {"type":"object","properties":{
          "sourcePath":{"type":"string","minLength":1,"maxLength":4096},
          "outputPath":{"type":"string","minLength":1,"maxLength":4096},
          "expectedSha256":{"type":"string","minLength":64,"maxLength":64},
          "edits":{"type":"array","items":{"type":"object","properties":{
            "sheet":{"type":"string","minLength":1,"maxLength":31},
            "cell":{"type":"string","minLength":2,"maxLength":10},
            "value":{},"formula":{"type":"string","minLength":1,"maxLength":1000},
            "literalFormulaText":{"type":"boolean"},
            "numberFormat":{"type":"string","minLength":1,"maxLength":120},
            "bold":{"type":"boolean"}},"required":["sheet","cell"],"additionalProperties":false}}},
         "required":["sourcePath","outputPath","expectedSha256","edits"],"additionalProperties":false}
        """), HasSideEffects: true);

    public string GetTarget(JsonElement args) => paths.Resolve(args.GetProperty("outputPath").GetString());
    public Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct) => Task.Run(() =>
    {
        string source = paths.Resolve(args.GetProperty("sourcePath").GetString());
        string output = GetTarget(args);
        if (!WithinWorkspace(source, paths.Root) || !WithinWorkspace(output, paths.Root))
            return ToolReceipt.Fail("WORKSPACE_SCOPE", "Office revision inputs and outputs must be inside the authorized workspace.");
        return ProductivityRevisions.ReviseWorkbook(source, output,
            args.GetProperty("expectedSha256").GetString()!, args.GetProperty("edits"));
    }, ct);

    internal static bool WithinWorkspace(string path, string root)
    {
        string parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string child = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return child.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class ProductivityReviseDocumentTool(WorkspacePathResolver paths) : IAgentTool
{
    public ToolDescriptor Descriptor { get; } = new("productivity.revise_document",
        "Revise an EXISTING Word .docx into a NEW version; never overwrite the original. " +
        "Use productivity.read to identify actual paragraph text/index, filesystem.sha256 " +
        "for optimistic version check. Replace a simple 1-run paragraph without losing its style, " +
        "convert a known paragraph to a real Word table, or append headings/paragraphs/tables. " +
        "Requires an unused outputPath; input remains a complete rollback copy. " +
        "The new Word file is reopened and hashed. Files must be in workspace.",
        ToolRisk.WriteWorkspace, JsonToolSchema.Schema("""
        {"type":"object","properties":{
          "sourcePath":{"type":"string","minLength":1,"maxLength":4096},
          "outputPath":{"type":"string","minLength":1,"maxLength":4096},
          "expectedSha256":{"type":"string","minLength":64,"maxLength":64},
          "operations":{"type":"array","items":{"type":"object","properties":{
            "kind":{"type":"string","enum":["replaceParagraph","replaceParagraphWithTable","appendParagraph","appendHeading","appendTable"]},
            "paragraphIndex":{"type":"integer"},"expectedText":{"type":"string","maxLength":12000},
            "text":{"type":"string","maxLength":12000},
            "rows":{"type":"array","items":{"type":"array","items":{"type":"string","maxLength":2000}}}},
            "required":["kind"],"additionalProperties":false}}},
         "required":["sourcePath","outputPath","expectedSha256","operations"],"additionalProperties":false}
        """), HasSideEffects: true);

    public string GetTarget(JsonElement args) => paths.Resolve(args.GetProperty("outputPath").GetString());
    public Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct) => Task.Run(() =>
    {
        string source = paths.Resolve(args.GetProperty("sourcePath").GetString());
        string output = GetTarget(args);
        if (!ProductivityReviseWorkbookTool.WithinWorkspace(source, paths.Root) ||
            !ProductivityReviseWorkbookTool.WithinWorkspace(output, paths.Root))
            return ToolReceipt.Fail("WORKSPACE_SCOPE", "Office revision inputs and outputs must be inside the authorized workspace.");
        return ProductivityRevisions.ReviseDocument(source, output,
            args.GetProperty("expectedSha256").GetString()!, args.GetProperty("operations"));
    }, ct);
}
