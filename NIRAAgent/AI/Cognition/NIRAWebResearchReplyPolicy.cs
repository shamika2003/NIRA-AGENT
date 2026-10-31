using System.Text;
using System.Text.Json;
using NIRAAgent.Capabilities;
using NIRAAgent.Presentation;
using NIRAAgent.Mind;

namespace NIRAAgent.AI.Cognition;

/// <summary>
/// Authoritative shape-only guard for a successful source lookup followed by an
/// unusable model decision. Does not infer facts, select tools, or rewrite
/// source text. Used ONLY after an observed successful web.search receipt.
/// </summary>
public static class NIRAWebResearchReplyPolicy
{
    // Compact read-only research synthesis has no expanded action schema.
    // An action emitted anyway must not be sent into the generic Executive
    // tool path; normal contextRequests-based expansion remains available.
    // Check structure, not domain names, URLs, test phrases or model choice.
    public static bool HasOutOfContractAction(NIRACognitionDecision decision) =>
        decision.CapabilityRequests.Count != 0 ||
        decision.ControlRequests.Count != 0 ||
        decision.DynamicToolInvocations.Count != 0 ||
        decision.DynamicToolProposals.Count != 0 ||
        decision.GoalProposals.Count != 0 ||
        decision.BranchProposals.Count != 0 ||
        decision.BranchWorkProposals.Count != 0;

    public static bool IsSilentTerminal(NIRACognitionDecision decision)
    {
        if (decision.State is not (NIRACognitionState.Complete or
            NIRACognitionState.Blocked or NIRACognitionState.Wait or
            NIRACognitionState.Continue)) return false;
        if (decision.EmitReply && !string.IsNullOrWhiteSpace(decision.Reply))
            return false;
        // A real continuation or a pending authoritative mutation must not be
        // replaced with a synthetic terminal reply.
        return decision.CapabilityRequests.Count == 0 &&
               decision.ContextRequests.Count == 0 &&
               decision.CapabilityIds.Count == 0 &&
               decision.MemorySearches.Count == 0 &&
               decision.ConversationSearches.Count == 0 &&
               decision.GoalProposals.Count == 0 &&
               decision.BranchProposals.Count == 0 &&
               decision.BranchWorkProposals.Count == 0 &&
               decision.DynamicToolProposals.Count == 0 &&
               decision.DynamicToolInvocations.Count == 0 &&
               decision.ControlRequests.Count == 0 &&
               !decision.ReviewCommitment;
    }

    public static bool IsGroundedAnswerShape(NIRACognitionDecision decision) =>
        decision.State == NIRACognitionState.Complete &&
        decision.EmitReply && !string.IsNullOrWhiteSpace(decision.Reply) &&
        decision.CapabilityRequests.Count == 0 &&
        decision.ContextRequests.Count == 0 &&
        decision.CapabilityIds.Count == 0 &&
        decision.MemorySearches.Count == 0 &&
        decision.ConversationSearches.Count == 0 &&
        decision.GoalProposals.Count == 0 &&
        decision.BranchProposals.Count == 0 &&
        decision.BranchWorkProposals.Count == 0 &&
        decision.DynamicToolProposals.Count == 0 &&
        decision.DynamicToolInvocations.Count == 0 &&
        decision.ControlRequests.Count == 0 &&
        !decision.ReviewCommitment;

    // The narrow source-review path applies to informational research with
    // verified reference images as well as text-only research. Image lookup
    // is still read-only and its factual claims must be grounded in web
    // extracts; no file mutation, browser action or uncertain result qualifies.
    public static bool IsFocusedResearchEvidenceSet(
        IReadOnlyCollection<NIRACapabilityResult> results) =>
        results is { Count: > 0 } &&
        results.Any(r => r.CapabilityId == NIRACapabilityIds.WebSearch && r.Succeeded) &&
        results.All(r =>
            (r.CapabilityId is NIRACapabilityIds.WebSearch or
                NIRACapabilityIds.ImageSearch or NIRACapabilityIds.ImageRetrieve) &&
            r.Succeeded && r.Risk == NIRACapabilityRisk.Observe &&
            !r.OutcomeUncertain && !r.ChangedSystemState);

