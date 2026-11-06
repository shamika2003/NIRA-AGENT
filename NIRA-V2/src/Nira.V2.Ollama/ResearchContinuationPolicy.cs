using Nira.V2.Contracts;

namespace Nira.V2.Ollama;

/// <summary>
/// Explicit research execution policy. More browsing must produce useful new evidence;
/// searches do not continue merely because the model keeps requesting them.
/// The defaults are limited to the PUBLIC ResearchPilot, not NIRA's future deep-research workflow.
/// </summary>
internal static class ResearchContinuationPolicy
{
    internal const int PilotMaxSearchCalls = 4;

    internal static bool RequireSynthesis(AgentRunSnapshot run, ResearchEvidenceContext.Snapshot evidence)
    {
        if (evidence.SourceCount == 0) return false;
        // Stop exploratory searching after two successful searches if at least three
        // distinct source URLs were collected. Always reserve the last decision for synthesis.
        return (evidence.SuccessfulSearches >= 2 && evidence.SourceCount >= 3) ||
            run.ToolCallCount >= PilotMaxSearchCalls;
    }
}
