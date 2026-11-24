using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;
using Nira.Core;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Nira.Infrastructure;

/// <summary>
/// General-purpose, model-driven Office/PDF capabilities. The agent decides
/// files, sheets, content and deliverables. The host owns authorization,
/// bounded reads, exclusive creation, independent read-back and receipts.
/// </summary>
public static class ProductivityDocuments
{
    private const long MaximumInputBytes = 48L * 1024 * 1024;
    private const long MaximumExpandedZipBytes = 180L * 1024 * 1024;

    public static ToolReceipt Inspect(string path)
    {
        ToolReceipt? invalid = ValidateInput(path);
        if (invalid is not null) return invalid;
        try
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".xlsx":
                    using (var workbook = new XLWorkbook(path))
                    {
                        var sheets = workbook.Worksheets.Take(80).Select(sheet => new
                        {
                            name = sheet.Name,
                            rows = sheet.LastRowUsed()?.RowNumber() ?? 0,
                            columns = sheet.LastColumnUsed()?.ColumnNumber() ?? 0
                        }).ToArray();
                        return ToolReceipt.Ok("Inspected Excel workbook", new
                        {
                            path, format = "xlsx", sheets, totalSheets = workbook.Worksheets.Count
                        });
                    }
                case ".docx":
                    using (var document = WordprocessingDocument.Open(path, false))
                    {
                        var body = document.MainDocumentPart?.Document?.Body;
                        if (body is null) return ToolReceipt.Fail("DOCUMENT_EMPTY", "No Word document body found.");
                        return ToolReceipt.Ok("Inspected Word document", new
                        {
                            path, format = "docx", paragraphs = body.Descendants<W.Paragraph>().Count(),
                            tables = body.Descendants<W.Table>().Count(), characters = body.InnerText.Length
                        });
                    }
                case ".pdf":
                    using (var document = PdfDocument.Open(path))
                        return ToolReceipt.Ok("Inspected PDF", new
                        {
                            path, format = "pdf", pages = document.NumberOfPages,
                            note = "Text extraction is available for text-based pages. Scanned pages require OCR."
                        });
                default:
                    return ToolReceipt.Fail("FORMAT_UNSUPPORTED", "Supported input formats: .xlsx, .docx, .pdf");
            }
        }
        catch (Exception ex) when (IsDocumentError(ex))
        { return ToolReceipt.Fail("DOCUMENT_INSPECT_FAILED", "Unable to inspect document: " + ex.GetType().Name); }
    }

    public static ToolReceipt Read(string path, string? sheet, int page,
        int startRow, int maxRows, int maxColumns, int startParagraph, int maxParagraphs)
    {
        ToolReceipt? invalid = ValidateInput(path);
        if (invalid is not null) return invalid;
        // Only validate bounds used by this document format. PDF page requests
        // do not require Excel/Word row counts and vice versa. The tool adapter
        // normalizes unsafe optional read-only bounds before calling this method.
        string kind = Path.GetExtension(path).ToLowerInvariant();
        if ((kind == ".pdf" && page < 1) ||
            (kind == ".xlsx" && (startRow < 1 || maxRows is < 1 or > 80 ||
                                 maxColumns is < 1 or > 40)) ||
            (kind == ".docx" && (startParagraph < 1 || maxParagraphs is < 1 or > 80)))
            return ToolReceipt.Fail("READ_BOUNDS",
                "Invalid read bounds for this file type. Excel: startRow>=1, " +
                "maxRows=1..80, maxColumns=1..40; Word: startParagraph>=1, " +
                "maxParagraphs=1..80; PDF: page>=1.");
        try
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".xlsx":
                    using (var workbook = new XLWorkbook(path))
                    {
                        IXLWorksheet? worksheet = string.IsNullOrWhiteSpace(sheet)
                            ? workbook.Worksheets.FirstOrDefault()
                            : workbook.Worksheets.FirstOrDefault(w =>
                                string.Equals(w.Name, sheet, StringComparison.OrdinalIgnoreCase));
                        if (worksheet is null)
                            return ToolReceipt.Fail("SHEET_NOT_FOUND", "Sheet not found; use productivity.inspect to list names.");
                        int lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;
                        int lastColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;
                        int columns = Math.Min(lastColumn, maxColumns);
                        var rows = new List<object>();
                        for (int row = startRow; row <= lastRow && row < (long)startRow + maxRows; row++)
                        {
                            var cells = new List<object>();
                            for (int column = 1; column <= columns; column++)
                            {
                                IXLCell cell = worksheet.Cell(row, column);
                                string display = cell.GetFormattedString(CultureInfo.InvariantCulture);
                                cells.Add(new { address = cell.Address.ToString(),
                                    value = Bound(display, 500), formula = cell.HasFormula,
                                    cellType = cell.HasFormula ? "formula" : cell.DataType.ToString(),
                                    formulaA1 = cell.HasFormula ? Bound(cell.FormulaA1, 500) : null });
                            }
                            rows.Add(new { row, cells });
                        }
                        return ToolReceipt.Ok("Read Excel cells", new
                        {
                            path, sheet = worksheet.Name, startRow, maxRows, maxColumns, lastRow,
                            lastColumn, rows, hasMore = (long)startRow + maxRows <= lastRow,
                            columnsTruncated = lastColumn > columns
                        });
                    }
                case ".docx":
                    using (var document = WordprocessingDocument.Open(path, false))
                    {
                        var body = document.MainDocumentPart?.Document?.Body;
                        if (body is null) return ToolReceipt.Fail("DOCUMENT_EMPTY", "No Word document body found.");
                        var paragraphs = body.Descendants<W.Paragraph>()
                            .Skip(startParagraph - 1).Take(maxParagraphs + 1)
                            .Select((p, i) => new { number = startParagraph + i,
                                style = p.ParagraphProperties?.ParagraphStyleId?.Val?.Value,
                                text = Bound(p.InnerText, 2400) }).ToArray();
                        bool hasMore = paragraphs.Length > maxParagraphs;
                        return ToolReceipt.Ok("Read Word paragraphs", new
                        {
                            path, startParagraph, maxParagraphs,
                            paragraphs = paragraphs.Take(maxParagraphs).ToArray(), hasMore
                        });
                    }
                case ".pdf":
                    using (var document = PdfDocument.Open(path))
                    {
                        if (page > document.NumberOfPages)
                            return ToolReceipt.Fail("PAGE_NOT_FOUND", "Requested PDF page does not exist.");
                        string text = ContentOrderTextExtractor.GetText(document.GetPage(page));
                        return ToolReceipt.Ok("Read PDF page", new
                        {
                            path, page, pages = document.NumberOfPages,
                            text = Bound(text, 16000), totalCharacters = text.Length,
                            truncated = text.Length > 16000,
                            requiresOcr = string.IsNullOrWhiteSpace(text)
                        });
                    }
                default:
                    return ToolReceipt.Fail("FORMAT_UNSUPPORTED", "Supported input formats: .xlsx, .docx, .pdf");
            }
        }
        catch (Exception ex) when (IsDocumentError(ex))
        { return ToolReceipt.Fail("DOCUMENT_READ_FAILED", "Unable to read document: " + ex.GetType().Name); }
    }

    public static ToolReceipt CreateWorkbook(string path, JsonElement sheets)
    {
        ToolReceipt? invalid = ValidateOutput(path, ".xlsx");
        if (invalid is not null) return invalid;
        if (sheets.ValueKind != JsonValueKind.Array || sheets.GetArrayLength() is < 1 or > 24)
            return ToolReceipt.Fail("WORKBOOK_SHAPE", "Specify 1-24 sheets.");
        string? parent = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(parent)) return ToolReceipt.Fail("PARENT_NOT_FOUND", "Destination parent is missing.");
        string temporary = Path.Combine(parent, ".nira-office-" + Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            using (var workbook = new XLWorkbook())
            {
                int worksheetNumber = 0;
                long totalCells = 0;
                var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (JsonElement sheet in sheets.EnumerateArray())
                {
                    if (sheet.ValueKind != JsonValueKind.Object || !sheet.TryGetProperty("name", out var nameValue) ||
                        nameValue.ValueKind != JsonValueKind.String || !sheet.TryGetProperty("rows", out var rowValues) ||
                        rowValues.ValueKind != JsonValueKind.Array)
                        return ToolReceipt.Fail("WORKBOOK_SHAPE", "Each sheet requires name and rows.");
                    string name = nameValue.GetString() ?? "";
                    if (name.Length is < 1 or > 31 || name.IndexOfAny(['[', ']', ':', '*', '?', '/', '\\']) >= 0 ||
                        !seenNames.Add(name))
                        return ToolReceipt.Fail("SHEET_NAME", "Sheet names must be unique and valid Excel names (max 31 chars).");
                    if (rowValues.GetArrayLength() > 2500)
                        return ToolReceipt.Fail("WORKBOOK_LIMIT", "Maximum 2500 rows per worksheet.");
                    IXLWorksheet worksheet = workbook.Worksheets.Add(name);
                    int rowNumber = 0;
                    foreach (JsonElement row in rowValues.EnumerateArray())
                    {
                        rowNumber++;
                        if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() > 60)
                            return ToolReceipt.Fail("WORKBOOK_SHAPE", "Each row must be an array of up to 60 values.");
                        totalCells += row.GetArrayLength();
                        if (totalCells > 100000)
                            return ToolReceipt.Fail("WORKBOOK_LIMIT", "Maximum 100000 cells across the workbook.");
                        int columnNumber = 0;
                        foreach (JsonElement value in row.EnumerateArray())
                        {
                            columnNumber++;
                            IXLCell cell = worksheet.Cell(rowNumber, columnNumber);
                            switch (value.ValueKind)
                            {
                                case JsonValueKind.Null: break;
                                case JsonValueKind.String:
                                    string contents = value.GetString() ?? "";
                                    if (contents.Length > 12000)
                                        return ToolReceipt.Fail("CELL_TOO_LONG", "A cell exceeds 12000 characters.");
                                    // SetValue on a string deliberately does NOT execute a formula.
                                    cell.SetValue(contents);
                                    break;
                                case JsonValueKind.True: cell.SetValue(true); break;
                                case JsonValueKind.False: cell.SetValue(false); break;
                                case JsonValueKind.Number:
                                    if (!value.TryGetDouble(out double number) || !double.IsFinite(number))
                                        return ToolReceipt.Fail("CELL_NUMBER", "An Excel numeric value is not finite.");
                                    cell.SetValue(number);
                                    break;
                                default:
                                    return ToolReceipt.Fail("CELL_TYPE", "Use strings, finite numbers, booleans, or null only.");
                            }
                        }
                    }
                    if (rowNumber > 0)
                    {
                        worksheet.Row(1).Style.Font.Bold = true;
                        worksheet.SheetView.FreezeRows(1);
                    }
                    worksheetNumber++;
                }
                workbook.SaveAs(temporary);
                if (worksheetNumber != sheets.GetArrayLength())
                    return ToolReceipt.Fail("WORKBOOK_SHAPE", "Workbook sheet count differs from request.");
            }
            return PublishAndVerify(temporary, path, ".xlsx", sheets.GetArrayLength(),
                saved => VerifyWorkbook(saved, sheets));
        }
        catch (Exception ex) when (IsDocumentError(ex) && !File.Exists(path))
        { return ToolReceipt.Fail("WORKBOOK_CREATE_FAILED", "Unable to create workbook: " + ex.GetType().Name); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static ToolReceipt CreateDocument(string path, string title, JsonElement blocks)
    {
        ToolReceipt? invalid = ValidateOutput(path, ".docx");
        if (invalid is not null) return invalid;
        if (string.IsNullOrWhiteSpace(title) || title.Length > 400 ||
            blocks.ValueKind != JsonValueKind.Array || blocks.GetArrayLength() > 300)
            return ToolReceipt.Fail("DOCUMENT_SHAPE", "Title and up to 300 content blocks are required.");
        string? parent = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(parent)) return ToolReceipt.Fail("PARENT_NOT_FOUND", "Destination parent is missing.");
        string temporary = Path.Combine(parent, ".nira-office-" + Guid.NewGuid().ToString("N") + ".docx");
        try
        {
            using (var document = WordprocessingDocument.Create(temporary, WordprocessingDocumentType.Document))
            {
                MainDocumentPart main = document.AddMainDocumentPart();
                var body = new W.Body();
                main.Document = new W.Document(body);
                body.Append(Paragraph(title, heading: true, level: 1));
                foreach (JsonElement block in blocks.EnumerateArray())
                {
                    if (block.ValueKind != JsonValueKind.Object ||
                        !block.TryGetProperty("kind", out var kindValue) ||
                        kindValue.ValueKind != JsonValueKind.String)
                        return ToolReceipt.Fail("DOCUMENT_SHAPE", "Every block needs a kind.");
                    string? kind = kindValue.GetString();
                    if (kind is "paragraph" or "heading")
                    {
                        if (!block.TryGetProperty("text", out var textValue) ||
                            textValue.ValueKind != JsonValueKind.String)
                            return ToolReceipt.Fail("DOCUMENT_SHAPE", "Paragraph/heading blocks need text.");
                        string content = textValue.GetString() ?? "";
                        if (content.Length > 12000)
                            return ToolReceipt.Fail("DOCUMENT_LIMIT", "Block text exceeds 12000 characters.");
                        int level = block.TryGetProperty("level", out var levelValue) &&
                            levelValue.ValueKind == JsonValueKind.Number && levelValue.TryGetInt32(out int parsed)
                            ? Math.Clamp(parsed, 1, 3) : 2;
                        body.Append(Paragraph(content, kind == "heading", level));
                    }
                    else if (kind == "table")
                    {
                        if (!block.TryGetProperty("rows", out var rows) ||
                            rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() is < 1 or > 500)
                            return ToolReceipt.Fail("DOCUMENT_SHAPE", "Table needs 1-500 rows.");
                        var table = new W.Table();
                        foreach (JsonElement row in rows.EnumerateArray())
                        {
                            if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() is < 1 or > 20)
                                return ToolReceipt.Fail("DOCUMENT_SHAPE", "Table rows require 1-20 text cells.");
                            var tableRow = new W.TableRow();
                            foreach (JsonElement item in row.EnumerateArray())
                            {
                                if (item.ValueKind != JsonValueKind.String || (item.GetString()?.Length ?? 0) > 4000)
                                    return ToolReceipt.Fail("DOCUMENT_SHAPE", "Table cells require bounded text.");
                                tableRow.Append(new W.TableCell(Paragraph(item.GetString() ?? "", false, 2)));
                            }
                            table.Append(tableRow);
                        }
                        body.Append(table);
                    }
                    else return ToolReceipt.Fail("DOCUMENT_SHAPE", "Block kind must be paragraph, heading, or table.");
                }
                main.Document.Save();
            }
            return PublishAndVerify(temporary, path, ".docx", blocks.GetArrayLength(),
                saved => VerifyDocument(saved, title, blocks));
        }
        catch (Exception ex) when (IsDocumentError(ex) && !File.Exists(path))
        { return ToolReceipt.Fail("DOCUMENT_CREATE_FAILED", "Unable to create Word document: " + ex.GetType().Name); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static W.Paragraph Paragraph(string content, bool heading, int level)
    {
        var properties = new W.RunProperties();
        if (heading)
        {
            properties.Append(new W.Bold());
            properties.Append(new W.FontSize { Val = level == 1 ? "32" : level == 2 ? "26" : "22" });
        }
        var run = new W.Run(properties, new W.Text(content) { Space = SpaceProcessingModeValues.Preserve });
        return new W.Paragraph(run);
    }

    private static ToolReceipt PublishAndVerify(string temporary, string target,
        string format, int units, Func<string, bool> verifyContent)
    {
        // Same-directory rename is exclusive: never replace another user's file.
        File.Move(temporary, target, overwrite: false);
        if (!verifyContent(target))
            throw new IOException("Published document failed full content verification. Manual reconciliation required.");
        using var stream = File.OpenRead(target);
        string sha = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        return ToolReceipt.Ok("Created, fully read back, and hashed Office deliverable", new
        {
            path = target, format = format.TrimStart('.'), bytes = stream.Length,
            sha256 = sha, units, contentVerified = true
        }, changedState: true);
    }

    private static bool VerifyWorkbook(string path, JsonElement expectedSheets)
    {
        using var workbook = new XLWorkbook(path);
        if (workbook.Worksheets.Count != expectedSheets.GetArrayLength()) return false;
        foreach (JsonElement sheet in expectedSheets.EnumerateArray())
        {
            string name = sheet.GetProperty("name").GetString()!;
            IXLWorksheet? worksheet = workbook.Worksheets.FirstOrDefault(item => item.Name == name);
            if (worksheet is null) return false;
            int rowIndex = 0;
            foreach (JsonElement row in sheet.GetProperty("rows").EnumerateArray())
            {
                rowIndex++;
                int columnIndex = 0;
                foreach (JsonElement expected in row.EnumerateArray())
                {
                    columnIndex++;
                    IXLCell cell = worksheet.Cell(rowIndex, columnIndex);
                    if (cell.HasFormula) return false; // Input text never becomes an executable formula.
                    bool equal = expected.ValueKind switch
                    {
                        JsonValueKind.Null => cell.IsEmpty(),
                        JsonValueKind.String => cell.GetString() == expected.GetString(),
                        JsonValueKind.True => cell.GetBoolean(),
                        JsonValueKind.False => !cell.GetBoolean(),
                        JsonValueKind.Number => expected.TryGetDouble(out double number) &&
                            Math.Abs(cell.GetDouble() - number) <= Math.Max(1e-10, Math.Abs(number) * 1e-12),
                        _ => false
                    };
                    if (!equal) return false;
                }
            }
        }
        return true;
    }

    private static bool VerifyDocument(string path, string title, JsonElement expectedBlocks)
    {
        using var document = WordprocessingDocument.Open(path, false);
        W.Body? body = document.MainDocumentPart?.Document?.Body;
        if (body is null) return false;
        OpenXmlElement[] elements = body.ChildElements
            .Where(element => element is W.Paragraph or W.Table).ToArray();
        if (elements.Length != expectedBlocks.GetArrayLength() + 1 ||
            elements[0] is not W.Paragraph heading || heading.InnerText != title) return false;
        int blockIndex = 0;
        foreach (JsonElement expected in expectedBlocks.EnumerateArray())
        {
            OpenXmlElement element = elements[++blockIndex];
            string kind = expected.GetProperty("kind").GetString()!;
            if (kind is "paragraph" or "heading")
            {
                if (element is not W.Paragraph paragraph ||
                    paragraph.InnerText != expected.GetProperty("text").GetString()) return false;
                continue;
            }
            if (element is not W.Table table) return false;
            W.TableRow[] rows = table.Elements<W.TableRow>().ToArray();
            JsonElement expectedRows = expected.GetProperty("rows");
            if (rows.Length != expectedRows.GetArrayLength()) return false;
            for (int i = 0; i < rows.Length; i++)
            {
                W.TableCell[] cells = rows[i].Elements<W.TableCell>().ToArray();
                JsonElement row = expectedRows[i];
                if (cells.Length != row.GetArrayLength()) return false;
                for (int column = 0; column < cells.Length; column++)
                    if (cells[column].InnerText != row[column].GetString()) return false;
            }
        }
        return true;
    }

    private static ToolReceipt? ValidateInput(string path)
    {
        if (Path.GetExtension(path).ToLowerInvariant() is not (".xlsx" or ".docx" or ".pdf"))
            return ToolReceipt.Fail("FORMAT_UNSUPPORTED", "Supported input formats: .xlsx, .docx, .pdf");
        if (!File.Exists(path)) return ToolReceipt.Fail("FILE_NOT_FOUND", "The document does not exist.");
        long size = new FileInfo(path).Length;
        if (size > MaximumInputBytes)
            return ToolReceipt.Fail("DOCUMENT_TOO_LARGE", "Document exceeds the 48 MiB input limit.");
        if (Path.GetExtension(path).ToLowerInvariant() is ".xlsx" or ".docx")
        {
            try
            {
                using var archive = ZipFile.OpenRead(path);
                if (archive.Entries.Count > 5000)
                    return ToolReceipt.Fail("OFFICE_ARCHIVE_LIMIT", "Office archive exceeds safe entry-count limit.");
                long remaining = MaximumExpandedZipBytes;
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (entry.Length > remaining)
                        return ToolReceipt.Fail("OFFICE_ARCHIVE_LIMIT", "Office archive exceeds safe expansion limits.");
                    remaining -= entry.Length;
                }
            }
            catch (InvalidDataException)
            { return ToolReceipt.Fail("OFFICE_ARCHIVE_INVALID", "Office file is not a valid ZIP container."); }
        }
        return null;
    }

    private static ToolReceipt? ValidateOutput(string path, string extension)
    {
        if (!string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase))
            return ToolReceipt.Fail("OUTPUT_FORMAT", "Destination must end in " + extension);
        if (File.Exists(path) || Directory.Exists(path))
            return ToolReceipt.Fail("ALREADY_EXISTS", "Destination exists; no overwrite is allowed.");
        string? parent = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
            return ToolReceipt.Fail("PARENT_NOT_FOUND", "Create the parent directory first.");
        return null;
    }

    private static string Bound(string content, int max) =>
        content.Length <= max ? content : content[..max];

    private static bool IsDocumentError(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or InvalidDataException or
        ArgumentException or InvalidOperationException or FormatException or
        System.Xml.XmlException or NotSupportedException;
}

