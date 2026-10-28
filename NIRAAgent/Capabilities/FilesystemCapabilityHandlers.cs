/*
 * filename: FilesystemCapabilityHandlers.cs
 */

using System.Security.Cryptography;
using System.Text;

namespace NIRAAgent.Capabilities;


public sealed class NIRAFileReadCapabilityHandler
    : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor
    {
        get;
    } =
        new NIRACapabilityDescriptor
        {
            Id =
                NIRACapabilityIds.FileRead,

            Description =
                "Read TEXT files only (not binary .docx/.pdf/.xlsx). For Word .docx use filesystem.docx.inspect; for SHA-256 use filesystem.hash. Reads bounded lines and audited numeric CSV/TSV totals.",

            DefaultRisk =
                NIRACapabilityRisk.Observe,

            Parameters =
                new[]
                {
                    Parameter("path", "string", true, "Verified file path. An ordinary relative name resolves under NIRA's working directory; a window title or historic file mention is not proof of an existing local file."),
                    Parameter("startLine", "integer", false, "1-based first line. Default 1."),
                    Parameter("maxLines", "integer", false, "Maximum returned lines. Default 300, maximum 1000."),
                    Parameter("maxChars", "integer", false, "Maximum returned characters. Default 24000, maximum 48000.")
                }
        };


    public NIRACapabilityRisk ResolveRisk(
        NIRACapabilityRequest request)
    {
        return NIRACapabilityRisk.Observe;
    }


    public async Task<NIRACapabilityHandlerResult> ExecuteAsync(
        NIRACapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        string requestedPath = NIRACapabilityArguments.RequireString(
            request, "path", 32760);
        string path = NIRACapabilityArguments.NormalizePath(requestedPath);
        // A DOCX is an OpenXML ZIP. Reject before opening a text stream and
        // point cognition at the registered structured reader on FIRST attempt.
        if (string.Equals(Path.GetExtension(path), ".docx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "DOCX is a binary OpenXML package. Use filesystem.docx.inspect for paragraphs, headings and tables; use filesystem.hash for SHA-256. Do not retry filesystem.read on this file.");


        int startLine =
            NIRACapabilityArguments.GetInteger(
                request,
                "startLine",
                1,
                1,
                5_000_000);


        int maxLines =
            NIRACapabilityArguments.GetInteger(
                request,
                "maxLines",
                300,
                1,
                1000);


        int maxChars =
            NIRACapabilityArguments.GetInteger(
                request,
                "maxChars",
                24000,
                1000,
                48000);


        FileInfo file =
            new(
                path);


        if (!file.Exists)
        {
            // Do not silently interpret a process/window caption as a physical
            // file or try invented directories. Preserve legacy relative reads
            // when their real normalized target exists, but explain the missing
            // location so cognition can choose a bounded discovery operation.
            bool relative = !Path.IsPathFullyQualified(
                Environment.ExpandEnvironmentVariables(requestedPath.Trim()));
            string locationHint = relative
                ? "The supplied name was relative. Only explicit user-folder prefixes " +
                  "resolve outside NIRA's working directory. A process window title " +
                  "is not a verified file path; locate an existing file before reading it."
                : "This absolute file path is unavailable on this machine. " +
                  "A location remembered from another session is not proof of local existence.";
            throw new FileNotFoundException(
                $"Requested file does not exist at '{path}'. {locationHint}",
                path);
        }


        StringBuilder output =
            new();


        int currentLine =
            0;


        int includedLines =
            0;


        bool truncated =
            false;

        // Only the actual complete, bounded text read may be audited. A page
        // or character-limited read is never represented as a file-wide total.
        List<string> rawLines = new();

        await using FileStream stream =
            new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);


        using StreamReader reader =
            new(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 4096,
                leaveOpen: false);


        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();


            string? line =
                await reader.ReadLineAsync(
                    cancellationToken);


            if (line ==
                null)
            {
                break;
            }


            currentLine++;


            if (currentLine <
                startLine)
            {
                continue;
            }


            if (line.IndexOf('\0') >=
                0)
            {
                throw new InvalidOperationException(
                    "This is a binary file, not plain text. For .docx use filesystem.docx.inspect; for file size use filesystem.metadata; for SHA-256 use filesystem.hash. Do not retry filesystem.read.");
            }


            string rendered =
                $"L{currentLine}: {line}";


            if (output.Length +
                    rendered.Length +
                    Environment.NewLine.Length >
                maxChars)
            {
                truncated =
                    true;


                break;
            }


            output.AppendLine(
                rendered);
            rawLines.Add(line);

            includedLines++;


            if (includedLines >=
                maxLines)
            {
                string? remainingLine =
                    await reader.ReadLineAsync(
                        cancellationToken);


                if (remainingLine !=
                    null)
                {
                    truncated =
                        true;
                }


                break;
            }
        }


        file.Refresh();


        string summary =
            $"Read {includedLines} line(s) from '{path}' starting at line {startLine}. " +
            $"Length={file.Length} bytes | LastWriteUtc={file.LastWriteTimeUtc:O} | Truncated={truncated}.";

        // Deterministic numeric evidence supplements (never replaces) raw file
        // content. It is completely generic: no project, column name, or fixture
        // is assumed. Skip ambiguous columns instead of inventing a sum.
        if (startLine == 1 && !truncated && file.Length <= 500_000)
        {
            string? audit = NIRADelimitedNumericAudit.TryAudit(path, rawLines);
            if (audit is not null)
                summary += " " + audit;
        }


        return new NIRACapabilityHandlerResult
        {
            Summary =
                summary,

            Output =
                output.ToString().TrimEnd(),

            ChangedSystemState =
                false
        };
    }


    private static NIRACapabilityParameterDescriptor Parameter(
        string name,
        string type,
        bool required,
        string description)
    {
        return new NIRACapabilityParameterDescriptor
        {
            Name = name,
            Type = type,
            Required = required,
            Description = description
        };
    }
}


