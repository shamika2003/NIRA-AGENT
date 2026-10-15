/*
 * filename: NIRAFileLocationCapabilityHandler.cs
 * Bounded, observation-only discovery under an evidence-grounded directory.
 * It never selects a write target or grants access to the discovered path.
 */
using System.Text;

namespace NIRAAgent.Capabilities;

public sealed class NIRAFileLocationCapabilityHandler : INIRACapabilityHandler
{
    private static readonly EnumerationOptions SafeEnumeration =
        new()
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

    public NIRACapabilityDescriptor Descriptor { get; } =
        new NIRACapabilityDescriptor
        {
            Id = NIRACapabilityIds.FileLocate,
            Description =
                "Find a filesystem file or folder by exact object name with bounded observation-only traversal. " +
                "For installed-application identity/location by human application name, prefer application.resolve; " +
                "do not guess conventional installation roots and use this as a substitute for Windows application discovery. " +
                "Use a grounded absolute root when one is known. If no root is supplied, the " +
                "handler first searches Windows-resolved user known folders (such as the current redirected Desktop), then ready fixed local drives, so an explicit whole-PC exact-name locate " +
                "request does not require the user to invent a base directory. File matches include " +
                "basic metadata (size and last-write time) so a location/size request does not need " +
                "a second filesystem.metadata call. It never accesses file contents.",
            DefaultRisk = NIRACapabilityRisk.Observe,
            Parameters = new[]
            {
                Parameter("root", "string", false, "Optional grounded absolute directory. Omit for scope=CurrentUserFolder or for a whole-PC search."),
                Parameter("scope", "string", false, "Optional CurrentUserFolder resolves exactly one OS-owned current-user known directory (Documents, Downloads, Desktop, Pictures, Music, Videos). Omit for unchanged exact-name discovery."),
                Parameter("name", "string", true, "Exact file or folder name, not a wildcard or path."),
                Parameter("kind", "string", false, "Directory (folder), File or Either (both). Default Either. Use these values, not arbitrary kinds."),
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
        string? scope = NIRACapabilityArguments.GetOptionalString(request, "scope", 40);
        string kind = (NIRACapabilityArguments.GetOptionalString(request, "kind", 32)
            ?? "Either").Trim();
        // Canonicalize common *meaning-equivalent* labels without broadening the
        // allowed search kind or guessing a different target.
        if (kind.Equals("Folder", StringComparison.OrdinalIgnoreCase) ||
            kind.Equals("Dir", StringComparison.OrdinalIgnoreCase) ||
            kind.Equals("Directories", StringComparison.OrdinalIgnoreCase))
            kind = "Directory";
        else if (kind.Equals("Files", StringComparison.OrdinalIgnoreCase))
            kind = "File";
        else if (kind.Equals("Both", StringComparison.OrdinalIgnoreCase) ||
            kind.Equals("Any", StringComparison.OrdinalIgnoreCase))
            kind = "Either";
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
        // Opt-in OS-owned location, not a search through other users' folders.
        // Default filesystem.locate behavior and all legacy callers are untouched.
        if (!string.IsNullOrWhiteSpace(scope))
        {
            if (!scope.Equals("CurrentUserFolder", StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrWhiteSpace(requestedRoot))
                throw new ArgumentException("scope must be CurrentUserFolder with no root.");
            if (!kind.Equals("Directory", StringComparison.OrdinalIgnoreCase) &&
                !kind.Equals("Either", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("CurrentUserFolder can only resolve directories.");
            string folder = ResolveCurrentUserFolder(name);
            return Task.FromResult(new NIRACapabilityHandlerResult
            {
                Summary = $"LocationSearch=Unique | Scope=CurrentUserFolder | Name='{name}' | " +
                    "Kind=Directory | Matches=1 | Truncated=False | Origin=WindowsUserProfile",
                Output = $"MatchEvidence=WindowsCurrentUserKnownFolder\nDIR\t{folder}",
                ChangedSystemState = false
            });
        }

        string[] roots;
        if (string.IsNullOrWhiteSpace(requestedRoot))
        {
            roots = ResolveWholePcSearchRoots();

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
                    enumerator = Directory.EnumerateFileSystemEntries(
                        directory,
                        "*",
                        SafeEnumeration).GetEnumerator();
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
                            if (isDirectory)
                            {
                                output.AppendLine($"DIR\t{entry}");
                            }
                            else
                            {
                                try
                                {
                                    FileInfo fileInfo =
                                        new(entry);

                                    output.AppendLine(
                                        $"FILE\t{entry}\tSizeBytes={fileInfo.Length}\t" +
                                        $"LastWriteUtc={fileInfo.LastWriteTimeUtc:O}");
                                }
                                catch (UnauthorizedAccessException)
                                {
                                    output.AppendLine(
                                        $"FILE\t{entry}\tSizeBytes=Unknown\tLastWriteUtc=Unknown");
                                }
                                catch (IOException)
                                {
                                    output.AppendLine(
                                        $"FILE\t{entry}\tSizeBytes=Unknown\tLastWriteUtc=Unknown");
                                }
                            }

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
            Output =
                (matches > 0
                    ? "MatchEvidence=ExactNamePathObservation\n" +
                      "FileMetadataIncluded=True\n"
                    : string.Empty) +
                output.ToString().TrimEnd(),
            ChangedSystemState = false
        });
    }

    // Opt-in known-user-folder resolution. Exact OS directory identities are
    // architectural targets, not a list of example filenames/test responses.
    // A missing or inaccessible folder is reported as such; never silently
    // scan the whole drive or another account's profile to guess a replacement.
    internal static string ResolveCurrentUserFolder(string name)
    {
        string? path = name.Trim().ToLowerInvariant() switch
        {
            "desktop" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "pictures" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "music" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            "videos" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            "downloads" => ResolveCurrentUserDownloads(),
            _ => throw new ArgumentException("Unknown current-user known folder name.")
        };
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) ||
            !Directory.Exists(path))
            throw new DirectoryNotFoundException(
                $"Current-user known folder '{name}' is unavailable; provide its actual path.");
        return Path.GetFullPath(path);
    }

    private static string? ResolveCurrentUserDownloads()
    {
        // Windows redirects Downloads through the per-user Shell Folders value.
        // Use the value only when present, not a guessed different user's path.
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using Microsoft.Win32.RegistryKey? key =
                    Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders");
                string? configured = key?.GetValue(
                    "{374DE290-123F-4565-9164-39C4925E467B}") as string;
                if (!string.IsNullOrWhiteSpace(configured))
                    return Environment.ExpandEnvironmentVariables(configured);
            }
            catch (System.Security.SecurityException) { }
            catch (UnauthorizedAccessException) { }
        }
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(profile) ? null : Path.Combine(profile, "Downloads");
    }

    // Windows Known Folder resolution respects OneDrive/enterprise redirection.
    // Do not substitute C:\Users\Default, Public, or a guessed user name.
    // These are read-only search roots; the explicit absolute-root contract
    // remains unchanged.
    internal static string[] ResolveWholePcSearchRoots()
    {
        List<string> roots = new();
        void AddDirectory(string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate) ||
                !Path.IsPathFullyQualified(candidate) ||
                !Directory.Exists(candidate)) return;
            string resolved = Path.GetFullPath(candidate);
            if (!roots.Contains(resolved, StringComparer.OrdinalIgnoreCase))
                roots.Add(resolved);
        }

        AddDirectory(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        // Some Windows setups redirect the user's Desktop through OneDrive.
        // The environment exposes the provider's actual configured root.
        foreach (string variable in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
        {
            string? providerRoot = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(providerRoot))
                AddDirectory(Path.Combine(providerRoot, "Desktop"));
        }
        AddDirectory(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop"));

        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                AddDirectory(drive.RootDirectory.FullName);
        }
        return roots.ToArray();
    }

    private static NIRACapabilityParameterDescriptor Parameter(
        string name, string type, bool required, string description) => new()
    {
        Name = name, Type = type, Required = required, Description = description
    };
}