public sealed class ProductivityInspectTool(WorkspacePathResolver resolver) : IAgentTool
{
    public ToolDescriptor Descriptor { get; } = new("productivity.inspect",
        "Inspect an existing Excel .xlsx, Word .docx or text-based PDF file, reporting workbook sheets, document paragraphs or PDF page count. Use before productivity.read.",
        ToolRisk.ReadLocal, JsonToolSchema.Schema("""
        {"type":"object","properties":{"path":{"type":"string","minLength":1,"maxLength":4096}},
         "required":["path"],"additionalProperties":false}
        """));
    public string GetTarget(JsonElement args) => resolver.Resolve(args.GetProperty("path").GetString());
    public Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct) =>
        Task.Run(() => ProductivityDocuments.Inspect(GetTarget(args)), ct);
}

public sealed class ProductivityReadTool(WorkspacePathResolver resolver) : IAgentTool
{
    public ToolDescriptor Descriptor { get; } = new("productivity.read",
        "Read actual content of an Excel worksheet, Word paragraphs, or one PDF page. " +
        "All optional pagination values use 1-based positions. Missing or nonpositive read-only " +
        "positions become 1; nonpositive/oversized counts are safely bounded " +
        "(Excel maxRows 1..80, maxColumns 1..40; Word maxParagraphs 1..80). " +
        "The receipt reports effective bounds. Use productivity.inspect to discover real " +
        "sheet names and PDF page counts. For scanned PDFs, report OCR is needed.",
        ToolRisk.ReadLocal, JsonToolSchema.Schema("""
        {"type":"object","properties":{
          "path":{"type":"string","minLength":1,"maxLength":4096},
          "sheet":{"type":"string","minLength":1,"maxLength":60},
          "page":{"type":"integer"},"startRow":{"type":"integer"},
          "maxRows":{"type":"integer"},"maxColumns":{"type":"integer"},
          "startParagraph":{"type":"integer"},"maxParagraphs":{"type":"integer"}},
         "required":["path"],"additionalProperties":false}
        """));
    public string GetTarget(JsonElement args) => resolver.Resolve(args.GetProperty("path").GetString());
    public Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct) =>
        Task.Run(() => ProductivityDocuments.Read(GetTarget(args),
            args.TryGetProperty("sheet", out var s) ? s.GetString() : null,
            Start(args, "page"), Start(args, "startRow"),
            Count(args, "maxRows", 30, 80), Count(args, "maxColumns", 24, 40),
            Start(args, "startParagraph"), Count(args, "maxParagraphs", 30, 80)), ct);

    // Host-side read-only adaptation: the model may use page=0, request 100
    // rows, or supply irrelevant format options. Never reject a safe read just
    // because optional pagination values were unsuitable. This does NOT affect
    // write tools, authorization, selected files, or chosen document content.
    public static int Start(JsonElement args, string name) =>
        Math.Max(1, Number(args, name, 1));
    public static int Count(JsonElement args, string name, int fallback, int maximum) =>
        Math.Clamp(Number(args, name, fallback), 1, maximum);
    private static int Number(JsonElement args, string name, int fallback) =>
        args.TryGetProperty(name, out var value) && value.TryGetInt32(out int number)
            ? number : fallback;
}