    // A successful read-only web receipt can support a source-only PARTIAL
    // handoff after exhausted synthesis. Optional image errors do not erase
    // source evidence. No mutation or uncertain-capability results are allowed.
    internal static string? ObservedSourceDigestFromResults(
        IReadOnlyList<NIRACapabilityResult> observedResults,
        string? userObjective = null)
    {
        if (observedResults is null || observedResults.Count == 0 ||
            observedResults.Any(result => result.Risk != NIRACapabilityRisk.Observe ||
                result.ChangedSystemState || result.OutcomeUncertain))
            return null;
        // Reconsideration can legitimately produce more than one successful
        // web.search result. The old exactly-one check erased ALL retrieved
        // evidence after repeated NeedsWork review verdicts. Prefer the newest
        // structurally validated receipt; never merge snippets into claims.
        // This remains failure-only and never certifies a model-written draft.
        foreach (var web in observedResults.Reverse())
        {
            if (web.CapabilityId != NIRACapabilityIds.WebSearch || !web.Succeeded)
                continue;
            try
            {
                using JsonDocument receipt = JsonDocument.Parse(web.Output);
                string? digest = ObservedSourceDigestFromPayload(receipt.RootElement, userObjective);
                if (digest is not null) return digest;
            }
            catch (JsonException) { }
            catch (InvalidOperationException) { }
        }
        return null;
    }

