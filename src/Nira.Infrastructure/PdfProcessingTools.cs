using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nira.Core;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Nira.Infrastructure;

/// <summary>Local, page-aware PDF text processing. No OCR or layout reconstruction is implied.</summary>
public static class PdfProcessing
{
    private const long MaxInputBytes = 48L * 1024 * 1024;
    private const int MaxExportPages = 300;
    private const int MaxExportCharacters = 2_000_000;

    public static ToolReceipt ExportText(string source, string destination, int startPage = 1,
        int? endPage = null, CancellationToken ct = default)
    {
        if (!string.Equals(Path.GetExtension(source), ".pdf", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetExtension(destination), ".txt", StringComparison.OrdinalIgnoreCase))
            return ToolReceipt.Fail("PDF_FORMAT", "Use a PDF source and a new .txt destination.");
        if (!File.Exists(source)) return ToolReceipt.Fail("FILE_NOT_FOUND", "PDF source not found.");
        if (File.Exists(destination) || Directory.Exists(destination))
            return ToolReceipt.Fail("ALREADY_EXISTS", "Destination already exists. Choose an unused filename.");
        string? parent = Path.GetDirectoryName(destination);
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
            return ToolReceipt.Fail("PARENT_NOT_FOUND", "Destination parent does not exist.");
        string temporary = Path.Combine(parent, ".nira-pdf-" + Guid.NewGuid().ToString("N") + ".tmp");
        bool published = false;
        try
        {
            ct.ThrowIfCancellationRequested();
            // Hold a read-only share while extracting and hashing; Windows denies concurrent
            // source writes/deletion. Never open the source for modification.
            using var sourceLock = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (sourceLock.Length > MaxInputBytes)
                return ToolReceipt.Fail("DOCUMENT_TOO_LARGE", "PDF exceeds 48 MiB.");
            string sourceSha256 = Digest(sourceLock);
            using var document = PdfDocument.Open(source);
            int lastPage = endPage ?? document.NumberOfPages;
            if (startPage < 1 || lastPage < startPage || lastPage > document.NumberOfPages)
                return ToolReceipt.Fail("PDF_PAGE_RANGE", "Use a valid inclusive 1-based page range.", new { pages = document.NumberOfPages });
            if ((long)lastPage - startPage + 1 > MaxExportPages)
                return ToolReceipt.Fail("PDF_PAGE_LIMIT", "Export at most 300 pages per file; choose smaller consecutive ranges.");
            var text = new StringBuilder();
            var noTextPages = new List<int>();
            int totalCharacters = 0;
            for (int page = startPage; page <= lastPage; page++)
            {
                ct.ThrowIfCancellationRequested();
                string content = ContentOrderTextExtractor.GetText(document.GetPage(page));
                bool empty = string.IsNullOrWhiteSpace(content);
                if ((long)text.Length + content.Length + 160 > MaxExportCharacters)
                    return ToolReceipt.Fail("PDF_TEXT_LIMIT", "Extraction exceeds two million characters; export a smaller page range. No output was saved.");
                text.Append("===== PDF page ").Append(page).Append(" =====\n");
                if (empty)
                {
                    noTextPages.Add(page);
                    text.Append("[No extractable text; page may be blank or require OCR.]\n");
                }
                else
                {
                    text.Append(content).Append('\n');
                    totalCharacters += content.Length;
                }
                text.Append('\n');
            }
            if (sourceSha256 != Digest(sourceLock))
                return ToolReceipt.Fail("PDF_SOURCE_CHANGED", "Source changed during extraction; no output published.");
            ct.ThrowIfCancellationRequested();
            byte[] expected = new UTF8Encoding(false, true).GetBytes(text.ToString());
            string sha256 = Convert.ToHexString(SHA256.HashData(expected)).ToLowerInvariant();
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                output.Write(expected);
                output.Flush(flushToDisk: true);
            }
            using (var check = File.OpenRead(temporary))
                if (check.Length != expected.Length || Digest(check) != sha256)
                    return ToolReceipt.Fail("PDF_EXPORT_VERIFY", "Temporary text verification failed. No output published.");
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: false);
            published = true;
            using var saved = File.OpenRead(destination);
            if (saved.Length != expected.Length || Digest(saved) != sha256)
                throw new IOException("Published PDF text export did not match extracted bytes; reconciliation required.");
            return ToolReceipt.Ok("Extracted PDF pages, reopened and verified the saved text file", new
            {
                sourcePath = source, outputPath = destination, sourceSha256, outputSha256 = sha256,
                startPage, endPage = lastPage, totalPages = document.NumberOfPages,
                exportedPages = lastPage - startPage + 1, textCharacters = totalCharacters,
                noTextPages, allPagesCovered = startPage == 1 && lastPage == document.NumberOfPages,
                ocrPerformed = false, sourceUnchanged = true, contentVerified = true,
                note = "Text extraction only; no-text pages are explicitly marked. Layout and OCR are not verified."
            }, changedState: true);
        }
        catch (Exception ex) when (!published && ex is not OperationCanceledException and not OutOfMemoryException)
        {
            // Parser versions can throw their own exception types. Before publication
            // a failure is safe to report; after publication uncertainty must reach the engine.
            return ToolReceipt.Fail("PDF_EXPORT_FAILED", "PDF export failed before publication: " + ex.GetType().Name);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static ToolReceipt Search(string path, string query, int startPage = 1,
        int maxPages = 30, CancellationToken ct = default)
    {
        if (!string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase))
            return ToolReceipt.Fail("PDF_FORMAT", "Use a PDF source.");
        if (!File.Exists(path)) return ToolReceipt.Fail("FILE_NOT_FOUND", "PDF source not found.");
        if (string.IsNullOrWhiteSpace(query) || query.Length > 300 || startPage < 1 || maxPages is < 1 or > 100)
            return ToolReceipt.Fail("PDF_SEARCH_BOUNDS", "Supply nonempty query <=300 characters, startPage>=1, maxPages=1..100.");
        try
        {
            using var sourceLock = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (sourceLock.Length > MaxInputBytes) return ToolReceipt.Fail("DOCUMENT_TOO_LARGE", "PDF exceeds 48 MiB.");
            using var document = PdfDocument.Open(path);
            if (startPage > document.NumberOfPages)
                return ToolReceipt.Fail("PAGE_NOT_FOUND", "Start page does not exist.", new { pages = document.NumberOfPages });
            int end = (int)Math.Min(document.NumberOfPages, (long)startPage + maxPages - 1);
            var hits = new List<object>();
            var noTextPages = new List<int>();
            int lastScanned = startPage - 1;
            for (int page = startPage; page <= end; page++)
            {
                ct.ThrowIfCancellationRequested();
                string text = ContentOrderTextExtractor.GetText(document.GetPage(page));
                lastScanned = page;
                if (string.IsNullOrWhiteSpace(text)) noTextPages.Add(page);
                int index = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                if (index < 0) continue;
                int offset = Math.Max(0, index - 120);
                int length = Math.Min(text.Length - offset, query.Length + 240);
                hits.Add(new { page, firstMatchOffset = index, snippet = text.Substring(offset, length) });
                if (hits.Count == 20) break;
            }
            return ToolReceipt.Ok("Searched extracted PDF text by page", new
            {
                path, query, startPage, lastScannedPage = lastScanned, totalPages = document.NumberOfPages,
                hits, noTextPages, nextPage = lastScanned < document.NumberOfPages ? (int?)(lastScanned + 1) : null,
                hasMore = lastScanned < document.NumberOfPages, ocrPerformed = false,
                note = "One first-match excerpt per matching page; no-text pages may be blank or require OCR."
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        { return ToolReceipt.Fail("PDF_SEARCH_FAILED", "PDF search failed: " + ex.GetType().Name); }
    }

    private static string Digest(Stream stream)
    {
        stream.Position = 0;
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}

public sealed class PdfExportTextTool(WorkspacePathResolver paths) : IAgentTool
{
    public ToolDescriptor Descriptor { get; } = new("productivity.pdf_export_text",
        "Extract actual PDF text into a NEW UTF-8 .txt file, with page markers, no-text page warnings, source/output SHA-256 and verified read-back. Defaults to all pages, max 300 per export. No OCR. Both paths must be inside the workspace. Do not repeat a successful export.",
        ToolRisk.WriteWorkspace, JsonToolSchema.Schema("""
        {"type":"object","properties":{
          "sourcePath":{"type":"string","minLength":1,"maxLength":4096},
          "outputPath":{"type":"string","minLength":1,"maxLength":4096},
          "startPage":{"type":"integer"},"endPage":{"type":"integer"}},
         "required":["sourcePath","outputPath"],"additionalProperties":false}
        """), HasSideEffects: true);
    public string GetTarget(JsonElement args) => paths.Resolve(args.GetProperty("outputPath").GetString());
    public Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct) => Task.Run(() =>
    {
        string source = paths.Resolve(args.GetProperty("sourcePath").GetString());
        string output = GetTarget(args);
        if (!ProductivityReviseWorkbookTool.WithinWorkspace(source, paths.Root) ||
            !ProductivityReviseWorkbookTool.WithinWorkspace(output, paths.Root))
            return ToolReceipt.Fail("WORKSPACE_SCOPE", "PDF export source and destination must both be inside the workspace.");
        int start = 1;
        int? end = null;
        if (args.TryGetProperty("startPage", out var a) && !a.TryGetInt32(out start))
            return ToolReceipt.Fail("PDF_PAGE_RANGE", "startPage must be an integer within range.");
        if (args.TryGetProperty("endPage", out var b))
        {
            if (!b.TryGetInt32(out int value)) return ToolReceipt.Fail("PDF_PAGE_RANGE", "endPage must be an integer within range.");
            end = value;
        }
        return PdfProcessing.ExportText(source, output, start, end, ct);
    }, ct);
}

public sealed class PdfSearchTool(WorkspacePathResolver paths) : IAgentTool
{
    public ToolDescriptor Descriptor { get; } = new("productivity.pdf_search",
        "Search PDF extracted text for a case-insensitive literal phrase. Returns first-match excerpts with page numbers, no-text pages and nextPage for continuation. Defaults to 30 pages, max 100, max 20 matching pages per call. No OCR.",
        ToolRisk.ReadLocal, JsonToolSchema.Schema("""
        {"type":"object","properties":{"path":{"type":"string","minLength":1,"maxLength":4096},
          "query":{"type":"string","minLength":1,"maxLength":300},
          "startPage":{"type":"integer"},"maxPages":{"type":"integer"}},
         "required":["path","query"],"additionalProperties":false}
        """));
    public string GetTarget(JsonElement args) => paths.Resolve(args.GetProperty("path").GetString());
    public Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct) => Task.Run(() =>
    {
        int start = 1, max = 30;
        if ((args.TryGetProperty("startPage", out var a) && !a.TryGetInt32(out start)) ||
            (args.TryGetProperty("maxPages", out var b) && !b.TryGetInt32(out max)))
            return ToolReceipt.Fail("PDF_SEARCH_BOUNDS", "Pagination fields must be integers within range.");
        return PdfProcessing.Search(GetTarget(args), args.GetProperty("query").GetString()!, Math.Max(1, start), Math.Clamp(max, 1, 100), ct);
    }, ct);
}
