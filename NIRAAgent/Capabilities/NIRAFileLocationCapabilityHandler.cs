/*
 * filename: NIRAFileLocationCapabilityHandler.cs
 * Bounded, observation-only discovery under an evidence-grounded directory.
 * It never selects a write target or grants access to the discovered path.
 */
using System.Text;

namespace NIRAAgent.Capabilities;

public sealed class NIRAFileLocationCapabilityHandler : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor { get; } =
        new NIRACapabilityDescriptor
        {
            Id = NIRACapabilityIds.FileLocate,
            Description =
                "Find files or folders by exact name with bounded observation-only traversal. " +
                "Use a grounded absolute root when one is known. If no root is supplied, the " +
                "handler can search all ready fixed local drives so an explicit whole-PC locate " +
                "request does not require the user to invent a base directory. It does not access file contents.",
            DefaultRisk = NIRACapabilityRisk.Observe,
            Parameters = new[]
            {
                Parameter("root", "string", false, "Optional grounded absolute directory. Omit only for a whole-PC search across ready fixed local drives."),
                Parameter("name", "string", true, "Exact file or folder name, not a wildcard or path."),
                Parameter("kind", "string", false, "Directory, File or Either. Default Either."),
                Parameter("maxDepth", "integer", false, "Child-directory depth, 0-6; default 3 for a known root, 6 for whole-PC search."),
                Parameter("maxEntries", "integer", false, "Maximum entries examined per search root, 1-12000; default 1000 for a known root, 8000 for whole-PC search."),
                Parameter("maxMatches", "integer", false, "Maximum returned matches, 1-50; default 20.")
            }
        };

    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest request) =>
        NIRACapabilityRisk.Observe;

    public Task<NIRACapabilityHandlerResult> ExecuteAsync(
        NIRACapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? requestedRoot =
            NIRACapabilityArguments.GetOptionalString(request, "root", 32760);
        string name = NIRACapabilityArguments.RequireString(request, "name", 255).Trim();
        string kind = NIRACapabilityArguments.GetOptionalString(request, "kind", 32)
            ?? "Either";
        int maxDepth = NIRACapabilityArguments.GetInteger(
            request, "maxDepth", string.IsNullOrWhiteSpace(requestedRoot) ? 6 : 3, 0, 6);
        int maxEntries = NIRACapabilityArguments.GetInteger(
            request, "maxEntries", string.IsNullOrWhiteSpace(requestedRoot) ? 8000 : 1000, 1, 12000);
        int maxMatches = NIRACapabilityArguments.GetInteger(request, "maxMatches", 20, 1, 50);

        // A name is deliberately one path segment: the model cannot use this
        // observation primitive to change its traversal root via '..' or globbing.
        if (string.IsNullOrWhiteSpace(name) ||
            name is "." or ".." ||
            name.IndexOfAny(new[] { '/', '\\', '*', '?', ':' }) >= 0 ||
            !string.Equals(name, Path.GetFileName(name), StringComparison.Ordinal))
            throw new ArgumentException("name must be a single exact file or directory name.");
        string[] roots;
        if (string.IsNullOrWhiteSpace(requestedRoot))
        {
            roots = DriveInfo.GetDrives()
                .Where(drive => drive.IsReady && drive.DriveType == DriveType.Fixed)
                .Select(drive => drive.RootDirectory.FullName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (roots.Length == 0)
                throw new InvalidOperationException(
                    "No ready fixed local drives are available for a whole-PC location search.");
        }
        else
        {
            string root = NIRACapabilityArguments.NormalizePath(requestedRoot);
            if (!Path.IsPathFullyQualified(root))
                throw new ArgumentException("root must be an absolute directory path.");
            if (!Directory.Exists(root))
                throw new DirectoryNotFoundException($"Search root does not exist: {root}");
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException(
                    "A linked/junction search root is not supported; use its real scoped path.");
            roots = new[] { root };
        }

        bool wantFiles;
        bool wantDirectories;
        if (kind.Equals("File", StringComparison.OrdinalIgnoreCase))
            (wantFiles, wantDirectories) = (true, false);
        else if (kind.Equals("Directory", StringComparison.OrdinalIgnoreCase))
            (wantFiles, wantDirectories) = (false, true);
        else if (kind.Equals("Either", StringComparison.OrdinalIgnoreCase))
            (wantFiles, wantDirectories) = (true, true);
        else
            throw new ArgumentException("kind must be File, Directory, or Either.");

        StringBuilder output = new();
        int examined = 0;
        int unreadable = 0;
        int matches = 0;
        bool truncated = false;
        List<string> truncatedRoots = new();

        foreach (string root in roots)
        {
            if (matches >= maxMatches)
            {
                truncated = true;
                break;
            }

            Queue<(string Directory, int Depth)> queue = new();
            queue.Enqueue((root, 0));
            int examinedThisRoot = 0;
            bool rootTruncated = false;

            while (queue.Count > 0 && !rootTruncated && matches < maxMatches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (directory, depth) = queue.Dequeue();
                IEnumerator<string> enumerator;
                try
                {
                    enumerator = Directory.EnumerateFileSystemEntries(directory).GetEnumerator();
                }
                catch (UnauthorizedAccessException) { unreadable++; continue; }
                catch (IOException) { unreadable++; continue; }

                using (enumerator)
                {
                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string entry;
                        try
                        {
                            if (!enumerator.MoveNext()) break;
                            entry = enumerator.Current;
                        }
                        catch (UnauthorizedAccessException) { unreadable++; break; }
                        catch (IOException) { unreadable++; break; }

                        if (examinedThisRoot >= maxEntries)
                        {
                            rootTruncated = true;
                            truncated = true;
                            truncatedRoots.Add(root);
                            break;
                        }

                        examinedThisRoot++;
                        examined++;

                        FileAttributes attributes;
                        try { attributes = File.GetAttributes(entry); }
                        catch (UnauthorizedAccessException) { unreadable++; continue; }
                        catch (IOException) { unreadable++; continue; }

                        // Never descend through symbolic links, junctions, or mount points.
                        if ((attributes & FileAttributes.ReparsePoint) != 0) continue;

                        bool isDirectory = (attributes & FileAttributes.Directory) != 0;
                        if (string.Equals(Path.GetFileName(entry), name,
                                StringComparison.OrdinalIgnoreCase) &&
                            (isDirectory ? wantDirectories : wantFiles))
                        {
                            output.AppendLine($"{(isDirectory ? "DIR" : "FILE")}\t{entry}");
                            matches++;
                            if (matches >= maxMatches)
                            {
                                truncated = true;
                                break;
                            }
                        }

                        if (isDirectory && depth < maxDepth)
                            queue.Enqueue((entry, depth + 1));
                    }
                }
            }
        }

        string resolution = matches == 1 && !truncated && unreadable == 0
            ? "Unique"
            : matches == 0 && !truncated && unreadable == 0
                ? "NotFound"
                : matches > 1 ? "Ambiguous" : "Incomplete";
        string rootSummary = roots.Length == 1
            ? roots[0]
            : string.Join(", ", roots);

        return Task.FromResult(new NIRACapabilityHandlerResult
        {
            Summary = $"LocationSearch={resolution} | Roots='{rootSummary}' | Name='{name}' | " +
                $"Kind={kind} | Matches={matches} | Examined={examined} | " +
                $"UnreadableDirectories={unreadable} | Truncated={truncated} | " +
                $"TruncatedRoots={truncatedRoots.Count} | MaxEntriesPerRoot={maxEntries} | " +
                $"MaxDepth={maxDepth}. Results are path observations, not authorization or proof of content.",
            Output = output.ToString().TrimEnd(),
            ChangedSystemState = false
        });
    }

    private static NIRACapabilityParameterDescriptor Parameter(
        string name, string type, bool required, string description) => new()
    {
        Name = name, Type = type, Required = required, Description = description
    };
}
