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
        IReadOnlyList<string> nominated = decision.ResourceIntent.ImageSubjects ?? Array.Empty<string>();
        if (nominated.Count > 3 || nominated.Any(q => ValidQuery(q, 160).Length == 0))
            return decision;
        string[] visualSubjects = nominated.Count > 0
            ? nominated.Select(q => ValidQuery(q, 160)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : (imageQuery.Length > 0 ? new[] { imageQuery } : Array.Empty<string>());

        // Reject the whole mixed proposal if any field is unsafe. Neither web
        // research nor a plausible subject may launder an invalid sibling.
        if ((!string.IsNullOrWhiteSpace(decision.ResourceIntent.ImageQuery) && imageQuery.Length == 0) ||
            (!string.IsNullOrWhiteSpace(decision.ResourceIntent.WebQuery) && webQuery.Length == 0) ||
            (!string.IsNullOrWhiteSpace(decision.ResourceIntent.VideoQuery) && videoQuery.Length == 0) ||
            (visualSubjects.Length == 0 && webQuery.Length == 0 && videoQuery.Length == 0))
            return decision;

        if (decision.CapabilityRequests.Any(r => r.CapabilityId == NIRACapabilityIds.ImageGenerate))
            return decision;

        var requests = decision.CapabilityRequests.ToList();
        bool existingImages = requests.Any(r => r.CapabilityId is
            NIRACapabilityIds.ImageSearch or NIRACapabilityIds.ImageRetrieve);
        // The model owns image intent. One independently identified visual
        // subject gets one independent search; several named subjects do not
        // share a lucky top result. Search returns verified real raster bytes.
        // Separate source/video searches cannot substitute article thumbnails
        // for reference photos. The three-subject bound protects cost/latency.
        if (!existingImages)
        {
            foreach (string subject in visualSubjects)
                requests.Add(new NIRACapabilityRequest {
                    CapabilityId = NIRACapabilityIds.ImageSearch,
                    Arguments = JsonSerializer.SerializeToElement(new {
                        query = subject, limit = 8, retrieveBest = true,
                        retrieveCount = visualSubjects.Length > 1 ? 1 :
                            Math.Clamp(decision.ResourceIntent.ImageCount, 1, 3),
                        photographsOnly = decision.ResourceIntent.PhotographsOnly
                    }),
                    Reason = "Model-selected subject-specific verified image; independent from web article media."
                });
        }
        string[] webQueries = new[] { webQuery, videoQuery }
            .Where(q => q.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (webQueries.Length > 0 && !requests.Any(r => r.CapabilityId == NIRACapabilityIds.WebSearch))
            requests.Add(new NIRACapabilityRequest {
                CapabilityId = NIRACapabilityIds.WebSearch,
                Arguments = JsonSerializer.SerializeToElement(new {
                    queries = webQueries, maxSources = 6,
                    evidenceDepth = string.Equals(decision.ResourceIntent.WebEvidenceDepth,
                        "detailed", StringComparison.OrdinalIgnoreCase) ? "detailed" : "brief",
                    includeImages = false, imageQuery = "", retrieveCount = 1,
                    provider = "auto"
                }),
                Reason = "Independently verify the facts and optional real demonstration."
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
