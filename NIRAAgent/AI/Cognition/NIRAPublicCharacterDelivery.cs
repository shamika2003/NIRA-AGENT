using System.Diagnostics;
using System.Text.RegularExpressions;
using NIRAAgent.Mind;

namespace NIRAAgent.AI.Cognition;

// Single policy for early public terminal exits, which do not pass through
// the Executive's regular post-commit response route. The normal route keeps
// its own goal/evidence review and realization. This boundary NEVER owns state,
// creates a capability, or changes an authorization decision.
public enum NIRAPublicReplyKind
{
    Natural,
    VerifiedResult,
    Protected,
    PreserveExact,
    Progress
}

public static class NIRAPublicCharacterDelivery
{
    private const int MaximumEarlyNaturalReplyCharacters = 1600;

    // Explicit categories are assigned at trusted call sites, NOT guessed
    // from user message keywords or the model's mood/character-ready flags.
    public static bool NeedsNaturalRealization(NIRAPublicReplyKind kind, string? draft) =>
        (kind is NIRAPublicReplyKind.Natural or NIRAPublicReplyKind.VerifiedResult) &&
        !string.IsNullOrWhiteSpace(draft) &&
        draft.Length <= MaximumEarlyNaturalReplyCharacters &&
        !LooksLikeVerbatimArtifact(draft);

    // Executable source, serialized outputs and large verified branch results
    // should not be reauthored for personality. They remain exact.
    public static bool LooksLikeVerbatimArtifact(string? draft) =>
        string.IsNullOrEmpty(draft) ||
        draft.Contains("```", StringComparison.Ordinal) ||
        draft.Contains("<xml", StringComparison.OrdinalIgnoreCase) ||
        draft.TrimStart().StartsWith('{') ||
        draft.TrimStart().StartsWith('[') && draft.Contains(']') ||
        draft.Length > MaximumEarlyNaturalReplyCharacters;

    // Guard specific observed data: path, URL, percentages, numbers, and IDs.
    // Anchors protect against replacing exact values in a completed action.
    private static readonly Regex GroundedTokens = new(
        @"(?<![A-Za-z0-9_])(?:[A-Za-z]:\\[^\s\r\n<>""|?*]+|https?://[^\s<>""']+|\b\d+(?:[.,:/-]\d+)*(?:\s?%)?\b)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static IReadOnlyList<string> Anchors(string source) =>
        GroundedTokens.Matches(source ?? string.Empty)
            .Select(m => m.Value.Trim().TrimEnd('.', ',', ';', ':', ')', ']'))
            .Where(token => token.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(48)
            .ToArray();

    public static bool PreservesGroundedResult(string original, string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return false;
        IReadOnlyList<string> originalAnchors = Anchors(original);
        IReadOnlyList<string> candidateAnchors = Anchors(candidate);
        // The model may change presentation, but it cannot add/remove
        // concrete facts. A candidate that changes evidence is discarded.
        return originalAnchors.All(x => candidate.Contains(x, StringComparison.Ordinal)) &&
               candidateAnchors.All(x => original.Contains(x, StringComparison.Ordinal));
    }

    public static async Task<string> DeliverAsync(
        NIRAResponseRealizationService realization,
        NIRAMindEvent mindEvent,
        string groundedDraft,
        NIRAPublicReplyKind kind,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(realization);
        ArgumentNullException.ThrowIfNull(mindEvent);
        if (!NeedsNaturalRealization(kind, groundedDraft))
        {
            Debug.WriteLine($"[PublicCharacterDelivery] PROTECTED | Kind={kind} | Event={mindEvent.Name}");
            return groundedDraft;
        }

        try
        {
            // Use the SAME production character state, attitude, social history,
            // personality assets and realization validator as normal chat.
            NIRAResponseRealizationResult result = await realization.RealizeAsync(
                new NIRAResponseRealizationRequest
                {
                    DraftReply = groundedDraft,
                    EventSource = mindEvent.Source.ToString(),
                    EventName = mindEvent.Name,
                    TopicKey = mindEvent.TopicKey,
                    EventContent = mindEvent.Source == NIRAMindEventSource.User
                        ? mindEvent.Content : string.Empty,
                    DecisionSummary = kind == NIRAPublicReplyKind.VerifiedResult
                        ? "A trusted runtime action or reviewed background result is already committed. " +
                          "Express it naturally without changing status, facts, paths, limits or evidence."
                        : "A real delegated action is in progress. Keep the status truthful; " +
                          "do not imply that the work has already finished.",
                    RequiredVerbatimFragments = Anchors(groundedDraft)
                }, cancellationToken);
            if (!PreservesGroundedResult(groundedDraft, result.Reply))
            {
                Debug.WriteLine("[PublicCharacterDelivery] REVERT | Evidence changed");
                return groundedDraft;
            }
            Debug.WriteLine($"[PublicCharacterDelivery] DELIVERED | Kind={kind} | " +
                $"Draft={groundedDraft.Length} | Final={result.Reply.Length}");
            return result.Reply;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PublicCharacterDelivery] FALLBACK | {ex.GetType().Name}");
            return groundedDraft;
        }
    }
}
