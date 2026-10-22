using System.Text.Json;
using NIRAAgent.Capabilities;

namespace NIRAAgent.AI.Cognition;

/// <summary>
/// Converts OPTIONAL model-selected, high-value resource intentions into
/// ordinary capability requests without another classification model call.
/// NEVER infers media from topic words, item names, user test prompts or domains.
/// The Executive still applies normal capability schema/permission/evidence rules.
/// </summary>
public static class NIRAResourceIntentPlanner
{
    public static NIRACognitionDecision Apply(
        NIRACognitionDecision decision, bool isInitialUserCycle)
    {
        if (!isInitialUserCycle || decision.ResourceIntent is null ||
            decision.State is not (NIRACognitionState.Complete or NIRACognitionState.Continue) ||
            decision.ReplyPresentation == NIRAReplyPresentationMode.PreserveExact ||
            decision.ControlRequests.Count > 0 || decision.ReviewCommitment ||
            decision.GoalProposals.Count > 0 || decision.BranchProposals.Count > 0 ||
            decision.BranchWorkProposals.Count > 0 ||
            decision.DynamicToolProposals.Count > 0 || decision.DynamicToolInvocations.Count > 0)
            return decision;

        // These are model-generated SEARCH QUERIES, not executable URLs or paths.
        // Reject malformed input rather than rewriting or extending it.
        static string ValidQuery(string? value, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            // Validate the RAW model field before trimming. Trimming first can
            // launder a leading newline or another control character into a
            // seemingly valid query. No model-provided URL is a search phrase.
            if (value.Any(char.IsControl) || value.Contains('\u2028') ||
                value.Contains('\u2029'))
                return string.Empty;
            string trimmed = value.Trim();
            if (trimmed.Length < 3 || trimmed.Length > max ||
                trimmed.Contains("https://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("http://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("www.", StringComparison.OrdinalIgnoreCase) ||
                Uri.TryCreate(trimmed, UriKind.Absolute, out _))
                return string.Empty;
            return trimmed;
        }

        string imageQuery = ValidQuery(decision.ResourceIntent.ImageQuery, 160);
        string webQuery = ValidQuery(decision.ResourceIntent.WebQuery, 250);
        string videoQuery = ValidQuery(decision.ResourceIntent.VideoQuery, 250);

        // This is one model-selected intent, not three unrelated grants. If
        // ANY provided nonblank query is malformed, discard the entire plan;
        // never execute a valid-looking remainder of untrusted mixed input.
        if ((!string.IsNullOrWhiteSpace(decision.ResourceIntent.ImageQuery) && imageQuery.Length == 0) ||
            (!string.IsNullOrWhiteSpace(decision.ResourceIntent.WebQuery) && webQuery.Length == 0) ||
            (!string.IsNullOrWhiteSpace(decision.ResourceIntent.VideoQuery) && videoQuery.Length == 0) ||
            (imageQuery.Length == 0 && webQuery.Length == 0 && videoQuery.Length == 0))
            return decision;

        // Explicit image generation remains an entirely separate user-requested
        // creation task. Never replace it with web image retrieval.
        if (decision.CapabilityRequests.Any(r => r.CapabilityId == NIRACapabilityIds.ImageGenerate))
            return decision;

        var requests = decision.CapabilityRequests.ToList();
        // Do not duplicate capability families the model has already planned.
        if (imageQuery.Length > 0 && !requests.Any(r => r.CapabilityId is
                NIRACapabilityIds.ImageSearch or NIRACapabilityIds.ImageRetrieve))
            requests.Add(new NIRACapabilityRequest {
                CapabilityId = NIRACapabilityIds.ImageSearch,
                // Opted in by the SAME model decision: download one provider-ranked,
                // allowlisted candidate within this authorized read-only operation.
                // No extra LLM call just to copy a candidate ID into image.retrieve.
                Arguments = JsonSerializer.SerializeToElement(new {
                    query = imageQuery, limit = 6, retrieveBest = true,
                    retrieveCount = Math.Clamp(decision.ResourceIntent.ImageCount, 1, 3)
                }),
                Reason = "Model-selected real visual evidence materially improves this answer."
            });
        // One web request can cover independent documentation and a published
        // demonstration without spending a second paid provider request.
        string[] webQueries = new[] { webQuery, videoQuery }
            .Where(q => q.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (webQueries.Length > 0 && !requests.Any(r => r.CapabilityId == NIRACapabilityIds.WebSearch))
            requests.Add(new NIRACapabilityRequest {
                CapabilityId = NIRACapabilityIds.WebSearch,
                Arguments = JsonSerializer.SerializeToElement(new {
                    queries = webQueries, maxSources = 6,
                    evidenceDepth = string.Equals(decision.ResourceIntent.WebEvidenceDepth,
                        "detailed", StringComparison.OrdinalIgnoreCase) ? "detailed" : "brief"
                }),
                Reason = "Model-selected published evidence would substantively improve the answer."
            });
        if (requests.Count == decision.CapabilityRequests.Count) return decision;

        // ConversationOnly and SelfContainedAnswer are model-classification
        // hints, not permission to discard the SAME model's concrete evidence
        // plan. A valid selected resource makes this an observable work turn.
        // Do not infer a tool from the user text, or modify images/media here.
        // An answer isn't final until the proposed evidence has been observed.
        // One ordinary Executive dispatch batch; next model cycle synthesizes
        // actual results. Tool failures must still lead to useful text, not fiction.
        return decision with {
            State = NIRACognitionState.Continue,
            EmitReply = false,
            ReplyReady = false,
            CharacterReady = false,
            ConversationOnly = false,
            SelfContainedAnswer = false,
            CapabilityRequests = requests.ToArray(),
            ResourceIntent = null
        };
    }
}
