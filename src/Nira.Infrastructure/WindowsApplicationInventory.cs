using Microsoft.Win32;

namespace Nira.Infrastructure;

/// <summary>Bounded read-only application discovery. Registry registration and PATH metadata are separate evidence.</summary>
public static class WindowsApplicationInventory
{
    private static string? Bounded(string? value, int limit = 200)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string text = new(value.Trim().Where(c => !char.IsControl(c)).Take(limit).ToArray());
        return text.Length == 0 ? null : text;
    }

    public static object Inspect(IReadOnlyList<string> queries, CancellationToken ct)
    {
        var rows = new List<(string Name, string? Version)>();
        var seen = new HashSet<(string, string?)>();
        bool incomplete = false;
        const string path = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = root.OpenSubKey(path, writable: false);
                if (uninstall is null) continue;
                string[] names = uninstall.GetSubKeyNames();
                if (names.Length > 2048) incomplete = true;
                foreach (string name in names.Take(2048))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        using var entry = uninstall.OpenSubKey(name, writable: false);
                        if (entry?.GetValue("SystemComponent") is int hidden && hidden == 1) continue;
                        string? display = Bounded(entry?.GetValue("DisplayName") as string);
                        if (display is null || (queries.Count > 0 && !queries.Any(query => display.Contains(query, StringComparison.OrdinalIgnoreCase)))) continue;
                        string? version = Bounded(entry?.GetValue("DisplayVersion") as string, 80);
                        if (seen.Add((display.ToUpperInvariant(), version))) rows.Add((display, version));
                    }
                    catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException) { incomplete = true; }
                }
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException) { incomplete = true; }
        }
        var ordered = rows.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        string[] directories = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var byQuery = queries.Select(query =>
        {
            var matches = ordered.Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
            var commands = FindPathCommands(query, directories, ct);
            return new { query, detectionStatus = DetectionStatus(matches.Length, commands.Count),
                observed = matches.Length > 0 || commands.Count > 0, registeredMatchCount = matches.Length,
                registeredMatches = matches.Take(10).Select(p => new { name = p.Name, version = p.Version }).ToArray(),
                pathCommands = commands, truncated = matches.Length > 10 };
        }).ToArray();
        return new
        {
            filter = queries.Count == 1 ? queries[0] : null, queries, incomplete, truncated = rows.Count > 40,
            observedMatches = rows.Count,
            entries = ordered.Take(40).Select(p => new { name = p.Name, version = p.Version }).ToArray(),
            matchesByQuery = byQuery,
            limitations = "Registration and local PATH file presence are different observations, not proof of launchability or runtime version. A nonempty pathCommands array is positive evidence of a command file even when registeredMatchCount is zero. PATH lookup only checks exact safe command basenames in up to 128 local non-linked directories; no command execution. Portable, Store, linked, unregistered or off-PATH installations may be absent. No match means not observed, not confirmed absent."
        };
    }

    public static string DetectionStatus(int registeredCount, int pathCount) =>
        registeredCount > 0 ? (pathCount > 0 ? "registered_and_path_observed" : "registered_observed") :
        pathCount > 0 ? "path_command_observed" : "not_observed";

    // Exact command basenames only. Do not traverse arbitrary user-supplied paths, launch files,
    // scan directories, consult network shares, or return private filesystem paths.
    public static IReadOnlyList<string> FindPathCommands(string query, IEnumerable<string> directories, CancellationToken ct)
    {
        if (query.Length is < 1 or > 80 || !query.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '+') || query is "." or "..") return [];
        string[] extensions = [".exe", ".com", ".cmd", ".bat"];
        string[] names = extensions.Any(ext => query.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            ? [query] : extensions.Select(ext => query + ext).ToArray();
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in directories.Take(128))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                string path = raw.Trim().Trim('"');
                if (path.Length < 3 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] is not ('\\' or '/') || !Path.IsPathFullyQualified(path)) continue;
                path = Path.GetFullPath(path);
                string? root = Path.GetPathRoot(path);
                if (root is null || new DriveInfo(root).DriveType != DriveType.Fixed) continue;
                var ancestors = new Stack<string>();
                for (string? current = path; current is not null; current = Path.GetDirectoryName(current)) ancestors.Push(current);
                bool linked = false;
                foreach (string current in ancestors)
                {
                    var attributes = File.GetAttributes(current);
                    if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) == 0) { linked = true; break; }
                }
                if (linked) continue;
                foreach (string name in names)
                {
                    string candidate = Path.Combine(path, name);
                    if (File.Exists(candidate) && (File.GetAttributes(candidate) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) == 0)
                        found.Add(name);
                }
                if (found.Count == names.Length) break;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException) { }
        }
        return found.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
