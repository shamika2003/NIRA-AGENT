using System.Text.Json;
using NIRAAgent.Capabilities;

namespace NIRAAgent.Presentation;

/// <summary>
/// UI evidence generated only from capability results in the *current run*.
/// Neither an LLM-authored completion claim nor a filename guess creates a verified card.
/// The file paths are display/copy-only; rendering does not grant further permissions.
/// </summary>
public static class NIRAVerifiedFilePresentation
{
    public static IReadOnlyList<NIRARichBlock> Attach(
        IReadOnlyList<NIRARichBlock>? modelBlocks,
        IReadOnlyDictionary<string, NIRACapabilityRequest> requests,
        IReadOnlyDictionary<string, NIRACapabilityResult> results,
        string finalReply)
    {
        var existing = NIRAPresentationPolicy.Normalize(modelBlocks);
        if (existing.Count >= NIRAPresentationPolicy.MaxBlocks ||
            requests.Count == 0 || results.Count == 0)
            return existing;

        var proven = requests
            .Where(p => results.TryGetValue(p.Key, out var result) &&
                result.Succeeded && !result.OutcomeUncertain)
            .Select(p => (Request: p.Value, Result: results[p.Key]))
            .ToArray();

        // A copy is stronger evidence of a new artifact than a preceding directory listing.
        foreach (var (request, result) in proven.Reverse())
        {
            if (request.CapabilityId != NIRACapabilityIds.FileCopy ||
                !result.ChangedSystemState ||
                !TryArgument(request.Arguments, "destination", out var destination) ||
                !TryArgument(request.Arguments, "source", out var source) ||
                !IsAbsoluteWindowsFilePath(destination))
                continue;

            return Add(existing, new NIRARichBlock
            {
                Type = "file",
                Title = FileName(destination),
                Text = destination,
                Unit = "COPY SAVED",
                Items = IsAbsoluteWindowsFilePath(source) ? new[] { source } : Array.Empty<string>()
            });
        }

        // For read-only file discovery, we only promote an actual observed FILE line
        // that agrees with the exact pathname NIRA put in the reviewed user reply.
        foreach (var (_, result) in proven.Reverse())
        {
            if (result.CapabilityId is not (NIRACapabilityIds.DirectoryList or NIRACapabilityIds.FileLocate))
                continue;
            foreach (string path in ObservedFilePaths(result.Output))
            {
                if (!IsAbsoluteWindowsFilePath(path) ||
                    string.IsNullOrWhiteSpace(finalReply) ||
                    !finalReply.Contains(path, StringComparison.OrdinalIgnoreCase))
                    continue;

                return Add(existing, new NIRARichBlock
                {
                    Type = "file",
                    Title = FileName(path),
                    Text = path,
                    Unit = "FILE FOUND"
                });
            }
        }
        return existing;
    }

    private static IReadOnlyList<NIRARichBlock> Add(
        IReadOnlyList<NIRARichBlock> existing, NIRARichBlock verified) =>
        NIRAPresentationPolicy.Normalize(existing.Concat(new[] { verified }).ToArray());

    private static bool TryArgument(JsonElement args, string key, out string value)
    {
        value = string.Empty;
        if (args.ValueKind != JsonValueKind.Object ||
            !args.TryGetProperty(key, out var element) ||
            element.ValueKind != JsonValueKind.String)
            return false;
        value = (element.GetString() ?? string.Empty).Trim();
        return value.Length is > 2 and <= 32760;
    }

    private static IEnumerable<string> ObservedFilePaths(string output)
    {
        foreach (var rawLine in (output ?? string.Empty).Split('\n'))
        {
            string[] columns = rawLine.TrimEnd('\r').Split('\t');
            if (columns.Length < 2 || columns[0] != "FILE") continue;
            // filesystem.list: FILE\tSizeBytes\tPath
            // filesystem.locate: FILE\tPath\tSizeBytes=...\tLastWriteUtc=...
            string path = columns.Length >= 3 && long.TryParse(columns[1], out _)
                ? columns[2].Trim() : columns[1].Trim();
            if (path.Length is > 2 and <= 32760) yield return path;
        }
    }

    private static string FileName(string path)
    {
        int index = path.LastIndexOfAny(new[] { '\\', '/' });
        return index < 0 ? path : path[(index + 1)..];
    }

    private static bool IsAbsoluteWindowsFilePath(string path) =>
        path.Length > 3 &&
        ((char.IsLetter(path[0]) && path[1] == ':' && (path[2] is '\\' or '/')) ||
         (path.StartsWith("\\\\", StringComparison.Ordinal) && path.Length > 5)) &&
        FileName(path).Length > 0;
}