public sealed class ProductivityCreateWorkbookTool(WorkspacePathResolver resolver) : IAgentTool
{
    public ToolDescriptor Descriptor { get; } = new("productivity.create_workbook",
        "Create a NEW .xlsx workbook with model-selected sheet names and typed table values. Supports any requested content, numbers and booleans. Preserve identifiers and leading zeros by writing them as strings. Does not execute spreadsheet formulas or overwrite files; full saved-cell read-back and SHA256 receipt.",
        ToolRisk.WriteWorkspace, JsonToolSchema.Schema("""
        {"type":"object","properties":{
          "path":{"type":"string","minLength":1,"maxLength":4096},
          "sheets":{"type":"array","items":{"type":"object","properties":{
            "name":{"type":"string","minLength":1,"maxLength":31},
            "rows":{"type":"array","items":{"type":"array","items":{}}}},
            "required":["name","rows"],"additionalProperties":false}}},
         "required":["path","sheets"],"additionalProperties":false}
        """), HasSideEffects: true);
    public string GetTarget(JsonElement args) => resolver.Resolve(args.GetProperty("path").GetString());
    public Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct) =>
        Task.Run(() => ProductivityDocuments.CreateWorkbook(GetTarget(args), args.GetProperty("sheets")), ct);
}

public sealed class ProductivityCreateDocumentTool(WorkspacePathResolver resolver) : IAgentTool
{
    public ToolDescriptor Descriptor { get; } = new("productivity.create_document",
        "Create a NEW Word .docx document with model-selected title, headings, paragraphs and tables. Never overwrite existing files; independently reopen and hash the saved deliverable.",
        ToolRisk.WriteWorkspace, JsonToolSchema.Schema("""
        {"type":"object","properties":{
          "path":{"type":"string","minLength":1,"maxLength":4096},
          "title":{"type":"string","minLength":1,"maxLength":400},
          "blocks":{"type":"array","items":{"type":"object","properties":{
            "kind":{"type":"string","enum":["paragraph","heading","table"]},
            "text":{"type":"string","maxLength":12000},
            "level":{"type":"integer"},"rows":{"type":"array","items":{"type":"array","items":{"type":"string"}}}},
            "required":["kind"],"additionalProperties":false}}},
         "required":["path","title","blocks"],"additionalProperties":false}
        """), HasSideEffects: true);
    public string GetTarget(JsonElement args) => resolver.Resolve(args.GetProperty("path").GetString());
    public Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct) =>
        Task.Run(() => ProductivityDocuments.CreateDocument(GetTarget(args),
            args.GetProperty("title").GetString()!, args.GetProperty("blocks")), ct);
}
