using System.Text;

namespace NIRAAgent.Capabilities;

// GATE R39 Step 1B: explicit-root discovery with a bounded breadth-first scan.
// This is read-only and uses the existing capability authorization/audit path.
// It never follows reparse points, includes child file contents, or silently
// treats an incomplete scan as an exhaustive answer.
internal static class NIRABoundedDirectoryScan
{
    internal sealed record Entry(string FullPath, string RelativePath, int Depth,
        bool IsDirectory, long Bytes, DateTime LastWriteUtc);

    internal sealed record Result(IReadOnlyList<Entry> Entries, int Examined,
        int SkippedLinks, int Unreadable, bool EntryLimitReached,
        bool DepthLimitReached)
    {
        internal bool ScanComplete => !EntryLimitReached && Unreadable == 0;
    }

    internal static NIRACapabilityParameterDescriptor Param(string name,
        string type, bool required, string description) => new()
    {
        Name = name, Type = type, Required = required, Description = description
    };

    internal static string CheckedRoot(NIRACapabilityRequest request)
    {
        string root = NIRACapabilityArguments.NormalizePath(
            NIRACapabilityArguments.RequireString(request, "path", 32760));
        if (!Path.IsPathFullyQualified(root))
            throw new ArgumentException("A fully qualified search root is required.");
        NIRAFileIntegrity.RejectLinkedPaths(root);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Requested directory does not exist: {root}");
        return root;
    }

    internal static string? SegmentFilter(NIRACapabilityRequest request,
        string key, int limit = 160)
    {
        string? raw = NIRACapabilityArguments.GetOptionalString(request, key, limit);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        string segment = raw.Trim();
        if (segment is "." or ".." || segment.IndexOfAny(
                ['/', '\\', ':', '*', '?', '\r', '\n', '\t', '\0']) >= 0)
            throw new ArgumentException($"{key} must be a literal name fragment, not a path or wildcard.");
        return segment;
    }

    internal static HashSet<string> Exclusions(NIRACapabilityRequest request)
    {
        string? raw = NIRACapabilityArguments.GetOptionalString(request, "excludeNames", 256);
        HashSet<string> excluded = new(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(raw)) return excluded;
        string[] tokens = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length > 8 || tokens.Length == 0)
            throw new ArgumentException("excludeNames supports at most eight literal directory names separated by commas.");
        foreach (string name in tokens)
        {
            if (name.Length > 80 || SegmentFilterValueInvalid(name) || !excluded.Add(name))
                throw new ArgumentException("excludeNames must contain distinct plain directory names only.");
        }
        return excluded;
    }

    private static bool SegmentFilterValueInvalid(string name) =>
        string.IsNullOrWhiteSpace(name) || name is "." or ".." ||
        name.IndexOfAny(['/', '\\', ':', '*', '?', '\r', '\n', '\t', '\0']) >= 0;

    internal static Result Scan(string root, int maxDepth, int maxScanned,
        HashSet<string> excludes, CancellationToken ct)
    {
        Queue<(string Path, int Depth)> pending = new();
        pending.Enqueue((root, 0));
        List<Entry> found = new();
        int examined = 0, skippedLinks = 0, unreadable = 0;
        bool limited = false, depthLimited = false;
        while (pending.Count > 0 && !limited)
        {
            ct.ThrowIfCancellationRequested();
            (string dir, int depth) = pending.Dequeue();
            // Handle inaccessible subdirectories instead of claiming no matches.
            try
            {
                foreach (string child in Directory.EnumerateFileSystemEntries(dir))
                {
                    ct.ThrowIfCancellationRequested();
                    if (examined >= maxScanned)
                    {
                        limited = true;
                        break;
                    }
                    examined++;
                    try
                    {
                        FileAttributes attributes = File.GetAttributes(child);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            skippedLinks++;
                            continue;
                        }
                        bool isDirectory = (attributes & FileAttributes.Directory) != 0;
                        string name = Path.GetFileName(child);
                        if (isDirectory && excludes.Contains(name)) continue;
                        long bytes = isDirectory ? 0 : new FileInfo(child).Length;
                        DateTime modified = isDirectory
                            ? new DirectoryInfo(child).LastWriteTimeUtc
                            : new FileInfo(child).LastWriteTimeUtc;
                        string relative = Path.GetRelativePath(root, child);
                        found.Add(new Entry(child, relative, depth + 1, isDirectory,
                            bytes, modified));
                        if (isDirectory)
                        {
                            if (depth < maxDepth) pending.Enqueue((child, depth + 1));
                            else depthLimited = true;
                        }
                    }
                    catch (UnauthorizedAccessException) { unreadable++; }
                    catch (IOException) { unreadable++; }
                }
            }
            catch (UnauthorizedAccessException) { unreadable++; }
            catch (IOException) { unreadable++; }
        }
        return new Result(found, examined, skippedLinks, unreadable, limited, depthLimited);
    }

    internal static (string Text, int Shown, bool OutputLimited) FormatRows(
        IEnumerable<Entry> rows, bool tree, int maxShown)
    {
        StringBuilder text = new();
        int shown = 0;
        bool clipped = false;
        const int maxOutputChars = 14000;
        foreach (Entry row in rows)
        {
            if (shown >= maxShown) { clipped = true; break; }
            string line = tree
                ? new string(' ', Math.Min(row.Depth, 7) * 2) +
                  (row.IsDirectory ? "[DIR] " : "[FILE] ") +
                  Path.GetFileName(row.FullPath) +
                  (row.IsDirectory ? "" : $" ({row.Bytes} bytes)") +
                  $" | Relative={row.RelativePath}"
                : $"{(row.IsDirectory ? "DIR" : "FILE")}\t{row.Bytes}\t{row.FullPath}\tLastWriteUtc={row.LastWriteUtc:O}";
            if (text.Length + line.Length + 1 > maxOutputChars)
            {
                clipped = true;
                break;
            }
            text.AppendLine(line);
            shown++;
        }
        return (text.ToString().TrimEnd(), shown, clipped);
    }
}

