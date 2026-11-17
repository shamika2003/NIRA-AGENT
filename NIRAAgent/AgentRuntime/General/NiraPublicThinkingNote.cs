namespace NIRAAgent.Runtime.General;

/// <summary>
/// Opt-in, brief first-person PUBLIC commentary composed by the SAME agent
/// model when choosing a tool. Never read or show provider reasoning_content,
/// model analysis fields, tool JSON, credentials, or hidden prompts. These
/// words are not authoritative tool evidence and cannot grant an action.
/// </summary>
public static class NiraPublicThinkingNote
{
    public const string Prefix = "NIRA_PUBLIC_UPDATE: ";

    public static bool TryParse(string? content, out string note)
    {
        note = string.Empty;
        if (content is null || !content.StartsWith(Prefix, StringComparison.Ordinal) ||
            content.Length > 450) return false;
        string candidate = content[Prefix.Length..].Trim();
        if (candidate.Length is < 20 or > 360 ||
            candidate.Contains('\r') || candidate.Contains('\n') ||
            candidate.Contains('`') || candidate.Contains('<') || candidate.Contains('>') ||
            candidate.Contains('{') || candidate.Contains('}') ||
            candidate.Contains("http://", StringComparison.OrdinalIgnoreCase) ||
            candidate.Contains("https://", StringComparison.OrdinalIgnoreCase) ||
            candidate.Any(char.IsControl)) return false;
        // This is a brief editorial/status observation, not a data channel.
        // Raw model analysis markers, provider reasoning and tool argument
        // representations must never become public update content.
        string lower = candidate.ToLowerInvariant();
        if (lower.Contains("chain of thought", StringComparison.Ordinal) ||
            lower.Contains("reasoning_content", StringComparison.Ordinal) ||
            lower.Contains("<think", StringComparison.Ordinal) ||
            lower.Contains("system prompt", StringComparison.Ordinal) ||
            lower.Contains("api key", StringComparison.Ordinal) ||
            lower.Contains("password", StringComparison.Ordinal)) return false;
        note = candidate;
        return true;
    }

    public static int RunSelfCheck()
    {
        if (!TryParse(Prefix + "The important distinction is between landing on legs and a tower catch; I'll check both.", out _))
            throw new InvalidOperationException("Valid public thinking note was rejected.");
        if (TryParse("I am thinking through private steps and tool arguments", out _) ||
            TryParse(Prefix + "Here is my chain of thought for the request.", out _) ||
            TryParse(Prefix + "api key is a secret and should never appear here.", out _) ||
            TryParse(Prefix + "I have examined https://example.com and found proof.", out _))
            throw new InvalidOperationException("Non-public provider content reached the thinking-note channel.");
        return 5;
    }
}