public sealed class NIRADirectoryListCapabilityHandler
    : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor
    {
        get;
    } =
        new NIRACapabilityDescriptor
        {
            Id =
                NIRACapabilityIds.DirectoryList,

            Description =
                "Enumerate a bounded directory listing, optionally recursive and filtered by search pattern. A zero-match result does not imply that the directory has no other files or subfolders.",

            DefaultRisk =
                NIRACapabilityRisk.Observe,

            Parameters =
                new[]
                {
                    Parameter("path", "string", true, "Directory path."),
                    Parameter("pattern", "string", false, "Filesystem search pattern such as *.cs. Default *."),
                    Parameter("recursive", "boolean", false, "Whether to recurse into child directories. Default false."),
                    Parameter("maxEntries", "integer", false, "Maximum entries per page. Default 200, maximum 1000."),
                    Parameter("offset", "integer", false, "Optional zero-based page offset (default 0). For a limited result, use NextOffset to read the next page; do not repeat offset 0."),
                    Parameter("sortBy", "string", false, "Optional LastWriteUtc (newest), SizeBytes (largest individual files), or Name (alphabetical). Directories do NOT have aggregated sizes; filesystem.usage measures them. Default none preserves legacy order and format."),
                    Parameter("filesOnly", "boolean", false, "Omit folders independently of sortBy. Default false; legacy output unchanged when omitted.")
                }
        };


    public NIRACapabilityRisk ResolveRisk(
        NIRACapabilityRequest request)
    {
        return NIRACapabilityRisk.Observe;
    }


    public Task<NIRACapabilityHandlerResult> ExecuteAsync(
        NIRACapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        string path =
            NIRACapabilityArguments.NormalizePath(
                NIRACapabilityArguments.RequireString(
                    request,
                    "path",
                    32760));


        string pattern =
            NIRACapabilityArguments.GetOptionalString(
                request,
                "pattern",
                256)
            ?? "*";


        bool recursive =
            NIRACapabilityArguments.GetBoolean(
                request,
                "recursive");


        int maxEntries =
            NIRACapabilityArguments.GetInteger(
                request,
                "maxEntries",
                200,
                1,
                1000);


        int offset = NIRACapabilityArguments.GetInteger(request, "offset", 0, 0, 20000);

        string? sortBy = NIRACapabilityArguments.GetOptionalString(request, "sortBy", 40);
        bool filesOnly = NIRACapabilityArguments.GetBoolean(request, "filesOnly");
        // Only proven-equivalent ranking labels are accepted. No arbitrary
        // sort expression is interpreted or executed.
        string sortToken = new string((sortBy ?? string.Empty)
            .Where(char.IsLetterOrDigit).ToArray());
        if (sortToken.Equals("LastWriteUtc", StringComparison.OrdinalIgnoreCase) ||
            sortToken.Equals("Modified", StringComparison.OrdinalIgnoreCase) ||
            sortToken.Equals("ModifiedTime", StringComparison.OrdinalIgnoreCase))
            sortBy = "LastWriteUtc";
        else if (sortToken.Equals("Size", StringComparison.OrdinalIgnoreCase) ||
                 sortToken.Equals("SizeBytes", StringComparison.OrdinalIgnoreCase) ||
                 sortToken.Equals("FileSize", StringComparison.OrdinalIgnoreCase) ||
                 sortToken.Equals("SizeDescending", StringComparison.OrdinalIgnoreCase))
            sortBy = "SizeBytes";
        else if (sortToken.Equals("Name", StringComparison.OrdinalIgnoreCase) ||
                 sortToken.Equals("FileName", StringComparison.OrdinalIgnoreCase) ||
                 sortToken.Equals("Alphabetical", StringComparison.OrdinalIgnoreCase))
            sortBy = "Name";
        else if (!string.IsNullOrWhiteSpace(sortBy))
            throw new ArgumentException("sortBy must be LastWriteUtc, SizeBytes, or Name when supplied.");

        if (!Directory.Exists(
                path))
        {
            throw new DirectoryNotFoundException(
                $"Requested directory does not exist: {path}");
        }


        EnumerationOptions options =
            new()
            {
                RecurseSubdirectories =
                    recursive,

                IgnoreInaccessible =
                    true,

                ReturnSpecialDirectories =
                    false,

                AttributesToSkip =
                    FileAttributes.ReparsePoint
            };


        // Opt-in bounded, source-grounded ranking. A legacy listing preserves
        // its original enumeration order and output format below.
        if (!string.IsNullOrWhiteSpace(sortBy))
        {
            const int maximumScannedEntries = 20000;
            List<(string Path, bool IsDirectory, long SizeBytes, DateTime ModifiedUtc)> observed = new();
            int scanned = 0;
            int unavailable = 0;
            bool scanTruncated = false;
            foreach (string entry in Directory.EnumerateFileSystemEntries(path, pattern, options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (scanned >= maximumScannedEntries)
                {
                    scanTruncated = true;
                    break;
                }
                scanned++;
                try
                {
                    bool isDirectory = Directory.Exists(entry);
                    if (isDirectory && filesOnly) continue;
                    if (isDirectory)
                    {
                        DirectoryInfo info = new(entry);
                        observed.Add((entry, true, 0L, info.LastWriteTimeUtc));
                    }
                    else
                    {
                        FileInfo info = new(entry);
                        observed.Add((entry, false, info.Length, info.LastWriteTimeUtc));
                    }
                }
                catch (UnauthorizedAccessException) { unavailable++; }
                catch (IOException) { unavailable++; }
            }

            var ranked = (sortBy switch
            {
                "SizeBytes" => observed.OrderByDescending(e => e.IsDirectory ? -1L : e.SizeBytes)
                    .ThenBy(e => e.Path, StringComparer.OrdinalIgnoreCase),
                "Name" => observed.OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase),
                _ => observed.OrderByDescending(e => e.ModifiedUtc)
                    .ThenBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
            }).Skip(offset).Take(maxEntries).ToArray();
            StringBuilder rankedOutput = new();
            foreach (var entry in ranked)
            {
                if (entry.IsDirectory)
                    rankedOutput.AppendLine(sortBy == "SizeBytes"
                        ? $"DIR\t{entry.Path}\tLastWriteUtc={entry.ModifiedUtc:O}\tSizeBytes=NotAggregated"
                        : $"DIR\t{entry.Path}\tLastWriteUtc={entry.ModifiedUtc:O}");
                else
                    rankedOutput.AppendLine($"FILE\t{entry.SizeBytes}\t{entry.Path}\tLastWriteUtc={entry.ModifiedUtc:O}");
            }
            bool? rankedRootHasEntries = ranked.Length == 0 ? RootHasEntries(path) : null;
            return Task.FromResult(new NIRACapabilityHandlerResult
            {
                Summary = $"Enumerated {ranked.Length} ranked entr(y/ies) under '{path}'. " +
                    $"SortBy={sortBy} | FilesOnly={filesOnly} | Scanned={scanned} | " +
                    $"Truncated={scanTruncated || observed.Count > offset + ranked.Length} | " +
                    $"ResultLimited={observed.Count > offset + ranked.Length} | " +
                    $"Offset={offset} | NextOffset={(observed.Count > offset + ranked.Length ? (offset + ranked.Length).ToString() : "None")} | " +
                    $"ScanComplete={!scanTruncated && unavailable == 0} | " +
                    $"RankingComplete={!scanTruncated && unavailable == 0} | Unreadable={unavailable}. " +
                    (ranked.Length == 0 ? $"ZeroMatchScope=Pattern:{pattern},Recursive:{recursive},FilesOnly:{filesOnly} | RootHasOtherEntries={DescribeExistence(rankedRootHasEntries)}. A zero-match result does not prove the directory is empty. " : string.Empty) +
                    (sortBy == "SizeBytes"
                        ? "Directory sizes are NOT calculated; this ranks individual file sizes. "
                        : string.Empty) +
                    (!scanTruncated && unavailable == 0
                        ? "The ranking is complete for the scanned scope. When NextOffset is present, request that offset only if more results are needed; do not repeat the same page."
                        : "The scan was incomplete; the top-ranked entries may not represent the full requested scope."),
                Output = rankedOutput.ToString().TrimEnd(),
                ChangedSystemState = false
            });
        }

        StringBuilder output =
            new();


        int count =
            0;


        bool truncated =
            false;

        // A file-only query could otherwise traverse arbitrarily many folders
        // before finding maxEntries files. Keep the legacy listing unchanged.
        const int maximumFilteredScan = 20000;
        int filteredScanned = 0;
        int skipped = 0;

        foreach (
            string entry
            in Directory.EnumerateFileSystemEntries(
                path,
                pattern,
                options))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (filesOnly && filteredScanned >= maximumFilteredScan)
            {
                truncated = true;
                break;
            }
            filteredScanned++;

            if (count >=
                maxEntries)
            {
                truncated =
                    true;


                break;
            }


            if (Directory.Exists(
                    entry))
            {
                // File filtering is independent of chronological ordering.
                // Excluded directories do not consume maxEntries.
                if (filesOnly)
                    continue;

                if (skipped++ < offset) continue;
                output.AppendLine(
                    $"DIR\t{entry}");
            }
            else
            {
                if (skipped++ < offset) continue;
                FileInfo file =
                    new(
                        entry);


                output.AppendLine(
                    $"FILE\t{file.Length}\t{entry}");
            }


            count++;
        }


        bool? rootHasEntries = count == 0 ? RootHasEntries(path) : null;
        return Task.FromResult(
            new NIRACapabilityHandlerResult
            {
                Summary =
                    $"Enumerated {count} entry/entries under '{path}'. Recursive={recursive} | Pattern='{pattern}' | Truncated={truncated} | Offset={offset} | NextOffset={(truncated ? (offset + count).ToString() : "None")}." +
                    (count == 0 ? $" ZeroMatchScope=Pattern:{pattern},Recursive:{recursive},FilesOnly:{filesOnly} | RootHasOtherEntries={DescribeExistence(rootHasEntries)}. A zero-match result does not prove the directory is empty." : string.Empty) +
                    (filesOnly ? $" FilesOnly=True | Scanned={filteredScanned} | ScanComplete={!truncated}." : string.Empty),

                Output =
                    output.ToString().TrimEnd(),

                ChangedSystemState =
                    false
            });
    }


    // A filtered query can return zero even when the folder contains other
    // extensions or directories. Keep this observation separate from matches.
    // One top-level entry is enough; never enumerate the whole tree to verify.
    private static bool? RootHasEntries(string path)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(path, "*", new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = false,
                ReturnSpecialDirectories = false,
                AttributesToSkip = 0
            }).Any();
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static string DescribeExistence(bool? hasEntries) => hasEntries switch
    {
        true => "True",
        false => "False",
        _ => "Unknown"
    };

    private static NIRACapabilityParameterDescriptor Parameter(
        string name,
        string type,
        bool required,
        string description)
    {
        return new NIRACapabilityParameterDescriptor
        {
            Name = name,
            Type = type,
            Required = required,
            Description = description
        };
    }
}


