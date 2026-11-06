using System.Text.RegularExpressions;

namespace NIRAAgent.AI.Cognition;

/// <summary>
/// Selects NIRA's existing Ultra deep-work reasoning resource for clearly
/// substantial USER-originated research requests. This does not authorize a
/// tool, bypass Executive review, change memory, or create another agent.
/// Ordinary chat and short factual questions stay on the adaptive model.
/// </summary>
public static class NIRADeepResearchRoutePolicy
{
    private const RegexOptions Rx = RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant | RegexOptions.Compiled;

    public static bool ShouldRouteObjective(string? objective)
    {
        if (string.IsNullOrWhiteSpace(objective) || objective.Length < 70)
            return false;
        bool substantive = Regex.IsMatch(objective,
            @"\b(?:research|investigat(?:e|ion)|study|analys[ie]s?|analy[sz]e|explain|understand|compare|explore)\b", Rx);
        bool depth = Regex.IsMatch(objective,
            @"\b(?:in.depth|deep research|comprehensive|thorough|detailed|complete story|full history|" +
            @"major engineering|from the planning|timeline|step.by.step explanation)\b", Rx);
        bool grounded = Regex.IsMatch(objective,
            @"\b(?:reliable|trustworthy|authoritative|primary source|official archive|" +
            @"historical sources|museum sources|research sources|verified|fact.check|citations?)\b", Rx);
        bool complexOutput = Regex.IsMatch(objective,
            @"\b(?:photographs|historical photos|real photos|images and|visual elements|" +
            @"timeline|misconceptions|discoveries and|inventions and)\b", Rx);
        // Two independent, explicit research-quality signals are required.
        // No arbitrary named topic, provider, or image count is hardcoded.
        return substantive && ((depth && (grounded || complexOutput)) ||
            (grounded && complexOutput));
    }

    public static bool Enabled()
    {
        string value = Environment.GetEnvironmentVariable("NIRA_AUTO_DEEP_RESEARCH")?.Trim() ?? "";
        return value is not ("0" or "false" or "FALSE" or "off" or "OFF");
    }
}
