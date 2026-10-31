using System.Text.RegularExpressions;

namespace NIRAAgent.AI.Cognition;

/// <summary>
/// Shared public-output boundary for every reasoning/research recovery route.
/// A natural-language answer must never masquerade as an executed tool trace.
/// This tests structural protocol markers, not topic words or example queries.
/// Actual tool calls continue through the Executive's typed capability contract.
/// </summary>
internal static class NIRAPublicOutputContract
{
    private static readonly Regex ForgedTranscriptMarker = new(
        @"(?im)^\s*\[(?:BEGIN|END)\s+(?:BROWSE|SEARCH|OPEN|TOOL|RESULT|CAPABILITY|WEB|HTTP|PLAYWRIGHT)[^\]\r\n]{0,90}\]\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(150));

    private static readonly Regex ForgedToolEnvelope = new(
        @"(?im)^\s*\[(?:TOOL[_ -]CALL|TOOL[_ -]RESULT|FUNCTION[_ -]CALL|FUNCTION[_ -]RESULT)\s*[:\]]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(150));

    internal const string SynthesisInstructions =
        "\n\nPUBLIC RESPONSE PROTOCOL: Your user-visible answer is prose and " +
        "optional Markdown, never an invented tool transcript. Only actual " +
        "registered capabilityRequests can execute a search, open a URL, or " +
        "inspect a page; do not simulate those operations with bracketed " +
        "BEGIN/END markers, synthetic JSON results, or fake tool responses. " +
        "If the evidence is incomplete, explain the actual gap instead. ";

    internal static bool ContainsInternalProtocol(string? reply)
    {
        if (string.IsNullOrWhiteSpace(reply)) return false;
        // Ordinary prose, Markdown headings, code samples and citations must
        // not be rejected merely for mentioning browsing, JSON or APIs.
        return ForgedTranscriptMarker.IsMatch(reply) ||
               ForgedToolEnvelope.IsMatch(reply);
    }
}
