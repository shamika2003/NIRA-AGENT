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
                "Read a bounded text window from a file with line numbers and file metadata.",

            DefaultRisk =
                NIRACapabilityRisk.Observe,

            Parameters =
                new[]
                {
                    Parameter("path", "string", true, "Absolute or relative file path."),
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
        string path =
            NIRACapabilityArguments.NormalizePath(
                NIRACapabilityArguments.RequireString(
                    request,
                    "path",
                    32760));


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
            throw new FileNotFoundException(
                "Requested file does not exist.",
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
                    "The requested file appears to be binary. Use filesystem.metadata instead of text read.");
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
                "Enumerate a bounded directory listing, optionally recursive and filtered by search pattern.",

            DefaultRisk =
                NIRACapabilityRisk.Observe,

            Parameters =
                new[]
                {
                    Parameter("path", "string", true, "Directory path."),
                    Parameter("pattern", "string", false, "Filesystem search pattern such as *.cs. Default *."),
                    Parameter("recursive", "boolean", false, "Whether to recurse into child directories. Default false."),
                    Parameter("maxEntries", "integer", false, "Maximum entries. Default 200, maximum 1000.")
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


        StringBuilder output =
            new();


        int count =
            0;


        bool truncated =
            false;


        foreach (
            string entry
            in Directory.EnumerateFileSystemEntries(
                path,
                pattern,
                options))
        {
            cancellationToken.ThrowIfCancellationRequested();


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
                output.AppendLine(
                    $"DIR\t{entry}");
            }
            else
            {
                FileInfo file =
                    new(
                        entry);


                output.AppendLine(
                    $"FILE\t{file.Length}\t{entry}");
            }


            count++;
        }


        return Task.FromResult(
            new NIRACapabilityHandlerResult
            {
                Summary =
                    $"Enumerated {count} entry/entries under '{path}'. Recursive={recursive} | Pattern='{pattern}' | Truncated={truncated}.",

                Output =
                    output.ToString().TrimEnd(),

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
    public NIRACapabilityDescriptor Descriptor
    {
        get;
    } =
        new NIRACapabilityDescriptor
        {
            Id =
                NIRACapabilityIds.FileWrite,

            Description =
                "Write UTF-8 text to an existing or new file. Supports overwrite or append and returns post-write hash evidence.",

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
        string path =
            NIRACapabilityArguments.NormalizePath(
                NIRACapabilityArguments.RequireString(
                    request,
                    "path",
                    32760));


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
                "Destination directory does not exist. Use filesystem.directory.create explicitly when creation is intended.");
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


        DirectoryInfo directory =
            Directory.CreateDirectory(
                path);


        return Task.FromResult(
            new NIRACapabilityHandlerResult
            {
                Summary =
                    $"Directory exists after create operation: '{directory.FullName}'.",

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