public sealed class NIRAFileMetadataCapabilityHandler
    : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor
    {
        get;
    } =
        new NIRACapabilityDescriptor
        {
            Id =
                NIRACapabilityIds.FileMetadata,

            Description =
                "Inspect authoritative filesystem metadata for a file or directory without reading its content.",

            DefaultRisk =
                NIRACapabilityRisk.Observe,

            Parameters =
                new[]
                {
                    Parameter("path", "string", true, "File or directory path.")
                }
        };


    public NIRACapabilityRisk ResolveRisk(
        NIRACapabilityRequest request)
    {
        return NIRACapabilityRisk.Observe;
    }


    public Task<NIRACapabilityHandlerResult> ExecuteAsync(
        NIRACapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();


        string path =
            NIRACapabilityArguments.NormalizePath(
                NIRACapabilityArguments.RequireString(
                    request,
                    "path",
                    32760));


        if (File.Exists(
                path))
        {
            FileInfo file =
                new(
                    path);


            return Task.FromResult(
                new NIRACapabilityHandlerResult
                {
                    Summary =
                        $"File exists: '{path}'.",

                    Output =
                        $"Type=File\n" +
                        $"FullPath={file.FullName}\n" +
                        $"Length={file.Length}\n" +
                        $"Attributes={file.Attributes}\n" +
                        $"CreationUtc={file.CreationTimeUtc:O}\n" +
                        $"LastWriteUtc={file.LastWriteTimeUtc:O}\n" +
                        $"LastAccessUtc={file.LastAccessTimeUtc:O}",

                    ChangedSystemState =
                        false
                });
        }


        if (Directory.Exists(
                path))
        {
            DirectoryInfo directory =
                new(
                    path);


            return Task.FromResult(
                new NIRACapabilityHandlerResult
                {
                    Summary =
                        $"Directory exists: '{path}'.",

                    Output =
                        $"Type=Directory\n" +
                        $"FullPath={directory.FullName}\n" +
                        $"Attributes={directory.Attributes}\n" +
                        $"CreationUtc={directory.CreationTimeUtc:O}\n" +
                        $"LastWriteUtc={directory.LastWriteTimeUtc:O}\n" +
                        $"LastAccessUtc={directory.LastAccessTimeUtc:O}",

                    ChangedSystemState =
                        false
                });
        }


        return Task.FromResult(
            new NIRACapabilityHandlerResult
            {
                Summary =
                    $"Filesystem path does not exist: '{path}'.",

                Output =
                    $"Type=Missing\nFullPath={path}",

                ChangedSystemState =
                    false
            });
    }


    private static NIRACapabilityParameterDescriptor Parameter(
        string name,
        string type,
        bool required,
        string description)
    {
        return new NIRACapabilityParameterDescriptor
        {
            Name = name,
            Type = type,
            Required = required,
            Description = description
        };
    }
}


