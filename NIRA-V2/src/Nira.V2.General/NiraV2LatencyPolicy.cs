using System.Text.RegularExpressions;
using Nira.V2.Contracts;

namespace Nira.V2.General;

/// <summary>
/// Changes provider generation effort only, never tool availability, authority,
/// NIRA identity, or the execution route. Anything ambiguous uses full reasoning.
/// No hardcoded assistant responses or persona text.
/// </summary>
public static class NiraV2LatencyPolicy
{
    private static readonly Regex SocialStart = new(
        @"^(?:(?:hi|hello|hey|hiya|yo|morning|good morning|good evening|good night|sup|what['’]s up)\b|" +
        @"(?:how are you|how have you been|how do you feel|how are you feeling)\b|" +
        @"(?:tell me about yourself|what makes you different|are you okay|you okay)\b|" +
        @"(?:o+h*\s*babe|babe|sweetie)\b)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    public static bool IsLightConversation(string request, int observationCount,
        IReadOnlyList<AgentToolSpec> tools)
    {
        if (string.IsNullOrWhiteSpace(request) || request.Length > 145 ||
            observationCount != 0 || tools.Any(t => t.Access != AccessLevel.Observe) ||
            request.Contains('\n') || request.Contains('\r') ||
            request.Contains('/') || request.Contains('\\') ||
            request.Contains('"') || request.Contains(':') ||
            request.Any(char.IsDigit)) return false;
        // An apparent social opener followed by an instruction is not fast mode.
        if (Regex.IsMatch(request,
                @"\b(?:open|create|delete|write|read|search|find|inspect|calculate|" +
                @"research|run|execute|send|change|update|complete|list|summarize)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100))) return false;
        return SocialStart.IsMatch(request.Trim());
    }
}
