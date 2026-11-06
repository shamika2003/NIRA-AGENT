using NIRAAgent.AI.Cognition;

namespace NIRAAgent.Mind;

/// <summary>
/// Test-only migration control. This switches RUNTIMES, never AI capabilities.
/// No semantic/keyword routing: after explicit opt-in the general model chooses tools.
/// </summary>
public static class NIRAV2CanaryRoutePolicy
{
    public const string EnableValue = "EXPLICIT_READONLY";
    public const string Prefix = "/v2 ";

    public static bool TryGetRequest(NIRAMindEvent? mindEvent,
        string? trustedHostMode, out string request)
    {
        request = string.Empty;
        if (!string.Equals(trustedHostMode, EnableValue, StringComparison.Ordinal) ||
            mindEvent is null || mindEvent.Source != NIRAMindEventSource.User ||
            !string.Equals(mindEvent.Name, "UserMessage", StringComparison.Ordinal) ||
            !mindEvent.Content.StartsWith(Prefix, StringComparison.Ordinal))
            return false;
        // Attachments and foreign conversation scopes are NOT connected to V2 yet.
        if (mindEvent.Metadata.ContainsKey(NIRAImageInput.MetadataKey) ||
            mindEvent.Metadata.ContainsKey("attachedPastChatSessionId") ||
            mindEvent.Content.Contains("[Selected file paths; contents not observed]",
                StringComparison.Ordinal) ||
            mindEvent.Content.Contains("[Real image data accompanies this turn.",
                StringComparison.Ordinal))
            return false;
        string clean = mindEvent.Content[Prefix.Length..].Trim();
        if (clean.Length is < 1 or > 5000 ||
            clean.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
            return false;
        request = clean;
        return true;
    }
}