public sealed class NIRAFileWriteCapabilityHandler
    : INIRACapabilityHandler
{
    private readonly NIRAWindowsProtectedFolderAccessService? _windowsAccess;
    public NIRAFileWriteCapabilityHandler(NIRAWindowsProtectedFolderAccessService? windowsAccess = null)
    {
        _windowsAccess = windowsAccess;
    }

    public NIRACapabilityDescriptor Descriptor
    {
        get;
    } =
        new NIRACapabilityDescriptor
        {
            Id =
                NIRACapabilityIds.FileWrite,

            Description =
                "Write UTF-8 PLAIN TEXT to .txt/.md/config/code files, never binary Office documents, images, PDFs or ZIPs. Supports overwrite/append and returns post-write hash evidence. Use format-aware capabilities for DOCX/XLSX/PPTX/PDF.",

            DefaultRisk =
                NIRACapabilityRisk.Modify,

            Parameters =
                new[]
                {
                    Parameter("path", "string", true, "Destination file path."),
                    Parameter("content", "string", true, "Text to write."),
                    Parameter("mode", "string", false, "overwrite or append. Default overwrite."),
                    Parameter("expectedLastWriteUtc", "string", false, "Optional optimistic-concurrency guard using prior ISO-8601 LastWriteUtc.")
                }
        };


    public NIRACapabilityRisk ResolveRisk(
        NIRACapabilityRequest request)
    {
        return NIRACapabilityRisk.Modify;
    }


    public async Task<NIRACapabilityHandlerResult> ExecuteAsync(
        NIRACapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await ExecuteWriteOnceAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            string target = NIRACapabilityArguments.NormalizePath(
                NIRACapabilityArguments.RequireString(request, "path", 32760));
            if (_windowsAccess is null ||
                !Directory.Exists(Path.GetDirectoryName(target)) ||
                !await _windowsAccess.TryRecoverAsync(target, failure, cancellationToken).ConfigureAwait(false))
                throw;
            // The original write remains atomic and its optimistic guards are
            // rechecked. Never change destination or overwrite semantics.
            return await ExecuteWriteOnceAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<NIRACapabilityHandlerResult> ExecuteWriteOnceAsync(
        NIRACapabilityRequest request, CancellationToken cancellationToken)
    {
        string path =
            NIRACapabilityArguments.NormalizePath(
                NIRACapabilityArguments.RequireString(
                    request,
                    "path",
                    32760));


        NIRAWindowsProtectedFolderAccessService.RejectRedirectedKnownFolderShadow(path);

        // Text-only writes must never masquerade as binary office documents or
        // image/package files. A 40-byte string saved as .docx blocks a later
        // create-only Word Studio operation and is NOT a valid Word document.
        // The prohibition is extension-based, independent of user topic.
        string extension = Path.GetExtension(path);
        if (new[] { ".docx", ".doc", ".xlsx", ".xls", ".pptx", ".ppt",
                    ".pdf", ".odt", ".ods", ".odp", ".zip",
                    ".png", ".jpg", ".jpeg", ".webp", ".gif" }
            .Contains(extension, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "TEXT_WRITE_BINARY_FORMAT_REJECTED: filesystem.write writes plain UTF-8 text, " +
                "not a valid '" + extension + "' file. Use a format-aware registered " +
                "creation capability; draft text belongs in a .txt/.md file. " +
                "The requested destination was not created or overwritten.");

        string content =
            NIRACapabilityArguments.RequireRawString(
                request,
                "content",
                2_000_000);


        string mode =
            NIRACapabilityArguments.GetOptionalString(
                request,
                "mode",
                32)
            ?? "overwrite";


        mode =
            mode.Trim().ToLowerInvariant();


        if (mode is not
            ("overwrite" or "append"))
        {
            throw new InvalidOperationException(
                "filesystem.write mode must be 'overwrite' or 'append'.");
        }


        string? expectedLastWrite =
            NIRACapabilityArguments.GetOptionalString(
                request,
                "expectedLastWriteUtc",
                128);


        if (!string.IsNullOrWhiteSpace(
                expectedLastWrite)
            &&
            File.Exists(
                path))
        {
            if (!DateTimeOffset.TryParse(
                    expectedLastWrite,
                    out DateTimeOffset expected))
            {
                throw new InvalidOperationException(
                    "expectedLastWriteUtc must be a valid ISO-8601 timestamp.");
            }


            DateTimeOffset actual =
                File.GetLastWriteTimeUtc(
                    path);


            if (Math.Abs(
                    (actual - expected.ToUniversalTime()).TotalSeconds) >
                1.0)
            {
                throw new InvalidOperationException(
                    $"Optimistic write guard failed. Expected LastWriteUtc={expected:O}, actual={actual:O}.");
            }
        }


        string? directory =
            Path.GetDirectoryName(
                path);


        if (string.IsNullOrWhiteSpace(
                directory)
            ||
            !Directory.Exists(
                directory))
        {
            throw new DirectoryNotFoundException(
                "Destination directory does not exist: '" + directory + "'. " +
                "A prior directory-create request must actually succeed before writing. " +
                "Check its last failure (including Windows folder protection or OneDrive availability); " +
                "repeating filesystem.write cannot create a blocked parent.");
        }


        // Stage the complete replacement next to its destination. Cancellation
        // before the move leaves the original file intact.
        string temporary = path + ".NIRA-write-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (FileStream output = new(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 81920, FileOptions.Asynchronous))
            {
                if (mode == "append" && File.Exists(path))
                {
                    await using FileStream input = new(path, FileMode.Open, FileAccess.Read,
                        FileShare.Read, 81920, FileOptions.Asynchronous);
                    await input.CopyToAsync(output, cancellationToken);
                }
                byte[] bytes = new UTF8Encoding(false).GetBytes(content);
                await output.WriteAsync(bytes.AsMemory(), cancellationToken);
                await output.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            // Recheck the optional concurrency guard immediately before replacing.
            if (!string.IsNullOrWhiteSpace(expectedLastWrite))
            {
                if (!File.Exists(path) || !DateTimeOffset.TryParse(expectedLastWrite, out DateTimeOffset expectedNow) ||
                    Math.Abs((new DateTimeOffset(File.GetLastWriteTimeUtc(path)) - expectedNow.ToUniversalTime()).TotalSeconds) > 1.0)
                    throw new InvalidOperationException("Optimistic write guard failed before replacing the file.");
            }
            File.Move(temporary, path, overwrite: !request.RequireCreateNew);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                try { File.Delete(temporary); } catch { }
            }
        }

        FileInfo file =
            new(
                path);


        string hash =
            ComputeSha256(
                path);


        return new NIRACapabilityHandlerResult
        {
            Summary =
                $"Wrote '{path}' successfully. Mode={mode} | Length={file.Length} | LastWriteUtc={file.LastWriteTimeUtc:O} | SHA256={hash}.",

            Output =
                $"FullPath={path}\nLength={file.Length}\nLastWriteUtc={file.LastWriteTimeUtc:O}\nSHA256={hash}",

            ChangedSystemState =
                true
        };
    }


    private static string ComputeSha256(
        string path)
    {
        using FileStream stream =
            File.OpenRead(
                path);


        byte[] hash =
            SHA256.HashData(
                stream);


        return Convert.ToHexString(
            hash)
            .ToLowerInvariant();
    }


    private static NIRACapabilityParameterDescriptor Parameter(
        string name,
        string type,
        bool required,
        string description)
    {
        return new NIRACapabilityParameterDescriptor
        {
            Name = name,
            Type = type,
            Required = required,
            Description = description
        };
    }
}


public sealed class NIRAFileCopyCapabilityHandler
    : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor
    {
        get;
    } =
        new NIRACapabilityDescriptor
        {
            Id =
                NIRACapabilityIds.FileCopy,

            Description =
                "Copy a file to a new path with explicit overwrite control.",

            DefaultRisk =
                NIRACapabilityRisk.Modify,

            Parameters =
                new[]
                {
                    Parameter("source", "string", true, "Existing source file."),
                    Parameter("destination", "string", true, "Destination file."),
                    Parameter("overwrite", "boolean", false, "Allow replacing an existing destination. Default false.")
                }
        };


    public NIRACapabilityRisk ResolveRisk(
        NIRACapabilityRequest request)
    {
        return NIRACapabilityArguments.GetBoolean(
                request,
                "overwrite")
            ? NIRACapabilityRisk.Destructive
            : NIRACapabilityRisk.Modify;
    }


    public Task<NIRACapabilityHandlerResult> ExecuteAsync(
        NIRACapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();


        string source =
            NIRACapabilityArguments.NormalizePath(
                NIRACapabilityArguments.RequireString(
                    request,
                    "source",
                    32760));


        string destination =
            NIRACapabilityArguments.NormalizePath(
                NIRACapabilityArguments.RequireString(
                    request,
                    "destination",
                    32760));


        bool overwrite =
            NIRACapabilityArguments.GetBoolean(
                request,
                "overwrite");


        File.Copy(
            source,
            destination,
            overwrite);


        return Task.FromResult(
            new NIRACapabilityHandlerResult
            {
                Summary =
                    $"Copied '{source}' to '{destination}'. Overwrite={overwrite}.",

                ChangedSystemState =
                    true
            });
    }


    private static NIRACapabilityParameterDescriptor Parameter(
        string name,
        string type,
        bool required,
        string description)
    {
        return new NIRACapabilityParameterDescriptor
        {
            Name = name,
            Type = type,
            Required = required,
            Description = description
        };
    }
}