public sealed class NIRAFileSearchCapabilityHandler : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor { get; } = new()
    {
        Id = NIRACapabilityIds.FileSearch,
        Description = "Bounded read-only recursive file/folder NAME and metadata search under an explicit scoped folder. Filters name, extension, kind and file bytes; no file-content inspection or whole-PC implicit search. Reports scan coverage and inaccessible folders.",
        DefaultRisk = NIRACapabilityRisk.Observe,
        Parameters =
        [
            NIRABoundedDirectoryScan.Param("path", "string", true, "Explicit grounded folder root; never omit or use whole-PC implicitly."),
            NIRABoundedDirectoryScan.Param("nameContains", "string", false, "Literal case-insensitive filename fragment, not globbing or content search."),
            NIRABoundedDirectoryScan.Param("extension", "string", false, "Optional file suffix, for example .txt or cs."),
            NIRABoundedDirectoryScan.Param("kind", "string", false, "File (default), Directory, or Either."),
            NIRABoundedDirectoryScan.Param("minSizeBytes", "integer", false, "Optional minimum file size in bytes (up to 2147483647)."),
            NIRABoundedDirectoryScan.Param("maxSizeBytes", "integer", false, "Optional maximum file size in bytes (up to 2147483647)."),
            NIRABoundedDirectoryScan.Param("maxDepth", "integer", false, "0-6 nested directory levels; default 3."),
            NIRABoundedDirectoryScan.Param("maxScanned", "integer", false, "Maximum entries examined, 1-10000; default 3000."),
            NIRABoundedDirectoryScan.Param("maxMatches", "integer", false, "Maximum matched results shown, 1-50; default 20."),
            NIRABoundedDirectoryScan.Param("excludeNames", "string", false, "Optional comma-separated directory names to skip, up to eight (e.g. bin,obj,.git).")
        ]
    };

    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest request) => NIRACapabilityRisk.Observe;

    public Task<NIRACapabilityHandlerResult> ExecuteAsync(NIRACapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        string root = NIRABoundedDirectoryScan.CheckedRoot(request);
        string? needle = NIRABoundedDirectoryScan.SegmentFilter(request, "nameContains");
        string? extension = NIRABoundedDirectoryScan.SegmentFilter(request, "extension", 30);
        if (extension is not null)
        {
            extension = extension.StartsWith('.') ? extension : "." + extension;
            if (extension == "." || extension.Contains(' '))
                throw new ArgumentException("extension must be one literal file suffix, for example .txt.");
        }
        string kind = (NIRACapabilityArguments.GetOptionalString(request, "kind", 32) ?? "File").Trim();
        if (!kind.Equals("File", StringComparison.OrdinalIgnoreCase) &&
            !kind.Equals("Directory", StringComparison.OrdinalIgnoreCase) &&
            !kind.Equals("Either", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("kind must be File, Directory, or Either.");
        int maxDepth = NIRACapabilityArguments.GetInteger(request, "maxDepth", 3, 0, 6);
        int maxScanned = NIRACapabilityArguments.GetInteger(request, "maxScanned", 3000, 1, 10000);
        int maxMatches = NIRACapabilityArguments.GetInteger(request, "maxMatches", 20, 1, 50);
        int minBytes = NIRACapabilityArguments.GetInteger(request, "minSizeBytes", 0, 0, int.MaxValue);
        int maxBytes = NIRACapabilityArguments.GetInteger(request, "maxSizeBytes", int.MaxValue, 0, int.MaxValue);
        if (minBytes > maxBytes) throw new ArgumentException("minSizeBytes cannot exceed maxSizeBytes.");
        if (!kind.Equals("File", StringComparison.OrdinalIgnoreCase) &&
            (minBytes != 0 || maxBytes != int.MaxValue || extension is not null))
            throw new ArgumentException("Extension and byte-size filters require kind=File.");
        HashSet<string> excluded = NIRABoundedDirectoryScan.Exclusions(request);
        var scan = NIRABoundedDirectoryScan.Scan(root, maxDepth, maxScanned, excluded, cancellationToken);
        var matches = scan.Entries.Where(e =>
            (kind.Equals("Either", StringComparison.OrdinalIgnoreCase) ||
             (e.IsDirectory ? kind.Equals("Directory", StringComparison.OrdinalIgnoreCase) :
                 kind.Equals("File", StringComparison.OrdinalIgnoreCase))) &&
            (needle is null || Path.GetFileName(e.FullPath).Contains(needle, StringComparison.OrdinalIgnoreCase)) &&
            (extension is null || (!e.IsDirectory && e.FullPath.EndsWith(extension, StringComparison.OrdinalIgnoreCase))) &&
            (e.IsDirectory || (e.Bytes >= minBytes && e.Bytes <= maxBytes)))
            .OrderBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
        var output = NIRABoundedDirectoryScan.FormatRows(matches, tree: false, maxShown: maxMatches);
        bool limited = matches.Length > output.Shown || output.OutputLimited;
        return Task.FromResult(new NIRACapabilityHandlerResult
        {
            Summary = $"FileSearch | Root={root} | MatchesObserved={matches.Length} | ResultsShown={output.Shown} | " +
                $"Examined={scan.Examined} | MaxDepth={maxDepth} | ScanComplete={scan.ScanComplete} | " +
                $"EntryLimitReached={scan.EntryLimitReached} | DepthLimitReached={scan.DepthLimitReached} | " +
                $"Unreadable={scan.Unreadable} | SkippedLinks={scan.SkippedLinks} | " +
                $"ResultLimited={limited} | SnapshotStable=False | " +
                "A zero match is conclusive only for the scanned depth when ScanComplete=True; skipped links and exclusions are not searched.",
            Output = output.Text,
            ChangedSystemState = false
        });
    }
}

public sealed class NIRADirectoryTreeCapabilityHandler : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor { get; } = new()
    {
        Id = NIRACapabilityIds.DirectoryTree,
        Description = "Read-only bounded folder-tree view under one explicit root, optionally showing file names and sizes; excludes symlinks/junctions and reports incomplete scans. Not a recursive file-size sum.",
        DefaultRisk = NIRACapabilityRisk.Observe,
        Parameters =
        [
            NIRABoundedDirectoryScan.Param("path", "string", true, "Existing grounded folder root."),
            NIRABoundedDirectoryScan.Param("maxDepth", "integer", false, "Directory levels, 0-6; default 2."),
            NIRABoundedDirectoryScan.Param("maxScanned", "integer", false, "Maximum entries examined, 1-10000; default 2000."),
            NIRABoundedDirectoryScan.Param("maxShown", "integer", false, "Maximum entries displayed, 1-100; default 60."),
            NIRABoundedDirectoryScan.Param("includeFiles", "boolean", false, "Include file entries and their individual byte sizes; default true."),
            NIRABoundedDirectoryScan.Param("excludeNames", "string", false, "Optional comma-separated literal directory names to skip, up to eight.")
        ]
    };

    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest request) => NIRACapabilityRisk.Observe;

    public Task<NIRACapabilityHandlerResult> ExecuteAsync(NIRACapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        string root = NIRABoundedDirectoryScan.CheckedRoot(request);
        int maxDepth = NIRACapabilityArguments.GetInteger(request, "maxDepth", 2, 0, 6);
        int maxScanned = NIRACapabilityArguments.GetInteger(request, "maxScanned", 2000, 1, 10000);
        int maxShown = NIRACapabilityArguments.GetInteger(request, "maxShown", 60, 1, 100);
        bool includeFiles = !request.Arguments.TryGetProperty("includeFiles", out var flag) ||
            flag.ValueKind == System.Text.Json.JsonValueKind.Null ||
            NIRACapabilityArguments.GetBoolean(request, "includeFiles");
        var scan = NIRABoundedDirectoryScan.Scan(root, maxDepth, maxScanned,
            NIRABoundedDirectoryScan.Exclusions(request), cancellationToken);
        var visible = scan.Entries.Where(e => includeFiles || e.IsDirectory)
            .OrderBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
        var output = NIRABoundedDirectoryScan.FormatRows(visible, tree: true, maxShown: maxShown);
        bool limited = visible.Length > output.Shown || output.OutputLimited;
        return Task.FromResult(new NIRACapabilityHandlerResult
        {
            Summary = $"FolderTree | Root={root} | EntriesObserved={scan.Entries.Count} | " +
                $"Displayed={output.Shown} | Examined={scan.Examined} | MaxDepth={maxDepth} | " +
                $"ScanComplete={scan.ScanComplete} | EntryLimitReached={scan.EntryLimitReached} | " +
                $"DepthLimitReached={scan.DepthLimitReached} | ResultLimited={limited} | " +
                $"Unreadable={scan.Unreadable} | SkippedLinks={scan.SkippedLinks} | SnapshotStable=False | " +
                "A directory's own size is not computed; see filesystem.usage for cumulative bytes.",
            Output = $"ROOT\t{root}\n{output.Text}".TrimEnd(),
            ChangedSystemState = false
        });
    }
}
