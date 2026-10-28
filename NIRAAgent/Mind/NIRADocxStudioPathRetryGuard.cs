using System.Text.Json;
using NIRAAgent.Capabilities;

namespace NIRAAgent.Mind;

// One user run may choose a DIFFERENT new output path, but must never
// re-dispatch Word Studio to an already succeeded/occupied exact destination.
// A changed model-generated blocksJson cannot bypass this path guard.
internal sealed class NIRADocxStudioPathRetryGuard
{
    private readonly HashSet<string> _terminalDestinations = new(StringComparer.OrdinalIgnoreCase);

    internal bool Blocks(NIRACapabilityRequest request) =>
        Destination(request) is string target && _terminalDestinations.Contains(target);

    internal void Observe(NIRACapabilityRequest request, NIRACapabilityResult result)
    {
        if (Destination(request) is not string target) return;
        bool definitiveCollision = result.Status == NIRACapabilityResultStatus.Failed &&
            !result.OutcomeUncertain &&
            result.Summary.StartsWith("R48_STUDIO_DESTINATION_EXISTS:", StringComparison.Ordinal);
        if (result.Succeeded || definitiveCollision)
            _terminalDestinations.Add(target);
    }

    private static string? Destination(NIRACapabilityRequest request)
    {
        if (request.CapabilityId != NIRACapabilityIds.DocxStudioCreate ||
            request.Arguments.ValueKind != JsonValueKind.Object ||
            !request.Arguments.TryGetProperty("path", out JsonElement path) ||
            path.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(path.GetString())) return null;
        try { return NIRACapabilityArguments.NormalizePath(path.GetString()!); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or
            NotSupportedException or System.IO.IOException) { return null; }
    }
}
