using NIRAAgent.Capabilities;

namespace NIRAAgent.Mind;

/// <summary>
/// A background application launch is non-idempotent. Prevent a model from
/// repeatedly starting the same executable while reasoning about a single
/// user objective. Separate user runs have independent ledgers; explicitly
/// awaited short-lived command executions are not background launches.
/// </summary>
internal sealed class NIRAProcessStartContinuityGuard
{
    private readonly Dictionary<string, string> _launched = new(StringComparer.OrdinalIgnoreCase);

    internal static string? LaunchKey(NIRACapabilityRequest request)
    {
        if (request.CapabilityId != NIRACapabilityIds.ProcessStart ||
            request.Arguments.ValueKind != System.Text.Json.JsonValueKind.Object ||
            !request.Arguments.TryGetProperty("fileName", out var fileName) ||
            fileName.ValueKind != System.Text.Json.JsonValueKind.String)
            return null;
        string? value = fileName.GetString()?.Trim().Trim('"');
        return string.IsNullOrWhiteSpace(value) ? null : value.Replace('/', '\\');
    }

    // Bare executable tokens and their full paths refer to the same launched
    // program when the filename matches. Explicitly different absolute paths
    // are NOT collapsed, even if their filename is the same.
    internal static bool EquivalentExecutable(string left, string right)
    {
        static string Leaf(string path) => path[(path.LastIndexOf('\\') + 1)..]
            .Trim().ToLowerInvariant();
        static bool Bare(string path) => !path.Contains('\\') && !path.Contains('/');
        static string Normalize(string value)
        {
            string leaf = Leaf(value.Replace('/', '\\'));
            return leaf.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? leaf[..^4] : leaf;
        }
        return left.Equals(right, StringComparison.OrdinalIgnoreCase) ||
            ((Bare(left) || Bare(right)) &&
             Normalize(left).Equals(Normalize(right), StringComparison.OrdinalIgnoreCase));
    }

    internal bool TryBlock(NIRACapabilityRequest request, out string evidence)
    {
        evidence = string.Empty;
        string? key = LaunchKey(request);
        if (key is null) return false;
        string? previous = _launched.TryGetValue(key, out string? exact)
            ? exact
            : _launched.FirstOrDefault(pair => EquivalentExecutable(pair.Key, key)).Value;
        if (previous is null) return false;
        evidence = "APPLICATION LAUNCH ALREADY DISPATCHED IN THIS RUN: " + previous +
            " A second process.start for the same executable was not sent. " +
            "Inspect the original launch PID and observed window/process state, " +
            "then continue the remaining operation (or report a limitation). " +
            "Do not restart an app merely to re-identify its already observed window. " +
            "This receipt does not establish that the launched application is still running.";
        return true;
    }

    internal void Record(NIRACapabilityRequest request, NIRACapabilityResult result)
    {
        string? key = LaunchKey(request);
        if (key is null || result.CapabilityId != NIRACapabilityIds.ProcessStart ||
            result.Status != NIRACapabilityResultStatus.Succeeded)
            return;
        // A completed command-line invocation may legitimately be executed
        // more than once. Protect background launches only.
        if (NIRACapabilityArguments.GetBoolean(request, "waitForExit")) return;
        _launched.TryAdd(key, $"Executable={key}; {result.Summary}");
    }
}