public sealed class NIRAFileMoveCapabilityHandler
    : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor
    {
        get;
    } =
        new NIRACapabilityDescriptor
        {
            Id =
                NIRACapabilityIds.FileMove,

            Description =
                "Move or rename a file with explicit overwrite control.",

            DefaultRisk =
                NIRACapabilityRisk.Modify,

            Parameters =
                new[]
                {
                    Parameter("source", "string", true, "Existing source file."),
                    Parameter("destination", "string", true, "Destination file."),
                    Parameter("overwrite", "boolean", false, "Allow replacing an existing destination. Default false.")
                }
        };


    public NIRACapabilityRisk ResolveRisk(
        NIRACapabilityRequest request)
    {
        return NIRACapabilityArguments.GetBoolean(
                request,
                "overwrite")
            ? NIRACapabilityRisk.Destructive
            : NIRACapabilityRisk.Modify;
    }


    public Task<NIRACapabilityHandlerResult> ExecuteAsync(
        NIRACapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();


        string source =
            NIRACapabilityArguments.NormalizePath(
                NIRACapabilityArguments.RequireString(
                    request,
                    "source",
                    32760));


        string destination =
            NIRACapabilityArguments.NormalizePath(
                NIRACapabilityArguments.RequireString(
                    request,
                    "destination",
                    32760));


        bool overwrite =
            NIRACapabilityArguments.GetBoolean(
                request,
                "overwrite");


        File.Move(
            source,
            destination,
            overwrite);


        return Task.FromResult(
            new NIRACapabilityHandlerResult
            {
                Summary =
                    $"Moved '{source}' to '{destination}'. Overwrite={overwrite}.",

                ChangedSystemState =
                    true
            });
    }


    private static NIRACapabilityParameterDescriptor Parameter(
        string name,
        string type,
        bool required,
        string description)
    {
        return new NIRACapabilityParameterDescriptor
        {
            Name = name,
            Type = type,
            Required = required,
            Description = description
        };
    }
}


