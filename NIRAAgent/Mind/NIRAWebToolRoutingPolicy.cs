using System.Text.RegularExpressions;
using NIRAAgent.Capabilities;

namespace NIRAAgent.Mind;

/// <summary>
/// R73: Public information discovery is API-first. Managed browser navigation
/// is reserved for user-directed interaction and evidence-based API fallbacks.
/// This policy never bypasses authorization. It defers browser actions for
/// research-only user turns while preserving expressly interactive workflows.
/// </summary>
public static class NIRAWebToolRoutingPolicy
{
    private static readonly Regex InteractiveIntent = new(
        @"\b(?:log\s*in|sign\s*in|sign\s*out|click|fill\s*(?:in|out)?|submit|" +
        @"scroll|upload|download|checkout|purchase|order|book|reserve|" +
        @"change\s+(?:my|the|your)\s+|update\s+(?:my|the|your)\s+|" +
        @"edit\s+(?:my|the|your)\s+|delete\s+(?:my|the|your)\s+|" +
        @"navigate\s+to|(?:open|launch)\s+(?:the\s+|a\s+|my\s+)?(?:browser|chrome|chromium|playwright|website|site|page|link|url)|" +
        @"visit\s+(?:(?:this|the|a)\s+)?(?:website|site|page|url)|" +
        @"use\s+(?:the\s+|my\s+)?(?:browser|chrome|chromium|playwright))\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex NoExternalBrowser = new(
        @"\b(?:do\s*not|don't|dont|no\s+need\s+to|without|avoid|instead\s+of)\b[^.!?\r\n]{0,90}\b(?:browser|chrome|chromium|playwright|external\s+window)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool UserAskedForInteractiveBrowserWork(string? userText)
    {
        if (string.IsNullOrWhiteSpace(userText)) return false;
        if (NoExternalBrowser.IsMatch(userText)) return false;
        return InteractiveIntent.IsMatch(userText);
    }

    public static bool ShouldDefer(
        string capabilityId, string? userText, bool isTopLevelUserTurn,
        bool hasApiSourceEvidence, bool hasAttemptedApiResearch)
    {
        if (!isTopLevelUserTurn) return false;
        if (capabilityId is not (
            NIRACapabilityIds.BrowserSessionOpen or
            NIRACapabilityIds.BrowserNavigate or
            NIRACapabilityIds.BrowserInspect or
            NIRACapabilityIds.BrowserFollow or
            NIRACapabilityIds.BrowserExplore or
            NIRACapabilityIds.BrowserBack or
            NIRACapabilityIds.BrowserRequest or
            NIRACapabilityIds.BrowserPageSelect or
            NIRACapabilityIds.BrowserClick or
            NIRACapabilityIds.BrowserFill or
            NIRACapabilityIds.BrowserSelect or
            NIRACapabilityIds.BrowserWait or
            NIRACapabilityIds.BrowserAuthenticate or
            NIRACapabilityIds.BrowserDownload or
            NIRACapabilityIds.BrowserUpload or
            NIRACapabilityIds.BrowserScreenshot))
            return false;
        // An explicit refusal of a separate browser remains authoritative
        // even when the API lacks sources. The native in-chat player is not
        // a browser session capability and remains unaffected.
        if (NoExternalBrowser.IsMatch(userText ?? string.Empty)) return true;
        if (UserAskedForInteractiveBrowserWork(userText)) return false;
        // Successful read-only sources are sufficient to attempt synthesis, not
        // permission to launch Playwright as a second search engine. If an API
        // attempt genuinely failed to produce sources, browser fallback is possible.
        return hasApiSourceEvidence || !hasAttemptedApiResearch;
    }
}
