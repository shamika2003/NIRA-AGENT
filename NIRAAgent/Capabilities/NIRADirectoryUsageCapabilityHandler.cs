using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace NIRAAgent.Capabilities;

/// <summary>
/// Bounded, metadata-only disk-usage observation. Child-directory totals are
/// measured rather than inferred from directory metadata. A partial scan is
/// never represented as a complete accounting of disk usage.
/// </summary>
public sealed class NIRADirectoryUsageCapabilityHandler : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor { get; } = new()
    {
        Id = NIRACapabilityIds.DirectoryUsage,
        Description = "Measure actual file bytes under a directory and rank its immediate child folders by cumulative file size. " +
            "Use for storage investigations; filesystem.list sortBy=SizeBytes only ranks individual files. " +
            "Read-only metadata scan. Bound by maxDepth, maxScannedEntries and maxSeconds; partial totals are lower bounds, NOT true folder totals.",
        DefaultRisk = NIRACapabilityRisk.Observe,
        Parameters = new[]
        {
            Parameter("path", "string", true, "Verified directory path to measure; no wildcard."),
            Parameter("maxDepth", "integer", false, "Maximum folder depth beneath root (default 5; 1..12)."),
            Parameter("maxScannedEntries", "integer", false, "Maximum files/folders examined (default 15000, maximum 100000)."),
            Parameter("maxSeconds", "integer", false, "Maximum scan duration in seconds (default 4, maximum 30)."),
            Parameter("top", "integer", false, "Number of immediate-child groups returned (default 12, maximum 50).")
        }
    };

    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest request) => NIRACapabilityRisk.Observe;

    public Task<NIRACapabilityHandlerResult> ExecuteAsync(
        NIRACapabilityRequest request, CancellationToken cancellationToken = default)
    {
        string path = NIRACapabilityArguments.NormalizePath(
            NIRACapabilityArguments.RequireString(request, "path", 32760));
        int maxDepth = NIRACapabilityArguments.GetInteger(request, "maxDepth", 5, 1, 12);
        int maxEntries = NIRACapabilityArguments.GetInteger(request, "maxScannedEntries", 15000, 1, 100000);
        int maxSeconds = NIRACapabilityArguments.GetInteger(request, "maxSeconds", 4, 1, 30);
        int top = NIRACapabilityArguments.GetInteger(request, "top", 12, 1, 50);
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Directory for usage analysis does not exist: {path}");
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Cannot scan a junction/symlink as the root; supply the real directory path.");

        // A normal background worker, not the UI dispatcher, performs the scan.
        return Task.Run(() => Scan(path, maxDepth, maxEntries, maxSeconds, top, cancellationToken), cancellationToken);
    }

    private static NIRACapabilityHandlerResult Scan(
        string root, int maxDepth, int maxEntries, int maxSeconds, int top, CancellationToken token)
    {
        var stopwatch = Stopwatch.StartNew();
        var totals = new Dictionary<string, (long Bytes, int Files)>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<(string Folder, int Depth, string Group)>();
        pending.Enqueue((root, 0, root));
        int examined = 0, files = 0, unreadable = 0, depthLimited = 0;
        long totalBytes = 0;
        bool limited = false;

        while (pending.Count > 0 && !limited)
        {
            token.ThrowIfCancellationRequested();
            if (examined >= maxEntries || stopwatch.Elapsed.TotalSeconds >= maxSeconds)
            {
                limited = true;
                break;
            }
            var (folder, depth, group) = pending.Dequeue();
            IEnumerator<string>? iterator;
            try { iterator = Directory.EnumerateFileSystemEntries(folder).GetEnumerator(); }
            catch (UnauthorizedAccessException) { unreadable++; continue; }
            catch (IOException) { unreadable++; continue; }
            using (iterator)
            {
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    if (examined >= maxEntries || stopwatch.Elapsed.TotalSeconds >= maxSeconds)
                    {
                        limited = true;
                        break;
                    }
                    string child;
                    try
                    {
                        if (!iterator.MoveNext()) break;
                        child = iterator.Current;
                    }
                    catch (UnauthorizedAccessException) { unreadable++; break; }
                    catch (IOException) { unreadable++; break; }
                    examined++;
                    try
                    {
                        FileAttributes attributes = File.GetAttributes(child);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            // Skipping links prevents cycles and inadvertent traversal.
                            depthLimited++;
                            continue;
                        }
                        bool directory = (attributes & FileAttributes.Directory) != 0;
                        string childGroup = depth == 0 ? child : group;
                        if (directory)
                        {
                            if (!totals.ContainsKey(childGroup)) totals[childGroup] = (0, 0);
                            if (depth < maxDepth)
                                pending.Enqueue((child, depth + 1, childGroup));
                            else
                                depthLimited++;
                        }
                        else
                        {
                            long size = new FileInfo(child).Length;
                            var current = totals.GetValueOrDefault(childGroup);
                            totals[childGroup] = (current.Bytes + size, current.Files + 1);
                            totalBytes += size;
                            files++;
                        }
                    }
                    catch (IOException) { unreadable++; }
                    catch (UnauthorizedAccessException) { unreadable++; }
                }
            }
        }

        bool complete = !limited && unreadable == 0 && depthLimited == 0;
        var ranked = totals.OrderByDescending(group => group.Value.Bytes)
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(top).ToArray();
        StringBuilder output = new();
        output.AppendLine("Path\tObservedBytes\tObservedFiles\tAccounting");
        foreach (var entry in ranked)
        {
            string display = string.Equals(entry.Key, root, StringComparison.OrdinalIgnoreCase)
                ? root + " [files directly inside root]" : entry.Key;
            output.Append(display).Append('\t')
                .Append(entry.Value.Bytes.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(entry.Value.Files.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .AppendLine(complete ? "MeasuredWithinDepth" : "LowerBoundIncomplete");
        }
        return new NIRACapabilityHandlerResult
        {
            Summary = $"DirectoryUsage | Root='{root}' | ObservedFiles={files} | " +
                $"ObservedBytes={totalBytes} | EntriesExamined={examined} | " +
                $"EntriesLimit={maxEntries} | DepthLimit={maxDepth} | SecondsLimit={maxSeconds} | " +
                $"ScanComplete={complete} | Incomplete={(!complete)} | " +
                $"Unreadable={unreadable} | LinksOrDepthOmitted={depthLimited} | " +
                $"TimeLimited={limited && stopwatch.Elapsed.TotalSeconds >= maxSeconds} | " +
                $"EntryLimited={limited && examined >= maxEntries} | RankedGroups={ranked.Length}. " +
                (complete ? "These groups total observed contents of this scanned subtree." :
                    "All byte totals are observed LOWER BOUNDS, not full folder sizes. Narrow the root or adjust bounds for better coverage."),
            Output = output.ToString().TrimEnd(),
            ChangedSystemState = false
        };
    }

    private static NIRACapabilityParameterDescriptor Parameter(string name, string type, bool required, string description) =>
        new() { Name = name, Type = type, Required = required, Description = description };
}