public sealed class NIRAFileDeleteCapabilityHandler
    : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor
    {
        get;
    } =
        new NIRACapabilityDescriptor
        {
            Id =
                NIRACapabilityIds.FileDelete,

            Description =
                "Delete one file or directory. Directory deletion requires explicit recursive=true for non-empty trees.",

            DefaultRisk =
                NIRACapabilityRisk.Destructive,

            Parameters =
                new[]
                {
                    Parameter("path", "string", true, "File or directory path to delete."),
                    Parameter("recursive", "boolean", false, "For directory deletion, remove child contents too. Default false.")
                }
        };


    public NIRACapabilityRisk ResolveRisk(
        NIRACapabilityRequest request)
    {
        return NIRACapabilityRisk.Destructive;
    }


    public Task<NIRACapabilityHandlerResult> ExecuteAsync(
        NIRACapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();


        string path =
            NIRACapabilityArguments.NormalizePath(
                NIRACapabilityArguments.RequireString(
                    request,
                    "path",
                    32760));


        bool recursive =
            NIRACapabilityArguments.GetBoolean(
                request,
                "recursive");


        if (File.Exists(
                path))
        {
            File.Delete(
                path);


            return Task.FromResult(
                new NIRACapabilityHandlerResult
                {
                    Summary =
                        $"Deleted file '{path}'.",

                    ChangedSystemState =
                        true
                });
        }


        if (Directory.Exists(
                path))
        {
            Directory.Delete(
                path,
                recursive);


            return Task.FromResult(
                new NIRACapabilityHandlerResult
                {
                    Summary =
                        $"Deleted directory '{path}'. Recursive={recursive}.",

                    ChangedSystemState =
                        true
                });
        }


        // Delete is idempotent at the capability boundary. If the target
        // is already absent, the requested end-state is already satisfied.
        // Treating that as a failure makes stale/repeated cognition believe
        // it must keep deleting the same path and can reopen destructive
        // authorization unnecessarily.
        return Task.FromResult(
            new NIRACapabilityHandlerResult
            {
                Summary =
                    $"Filesystem path '{path}' is already absent; nothing needed to be deleted.",

                ChangedSystemState =
                    false
            });
    }


    private static NIRACapabilityParameterDescriptor Parameter(
        string name,
        string type,
        bool required,
        string description)
    {
        return new NIRACapabilityParameterDescriptor
        {
            Name = name,
            Type = type,
            Required = required,
            Description = description
        };
    }
}


