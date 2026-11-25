using System.Text.Json;

namespace Nira.Core;

/// <summary>Bind a desktop launch proposal to an exact successful observation in the current task.</summary>
internal static class ApplicationLaunchEvidence
{
    internal static bool Allows(ConversationState state, JsonElement args)
    {
        string? path = args.GetProperty("executablePath").GetString();
        string? fingerprint = args.GetProperty("fingerprint").GetString();
        foreach (var operation in state.Operations.Where(o => o.ToolName == "desktop.application_find" && o.Status == OperationStatus.Succeeded && o.ResultJson is not null))
        {
            try
            {
                using var doc = JsonDocument.Parse(operation.ResultJson!);
                var root = doc.RootElement;
                if (!root.TryGetProperty("Success", out var success) || success.ValueKind != JsonValueKind.True ||
                    !root.TryGetProperty("Data", out var data) || data.ValueKind != JsonValueKind.Object ||
                    !data.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array) continue;
                foreach (var item in candidates.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("ExecutablePath", out var observedPath) && observedPath.ValueKind == JsonValueKind.String &&
                        item.TryGetProperty("Fingerprint", out var observedFingerprint) && observedFingerprint.ValueKind == JsonValueKind.String &&
                        string.Equals(observedPath.GetString(), path, StringComparison.Ordinal) && string.Equals(observedFingerprint.GetString(), fingerprint, StringComparison.Ordinal)) return true;
                }
            }
            catch (JsonException) { }
        }
        return false;
    }
}
