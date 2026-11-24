using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nira.Core;

namespace Nira.Infrastructure;

public sealed record OcrPageResult(bool Success, string ErrorCode, string Text, string Language,
    int TotalPages, int Width, int Height)
{
    public string Backend { get; init; } = "windows-ocr";
    public string Diagnostic { get; init; } = "";
}

/// <summary>Read one actual raster image or PDF page, without modifying the input.</summary>
public sealed class OcrReadTool(WorkspacePathResolver paths,
    Func<string, int, string, CancellationToken, Task<OcrPageResult>>? recognize = null) : IAgentTool
{
    private readonly Func<string, int, string, CancellationToken, Task<OcrPageResult>> _recognize =
        recognize ?? WindowsOcrWorker.RecognizeWithFallbackAsync;
    public ToolDescriptor Descriptor { get; } = new("productivity.ocr_read",
        "Local CPU OCR for PNG, JPG, JPEG, BMP, or one rendered PDF page. Uses installed RapidOCR for English/default recognition, and Windows OCR for other installed language tags. Returns recognized text, page and language; accuracy is not guaranteed. Use exact observed paths. Page defaults to 1 (1-based); Omit languageTag for default recognition; use en or en-US for English. Other languages require installed Windows OCR support. Do not retry unchanged failures or guess external command-line programs. For more PDF pages call with their page numbers. Use filesystem.write_text or Office tools to save results if requested. No network or file writes.",
        ToolRisk.ReadLocal, JsonToolSchema.Schema("""
        {"type":"object","properties":{"path":{"type":"string","minLength":1,"maxLength":4096},
        "page":{"type":"integer"},"languageTag":{"type":"string","minLength":2,"maxLength":40}},
        "required":["path"],"additionalProperties":false}
        """));
    public string GetTarget(JsonElement args) => paths.Resolve(args.GetProperty("path").GetString());

    public async Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        string path = GetTarget(args);
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".pdf" or ".png" or ".jpg" or ".jpeg" or ".bmp"))
            return ToolReceipt.Fail("OCR_FORMAT", "Use a PDF, PNG, JPG, JPEG or BMP file.");
        int page = 1;
        if (args.TryGetProperty("page", out var p) && !p.TryGetInt32(out page))
            return ToolReceipt.Fail("OCR_PAGE_RANGE", "Page must be a 1-based integer.");
        page = Math.Max(1, page);
        if (extension != ".pdf" && page != 1)
            return ToolReceipt.Fail("OCR_PAGE_RANGE", "Images have only page 1.");
        string language = args.TryGetProperty("languageTag", out var l) ? l.GetString() ?? "" : "";
        if (language.Length > 40 || language.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            return ToolReceipt.Fail("OCR_LANGUAGE", "Use an installed Windows OCR language tag, e.g. en-US, or omit it.");
        if (!File.Exists(path)) return ToolReceipt.Fail("FILE_NOT_FOUND", "OCR source file not found.", new { path });
        try
        {
            ct.ThrowIfCancellationRequested();
            using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (source.Length > 48L * 1024 * 1024)
                return ToolReceipt.Fail("DOCUMENT_TOO_LARGE", "OCR input exceeds 48 MiB.");
            string sourceSha256 = Digest(source);
            OcrPageResult result = await _recognize(path, page, language, ct);
            ct.ThrowIfCancellationRequested();
            if (!result.Success)
                return ToolReceipt.Fail(result.ErrorCode, result.ErrorCode switch
                {
                    "OCR_LANGUAGE_UNAVAILABLE" => "No matching OCR language is available. RapidOCR supports default English recognition; other tags require a matching Windows OCR language.",
                    "OCR_BACKENDS_FAILED" => "Both local OCR backends failed. Inspect backend diagnostics; do not repeat unchanged calls.",
                    "OCR_WORKER_FAILED" => "OCR subprocess exited unsuccessfully; see backend diagnostic.",
                    "OCR_INVALID_RESULT" => "OCR subprocess did not return a valid recognition receipt.",
                    "OCR_UNAVAILABLE" => "Local OCR runtime is unavailable; install or repair the OCR runtime.",
                    "OCR_PAGE_RANGE" => "Requested PDF page does not exist.",
                    "OCR_IMAGE_SIZE" => "Image exceeds the supported OCR dimensions; use a smaller image.",
                    "OCR_TIMEOUT" => "Local OCR exceeded its time limit; try one smaller page or image.",
                    "OCR_WINRT_INIT" => "Windows Runtime OCR initialization failed. Check Windows PowerShell 5.1 and Windows OCR availability.",
                    "OCR_OPEN_FAILED" => "Windows could not open the source image or PDF.",
                    "OCR_RENDER_FAILED" => "Windows could not render the selected PDF page.",
                    "OCR_DECODE_FAILED" => "Windows could not decode the source bitmap.",
                    "OCR_RECOGNITION_FAILED" => "The installed Windows OCR engine could not recognize this bitmap.",
                    _ => "Local Windows OCR could not read the source. No content was inferred."
                }, new { path, page, totalPages = result.TotalPages, backend = result.Backend, diagnostic = result.Diagnostic });
            if (result.TotalPages < page || result.Width < 1 || result.Height < 1 ||
                string.IsNullOrWhiteSpace(result.Language) || result.Text is null)
                return ToolReceipt.Fail("OCR_INVALID_RESULT", "OCR worker did not return a valid observation.");
            if (sourceSha256 != Digest(source))
                return ToolReceipt.Fail("OCR_SOURCE_CHANGED", "Source changed during OCR; results discarded.");
            const int textLimit = 24000;
            string text = result.Text.Length > textLimit ? result.Text[..textLimit] : result.Text;
            return ToolReceipt.Ok("Recognized text from the actual source image or rendered PDF page", new
            {
                path, sourceSha256, page, totalPages = result.TotalPages,
                language = result.Language, backend = result.Backend, width = result.Width, height = result.Height,
                text, textCharacters = result.Text.Length, truncated = result.Text.Length > textLimit,
                noRecognizedText = string.IsNullOrWhiteSpace(result.Text),
                nextPage = page < result.TotalPages ? (int?)(page + 1) : null,
                ocrPerformed = true, sourceUnchanged = true,
                note = "OCR is an estimate, not verified transcription. No recognized text does not prove a blank page. RapidOCR default model recognizes Chinese/English; other languages require an appropriate engine."
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or JsonException or InvalidOperationException)
        { return ToolReceipt.Fail("OCR_FAILED", "OCR could not complete: " + ex.GetType().Name); }
    }

    private static string Digest(Stream input)
    {
        input.Position = 0;
        return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
    }
}

/// <summary>Fixed local WinRT adapter. Model arguments are data, never PowerShell source.</summary>
internal static class WindowsOcrWorker
{
    private const int CaptureLimit = 512000;
    internal static async Task<OcrPageResult> RecognizeWithFallbackAsync(string path, int page, string language, CancellationToken ct)
    {
        string python = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ELVARA", "NIRA", "OcrRuntime-M3D1", "Scripts", "python.exe");
        bool english = string.IsNullOrEmpty(language) || language.Equals("en", StringComparison.OrdinalIgnoreCase) ||
            language.StartsWith("en-", StringComparison.OrdinalIgnoreCase);
        OcrPageResult? cpu = null;
        if (english && File.Exists(python))
        {
            cpu = await RunWorkerAsync(python, ["-I", "-c", PythonScript], "rapidocr-cpu", path, page, language, ct);
            if (cpu.Success || cpu.ErrorCode is "OCR_PAGE_RANGE" or "OCR_IMAGE_SIZE") return cpu;
        }
        string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        string executable = Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe");
        OcrPageResult windows = !OperatingSystem.IsWindows() || !File.Exists(executable)
            ? Failure("OCR_UNAVAILABLE", "windows-ocr")
            : await RunWorkerAsync(executable, ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand",
                Convert.ToBase64String(Encoding.Unicode.GetBytes(Script))], "windows-ocr", path, page, language, ct);
        if (windows.Success || cpu is null) return windows;
        return Failure("OCR_BACKENDS_FAILED", "rapidocr-cpu + windows-ocr",
            $"RapidOCR: {cpu.ErrorCode} {cpu.Diagnostic}; Windows: {windows.ErrorCode} {windows.Diagnostic}");
    }

    private static async Task<OcrPageResult> RunWorkerAsync(string executable, string[] args, string backend,
        string path, int page, string language, CancellationToken ct)
    {
        string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = system, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (string arg in args) start.ArgumentList.Add(arg);
        start.Environment.Clear();
        foreach (string key in new[] { "SystemRoot", "WINDIR", "OS", "PATH", "TEMP", "TMP", "USERPROFILE", "LOCALAPPDATA", "APPDATA" })
        {
            string? value = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrEmpty(value)) start.Environment[key] = value;
        }
        start.Environment["NIRA_OCR_PATH"] = path;
        start.Environment["NIRA_OCR_PAGE"] = page.ToString(CultureInfo.InvariantCulture);
        start.Environment["NIRA_OCR_LANGUAGE"] = language;
        start.Environment["OMP_NUM_THREADS"] = "2";
        start.Environment["OPENBLAS_NUM_THREADS"] = "2";
        using var process = new Process { StartInfo = start };
        ct.ThrowIfCancellationRequested();
        try { if (!process.Start()) return Failure("OCR_UNAVAILABLE", backend); }
        catch (Win32Exception ex) { return Failure("OCR_UNAVAILABLE", backend, "Win32 error " + ex.NativeErrorCode); }
        process.StandardInput.Close();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(90));
        Task<string?> stdout = CaptureAsync(process.StandardOutput, deadline.Token);
        Task<string?> stderr = CaptureAsync(process.StandardError, deadline.Token);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            string?[] capture = await Task.WhenAll(stdout, stderr).WaitAsync(deadline.Token);
            if (process.ExitCode != 0 || capture[0] is not { } output)
                return Failure("OCR_WORKER_FAILED", backend, "exit=" + process.ExitCode.ToString(CultureInfo.InvariantCulture));
            const string marker = "NIRA_OCR_RESULT:";
            string[] records = output.Split('\n').Select(line => line.Trim())
                .Where(line => line.StartsWith(marker, StringComparison.Ordinal)).ToArray();
            if (records.Length != 1) return Failure("OCR_INVALID_RESULT", backend, "missing or duplicate result marker");
            byte[] bytes = Convert.FromBase64String(records[0][marker.Length..]);
            OcrPageResult? result = JsonSerializer.Deserialize<OcrPageResult>(bytes);
            return result is null ? Failure("OCR_INVALID_RESULT", backend) : result with { Backend = backend };
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            using var stopped = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(stopped.Token);
            ct.ThrowIfCancellationRequested();
            return Failure("OCR_TIMEOUT", backend);
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        { return Failure("OCR_INVALID_RESULT", backend, ex.GetType().Name); }
        finally { deadline.Cancel(); }
    }

    private static OcrPageResult Failure(string code, string backend, string diagnostic = "") =>
        new(false, code, "", "", 0, 0, 0) { Backend = backend, Diagnostic = diagnostic };

    private static async Task<string?> CaptureAsync(StreamReader reader, CancellationToken ct)
    {
        var data = new StringBuilder();
        var buffer = new char[2048];
        bool exceeded = false;
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), ct)) != 0)
        {
            if (data.Length + count <= CaptureLimit && !exceeded) data.Append(buffer, 0, count);
            else exceeded = true;
        }
        return exceeded ? null : data.ToString();
    }

    // A fixed script shipped in the compiled application. Input stays in environment
    // values; there is no interpolation, evaluation, shell command, or remote access.
    private const string Script = """
        $ErrorActionPreference = 'Stop'
        $ProgressPreference = 'SilentlyContinue'
        $total = 0
        $stage = 'OCR_WINRT_INIT'
        $stream = $null; $bitmap = $null; $pdfPage = $null
        function Emit($ok, $code, $text, $lang, $width, $height, $diagnostic = '') {
            $payload = @{Success=[bool]$ok; ErrorCode=[string]$code; Text=[string]$text;
                Language=[string]$lang; TotalPages=[int]$total; Width=[int]$width; Height=[int]$height; Diagnostic=[string]$diagnostic} | ConvertTo-Json -Compress
            [Console]::WriteLine('NIRA_OCR_RESULT:' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($payload)))
        }
        try {
            Add-Type -AssemblyName System.Runtime.WindowsRuntime
            $null = [Windows.Storage.StorageFile,Windows.Storage,ContentType=WindowsRuntime]
            $null = [Windows.Storage.FileAccessMode,Windows.Storage,ContentType=WindowsRuntime]
            $null = [Windows.Storage.Streams.IRandomAccessStream,Windows.Storage.Streams,ContentType=WindowsRuntime]
            $null = [Windows.Storage.Streams.InMemoryRandomAccessStream,Windows.Storage.Streams,ContentType=WindowsRuntime]
            $null = [Windows.Graphics.Imaging.BitmapDecoder,Windows.Foundation,ContentType=WindowsRuntime]
            $null = [Windows.Graphics.Imaging.SoftwareBitmap,Windows.Foundation,ContentType=WindowsRuntime]
            $null = [Windows.Graphics.Imaging.BitmapPixelFormat,Windows.Foundation,ContentType=WindowsRuntime]
            $null = [Windows.Graphics.Imaging.BitmapAlphaMode,Windows.Foundation,ContentType=WindowsRuntime]
            $null = [Windows.Media.Ocr.OcrEngine,Windows.Foundation,ContentType=WindowsRuntime]
            $null = [Windows.Media.Ocr.OcrResult,Windows.Foundation,ContentType=WindowsRuntime]
            $null = [Windows.Globalization.Language,Windows.Globalization,ContentType=WindowsRuntime]
            $null = [Windows.Data.Pdf.PdfDocument,Windows.Data.Pdf,ContentType=WindowsRuntime]
            $null = [Windows.Data.Pdf.PdfPageRenderOptions,Windows.Data.Pdf,ContentType=WindowsRuntime]
            $async = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
                $_.Name -eq 'AsTask' -and $_.IsGenericMethodDefinition -and $_.GetParameters().Count -eq 1 -and
                $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'
            } | Select-Object -First 1
            $action = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
                $_.Name -eq 'AsTask' -and -not $_.IsGenericMethod -and $_.GetParameters().Count -eq 1 -and
                $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncAction'
            } | Select-Object -First 1
            function WaitResult($operation, [Type]$type) {
                $task = $async.MakeGenericMethod($type).Invoke($null, @($operation))
                $task.Wait()
                return ,$task.Result
            }
            if ([string]::IsNullOrWhiteSpace($env:NIRA_OCR_LANGUAGE)) {
                $engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages()
            } else {
                $lang = [Windows.Globalization.Language]::new($env:NIRA_OCR_LANGUAGE)
                $engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage($lang)
            }
            if ($null -eq $engine) { Emit $false 'OCR_LANGUAGE_UNAVAILABLE' '' '' 0 0; exit 0 }
            $limit = [Math]::Min(2400, [int][Windows.Media.Ocr.OcrEngine]::MaxImageDimension)
            $stage = 'OCR_OPEN_FAILED'
            $file = WaitResult ([Windows.Storage.StorageFile]::GetFileFromPathAsync($env:NIRA_OCR_PATH)) ([Windows.Storage.StorageFile])
            $number = [int]$env:NIRA_OCR_PAGE
            if ([IO.Path]::GetExtension($env:NIRA_OCR_PATH) -ieq '.pdf') {
                $pdf = WaitResult ([Windows.Data.Pdf.PdfDocument]::LoadFromFileAsync($file)) ([Windows.Data.Pdf.PdfDocument])
                $total = [int]$pdf.PageCount
                if ($number -lt 1 -or $number -gt $total) { Emit $false 'OCR_PAGE_RANGE' '' '' 0 0; exit 0 }
                $stage = 'OCR_RENDER_FAILED'
                $pdfPage = $pdf.GetPage([uint32]($number - 1))
                $stream = [Windows.Storage.Streams.InMemoryRandomAccessStream]::new()
                $options = [Windows.Data.Pdf.PdfPageRenderOptions]::new()
                $scale = $limit / [Math]::Max([double]$pdfPage.Size.Width, [double]$pdfPage.Size.Height)
                $options.DestinationWidth = [uint32][Math]::Max(1, [Math]::Floor($pdfPage.Size.Width * $scale))
                $options.DestinationHeight = [uint32][Math]::Max(1, [Math]::Floor($pdfPage.Size.Height * $scale))
                $render = $action.Invoke($null, @($pdfPage.RenderToStreamAsync($stream, $options)))
                $render.Wait()
                $stream.Seek(0)
            } else {
                $total = 1
                $stream = WaitResult ($file.OpenAsync([Windows.Storage.FileAccessMode]::Read)) ([Windows.Storage.Streams.IRandomAccessStream])
            }
            $stage = 'OCR_DECODE_FAILED'
            $decoder = WaitResult ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
            $width = [int]$decoder.PixelWidth; $height = [int]$decoder.PixelHeight
            if ($width -gt $limit -or $height -gt $limit -or $width -lt 1 -or $height -lt 1) {
                Emit $false 'OCR_IMAGE_SIZE' '' '' 0 0; exit 0
            }
            $bitmap = WaitResult ($decoder.GetSoftwareBitmapAsync([Windows.Graphics.Imaging.BitmapPixelFormat]::Gray8, [Windows.Graphics.Imaging.BitmapAlphaMode]::Ignore)) ([Windows.Graphics.Imaging.SoftwareBitmap])
            $stage = 'OCR_RECOGNITION_FAILED'
            $result = WaitResult ($engine.RecognizeAsync($bitmap)) ([Windows.Media.Ocr.OcrResult])
            $text = ($result.Lines | ForEach-Object { $_.Text }) -join "`n"
            Emit $true '' $text $engine.RecognizerLanguage.LanguageTag $width $height
        } catch {
            Emit $false $stage '' '' 0 0 ($_.Exception.GetType().Name + ' HRESULT=' + $_.Exception.HResult)
        } finally {
            if ($null -ne $bitmap) { try { $bitmap.Dispose() } catch {} }
            if ($null -ne $stream) { try { $stream.Dispose() } catch {} }
            if ($null -ne $pdfPage) { try { $pdfPage.Dispose() } catch {} }
        }
        """;

    private const string PythonScript = """
        import base64, contextlib, json, os, sys
        from pathlib import Path

        def emit(ok, code='', text='', total=0, width=0, height=0, diagnostic=''):
            payload = dict(Success=ok, ErrorCode=code, Text=text, Language='en' if ok else '',
                           TotalPages=total, Width=width, Height=height, Diagnostic=diagnostic)
            print('NIRA_OCR_RESULT:' + base64.b64encode(json.dumps(payload, ensure_ascii=False).encode('utf-8')).decode('ascii'))

        def recognize():
            from PIL import Image, ImageOps
            import numpy as np
            import pypdfium2 as pdfium
            from rapidocr_onnxruntime import RapidOCR
            path = Path(os.environ['NIRA_OCR_PATH'])
            number = int(os.environ['NIRA_OCR_PAGE'])
            limit = 2400
            if path.suffix.lower() == '.pdf':
                with pdfium.PdfDocument(str(path)) as document:
                    total = len(document)
                    if not 1 <= number <= total:
                        return dict(ok=False, code='OCR_PAGE_RANGE', total=total)
                    page = document[number - 1]
                    try:
                        w, h = page.get_size()
                        if min(w, h) <= 0:
                            return dict(ok=False, code='OCR_IMAGE_SIZE', total=total)
                        bitmap = page.render(scale=min(4.0, (limit - 1) / max(w, h)))
                        try:
                            image = bitmap.to_pil().convert('RGB')
                        finally:
                            bitmap.close()
                    finally:
                        page.close()
            else:
                total = 1
                if number != 1:
                    return dict(ok=False, code='OCR_PAGE_RANGE', total=total)
                Image.MAX_IMAGE_PIXELS = 40_000_000
                with Image.open(path) as original:
                    if original.width * original.height > 40_000_000:
                        return dict(ok=False, code='OCR_IMAGE_SIZE', total=total)
                    image = ImageOps.exif_transpose(original).convert('RGB')
            image.thumbnail((limit, limit), Image.Resampling.LANCZOS)
            width, height = image.size
            engine = RapidOCR(intra_op_num_threads=2, inter_op_num_threads=1)
            rows, _ = engine(np.asarray(image)[:, :, ::-1].copy())
            text = '\n'.join(str(row[1]) for row in (rows or []))
            if len(text) > 120000:
                return dict(ok=False, code='OCR_INVALID_RESULT', diagnostic='recognition result exceeds worker limit', total=total)
            return dict(ok=True, text=text, total=total, width=width, height=height)

        try:
            with contextlib.redirect_stdout(sys.stderr):
                result = recognize()
            emit(**result)
        except Exception as error:
            emit(False, 'OCR_CPU_FAILED', diagnostic=type(error).__name__)
        """;
}