public sealed class NIRADirectoryCreateCapabilityHandler
    : INIRACapabilityHandler
{
    private readonly NIRAWindowsProtectedFolderAccessService? _windowsAccess;

    public NIRADirectoryCreateCapabilityHandler(NIRAWindowsProtectedFolderAccessService? windowsAccess = null)
    {
        _windowsAccess = windowsAccess;
    }

    public NIRACapabilityDescriptor Descriptor
    {
        get;
    } =
        new NIRACapabilityDescriptor
        {
            Id =
                NIRACapabilityIds.DirectoryCreate,

            Description =
                "Create a directory path, including missing parents.",

            DefaultRisk =
                NIRACapabilityRisk.Modify,

            Parameters =
                new[]
                {
                    Parameter("path", "string", true, "Directory path to create.")
                }
        };


    public NIRACapabilityRisk ResolveRisk(
        NIRACapabilityRequest request)
    {
        return NIRACapabilityRisk.Modify;
    }


    public async Task<NIRACapabilityHandlerResult> ExecuteAsync(
        NIRACapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string path = NIRACapabilityArguments.NormalizePath(
            NIRACapabilityArguments.RequireString(request, "path", 32760));
        string parent = Path.GetDirectoryName(path) ?? string.Empty;
        NIRAWindowsProtectedFolderAccessService.RejectRedirectedKnownFolderShadow(path);
        bool existedBefore = Directory.Exists(path);
        bool recovered = false;
        bool retried = false;

        try
        {
            try
            {
                // Directory.CreateDirectory already creates missing parents.
                // A normal new folder is a routine, silent user-authorized
                // operation; it does not require administrator elevation.
                Directory.CreateDirectory(path);
                if (!Directory.Exists(path))
                    throw new DirectoryNotFoundException(
                        "The provider did not expose the created directory.");
            }
            catch (Exception first) when (first is IOException or UnauthorizedAccessException)
            {
                // Some redirected/sync folders transiently report an absent
                // path while its existing parent is being materialized. One
                // bounded retry requires no new model decision or user prompt.
                // Never retry an access denial, a missing parent, or a generic
                // I/O fault; that is not a permission-bypass mechanism.
                bool parentAvailable = Directory.Exists(parent);
                if (!NIRADirectoryRecoveryPolicy.ShouldRetry(
                        first, parentAvailable, Directory.Exists(path), 0))
                    throw;

                retried = true;
                await Task.Delay(180, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
                if (!Directory.Exists(path))
                    throw new DirectoryNotFoundException(
                        "The redirected directory is still unavailable after a bounded retry.");
                recovered = true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Exception observedFailure = ex;
            // A blocked operation in a protected Windows known folder can ask
            // the owner once for an OS-managed, persistent application allowance.
            // The user sees its broad scope before UAC. No model authorization
            // or silent security modification is involved.
            if (_windowsAccess is not null &&
                await _windowsAccess.TryRecoverAsync(path, ex, cancellationToken).ConfigureAwait(false))
            {
                // Exactly one replay of the ORIGINAL destination, then verify.
                // If Windows still denies it, normal diagnostics below apply.
                try
                {
                    Directory.CreateDirectory(path);
                    if (!Directory.Exists(path))
                        throw new DirectoryNotFoundException("Directory was not visible after OS approval.");
                    return new NIRACapabilityHandlerResult
                    {
                        Summary = $"Directory created and verified after explicit Windows owner approval: '{path}'.",
                        ChangedSystemState = !existedBefore
                    };
                }
                catch (Exception replay) when (replay is IOException or UnauthorizedAccessException)
                {
                    observedFailure = replay;
                }
            }
            // Report a verified OS/provider outcome, not a guessed Defender
            // diagnosis. A full-autonomy setting inside NIRA cannot overrule
            // Windows access control. Never substitute an unrequested folder.
            bool parentAvailable = Directory.Exists(parent);
            bool targetAvailable = Directory.Exists(path);
            string failureKind = NIRADirectoryRecoveryPolicy.Classify(observedFailure);
            string nextStep = failureKind == "AccessDenied"
                ? "Windows denied this path. Ask the owner once to choose an accessible location or use the OS-managed app-access configuration; do not launch NIRA as administrator."
                : parentAvailable
                    ? "The parent exists, but Windows did not expose the requested child. Controlled Folder Access and sync-provider failures can look similar; do not attribute this to OneDrive without independent evidence."
                    : "The actual parent path is unavailable. Recheck the resolved known-folder location, without guessing another user profile.";
            throw new IOException(
                $"DirectoryCreateFailed | Kind={failureKind} | Target='{path}' | " +
                $"ParentExists={parentAvailable} | TargetExists={targetAvailable} | " +
                $"AutomaticRetryUsed={retried} | Exception={observedFailure.GetType().Name} | " +
                $"HResult=0x{observedFailure.HResult:X8} | Detail={observedFailure.Message}. " +
                nextStep + " No repeat approval or identical retry will resolve an unchanged OS restriction.", observedFailure);
        }

        // Verify the requested exact path. Never silently save to Public,
        // AppData, or a different account in order to claim task success.
        return new NIRACapabilityHandlerResult
        {
            Summary = existedBefore
                ? $"Directory was already present: '{path}'."
                : $"Directory created and verified: '{path}'. " +
                  $"SilentRecovery={(recovered ? "TransientProviderRetry" : "NotNeeded")}",
            ChangedSystemState = !existedBefore
        };
    }


    private static NIRACapabilityParameterDescriptor Parameter(
        string name,
        string type,
        bool required,
        string description)
    {
        return new NIRACapabilityParameterDescriptor
        {
            Name = name,
            Type = type,
            Required = required,
            Description = description
        };
    }
}

// Platform-independent decision rules make a single safe provider retry
// testable without changing the ordinary authorization or capability set.
internal static class NIRADirectoryRecoveryPolicy
{
    internal static bool ShouldRetry(
        Exception error, bool parentExists, bool targetExists, int retryNumber) =>
        retryNumber == 0 && parentExists && !targetExists &&
        (error is FileNotFoundException or DirectoryNotFoundException);

    internal static string Classify(Exception error) => error switch
    {
        UnauthorizedAccessException => "AccessDenied",
        IOException e when e.HResult == unchecked((int)0x80070005) => "AccessDenied",
        FileNotFoundException => "PathOrProviderNotFound",
        DirectoryNotFoundException => "ParentPathUnavailable",
        IOException => "FilesystemIOError",
        _ => "UnexpectedFailure"
    };
}