    // Failure-only, source-attributed partial handoff. The reviewer is still
    // mandatory to certify a model-written factual answer; this never releases
    // the unchecked draft or interprets a search snippet as proven truth.
    // A real successful read is worth more to the user than a blanket apology.
    public static string? ObservedSourceHandoff(
        IReadOnlyList<NIRACapabilityResult> observedResults,
        string? userObjective = null)
    {
        if (observedResults is null) return null;
        foreach (var result in observedResults)
        {
            if (result.CapabilityId != NIRACapabilityIds.WebSearch ||
                !result.Succeeded || result.Risk != NIRACapabilityRisk.Observe ||
                result.OutcomeUncertain || result.ChangedSystemState) continue;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(result.Output);
                JsonElement root = NIRAWebEvidenceIdentityPolicy.RelevantSourcePayload(
                    doc.RootElement, userObjective);
                if (!root.TryGetProperty("evidenceType", out var kind) ||
                    kind.GetString() != "UntrustedPublicWebSearchExtracts" ||
                    !root.TryGetProperty("completed", out var completed) ||
                    completed.ValueKind != JsonValueKind.True ||
                    !root.TryGetProperty("sources", out var sources) ||
                    sources.ValueKind != JsonValueKind.Array ||
                    !root.TryGetProperty("sourceCount", out var count) ||
                    !count.TryGetInt32(out int expected) || expected != sources.GetArrayLength() ||
                    expected is < 1 or > 8) continue;
                StringBuilder reply = new(
                    "I found published sources, but my independent answer check didn't complete. " +
                    "I won't present unreviewed specifications as verified. " +
                    "Here are the source pages I located:\n");
                int added = 0;
                foreach (var source in sources.EnumerateArray())
                {
                    if (source.ValueKind != JsonValueKind.Object ||
                        !source.TryGetProperty("url", out var address) ||
                        address.ValueKind != JsonValueKind.String ||
                        !NIRAWebSearchParsing.SafeSourceUrl(address.GetString() ?? "") ||
                        !source.TryGetProperty("extract", out var extract) ||
                        extract.ValueKind != JsonValueKind.String ||
                        string.IsNullOrWhiteSpace(extract.GetString())) continue;
                    string url = address.GetString()!;
                    string title = source.TryGetProperty("title", out var display) &&
                        display.ValueKind == JsonValueKind.String ? display.GetString() ?? "" : "";
                    // Escaping title control/Markdown makes untrusted provider
                    // strings data, not user-facing instructions or forged links.
                    title = new string(title.Where(c => !char.IsControl(c)).Take(100).ToArray());
                    foreach (char marker in new[] { '[', ']', '(', ')', '*', '`' })
                        title = title.Replace(marker.ToString(), "");
                    if (string.IsNullOrWhiteSpace(title)) title = "Found source";
                    reply.Append("- [").Append(title.Trim()).Append("](")
                        .Append(url).AppendLine(")");
                    if (++added == 3) break;
                }
                if (added > 0) return reply.ToString().TrimEnd();
            }
            catch (JsonException) { }
            catch (InvalidOperationException) { }
        }
        return null;
    }

    // Regression guard: a successful source lookup must not vanish into a
    // long series of speculative web searches and a generic "search failed"
    // message. This routine is used ONLY after a bounded sequence of real,
    // successful, read-only web.search receipts and unsuccessful synthesis.
    // It never upgrades snippets to verified facts, executes URLs, or invents
    // video pages. Its sole media sources are direct, structurally observed
    // watch-page URLs returned by the already-authorized search provider.
    internal static NIRACognitionDecision? BoundedSourceHandoff(
        IReadOnlyList<NIRACapabilityResult> observedResults,
        string? userObjective = null)
    {
        if (observedResults is null || observedResults.Count == 0 ||
            observedResults.Any(result =>
                result.Risk != NIRACapabilityRisk.Observe ||
                result.ChangedSystemState || result.OutcomeUncertain))
            return null;

        // Source-only recovery must never convert an incidental retrieved video
        // into the user's requested answer. Native video delivery stays under
        // ordinary user-request/media-intent and verified-reference routing.
        // This is a failure report, not another tool planner.

        string? links = ObservedSourceHandoff(observedResults, userObjective) ??
            ObservedSourceDigestFromResults(observedResults, userObjective);
        if (string.IsNullOrWhiteSpace(links) &&
            NIRAWebEvidenceIdentityPolicy.FirstRequestedUrl(userObjective) is not null)
        {
            // Three successful API receipts that never identify the actual
            // user-supplied resource cannot justify quoting similar results.
            // End the bounded research attempt truthfully, not with unrelated
            // page excerpts or another browser/search detour.
            return new NIRACognitionDecision {
                State = NIRACognitionState.Blocked,
                EmitReply = true,
                Reply = "I searched for the link you provided, but the returned sources " +
                    "did not reliably identify that exact resource. I can't verify " +
                    "its details from those results, so I won't substitute a " +
                    "similarly named page or make up an answer.",
                Speech = string.Empty,
                ReplyReady = true,
                CharacterReady = true,
                DisplayBlocks = Array.Empty<NIRARichBlock>(),
                DecisionSummary = "User-linked resource was not identified in the observed search evidence."
            };
        }
        if (string.IsNullOrWhiteSpace(links)) return null;
        return new NIRACognitionDecision {
            State = NIRACognitionState.Complete,
            EmitReply = true, Reply = links, Speech = string.Empty,
            ReplyReady = false, CharacterReady = false,
            DisplayBlocks = Array.Empty<NIRARichBlock>(),
            DecisionSummary = "Bounded attributed source handoff after repeated web research."
        };
    }

    // Only a syntax-repair-produced Blocked decision is treated as a failed
    // answer envelope. Genuine authorization/safety denials are NOT recovered.
    internal static bool IsFailedResearchEnvelope(
        NIRACognitionDecision decision, bool syntaxRepairUsed) =>
        IsSilentTerminal(decision) ||
        (syntaxRepairUsed && decision.State == NIRACognitionState.Blocked &&
         decision.CapabilityRequests.Count == 0 &&
         decision.ControlRequests.Count == 0 &&
         decision.GoalProposals.Count == 0 &&
         decision.BranchProposals.Count == 0 &&
         decision.DynamicToolInvocations.Count == 0);

    // A successful read-only search should not disappear because an answer model
    // produced malformed JSON. Show the actual attributable source excerpts as
    // a PARTIAL, explicitly unverified result; never compose new factual claims.
    // The caller must have a structurally accepted Executive web.search receipt.
    internal static string? ObservedSourceDigest(string capabilityEvidence)
    {
        if (string.IsNullOrWhiteSpace(capabilityEvidence) ||
            !capabilityEvidence.Contains("CapabilityId: web.search", StringComparison.Ordinal) ||
            !capabilityEvidence.Contains("Status: Succeeded", StringComparison.Ordinal))
            return null;
        int marker = capabilityEvidence.IndexOf("\nOutput:", StringComparison.Ordinal);
        if (marker < 0) return null;
        int start = capabilityEvidence.IndexOf('\n', marker + 1);
        if (start < 0) return null;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(capabilityEvidence[(start + 1)..].Trim());
            return ObservedSourceDigestFromPayload(doc.RootElement);
        }
        catch (JsonException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    // For failure handling after a trusted capability operation; the payload is
    // validated here as well, so no arbitrary text or forged URLs are promoted.
    internal static string? ObservedSourceDigestFromPayload(JsonElement payload, string? userObjective = null)
    {
        payload = NIRAWebEvidenceIdentityPolicy.RelevantSourcePayload(payload, userObjective);
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("evidenceType", out var kind) ||
            kind.ValueKind != JsonValueKind.String ||
            kind.GetString() != "UntrustedPublicWebSearchExtracts" ||
            !payload.TryGetProperty("completed", out var completed) ||
            completed.ValueKind != JsonValueKind.True ||
            !payload.TryGetProperty("sourceCount", out var total) ||
            !total.TryGetInt32(out int count) || count is < 1 or > 8 ||
            !payload.TryGetProperty("sources", out var sources) ||
            sources.ValueKind != JsonValueKind.Array ||
            sources.GetArrayLength() != count) return null;

        StringBuilder reply = new("I found published information relevant to your question. " +
            "I couldn't independently settle every claim, so here are the " +
            "specific source findings rather than an empty answer. " +
            "These are attributed excerpts, not verified results:\n\n");
        int added = 0;
        foreach (JsonElement source in sources.EnumerateArray())
        {
            if (source.ValueKind != JsonValueKind.Object ||
                !source.TryGetProperty("url", out var link) ||
                link.ValueKind != JsonValueKind.String ||
                !NIRAWebSearchParsing.SafeSourceUrl(link.GetString() ?? "") ||
                !source.TryGetProperty("extract", out var excerpt) ||
                excerpt.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(excerpt.GetString())) continue;
            string url = link.GetString()!;
            string title = source.TryGetProperty("title", out var heading) &&
                heading.ValueKind == JsonValueKind.String ? heading.GetString() ?? "" : "";
            string text = excerpt.GetString() ?? "";
            // The API supplied these fields. Render them as inert, bounded text;
            // remove Markdown control characters and whitespace injection.
            static string Clean(string value, int max) =>
                new string(value.Where(c => !char.IsControl(c) || c == ' ')
                    .Select(c => c is '[' or ']' or '(' or ')' or '<' or '>' or '`' or '*' or '#' ? ' ' : c)
                    .Take(max).ToArray()).Trim();
            title = Clean(title, 95);
            text = Clean(text, 360);
            if (title.Length == 0) title = "Published reference";
            if (text.Length == 0) continue;
            reply.Append("**").Append(title).Append("** — [Source](")
                .Append(url).Append(")\n");
            reply.Append("Source excerpt: “").Append(text).AppendLine("”\n");
            if (++added == 4) break;
        }
        if (added == 0) return null;
        reply.Append("\nSome source claims may be proposed, advertised, or not " +
            "independently verified. These extracts alone do not establish " +
            "that a claimed outcome was demonstrated.");
        return reply.ToString();
    }

    public static NIRACognitionDecision TruthfulFailure() => new()
    {
        State = NIRACognitionState.Blocked,
        EmitReply = true,
        ReplyReady = true,
        CharacterReady = true,
        Reply = "I found public web references, but I couldn't reliably " +
                "finish checking and explaining them in this response. " +
                "I won't invent the missing details.",
        DecisionSummary = "A successful web read had no usable, answer-shaped synthesis."
    };
}
