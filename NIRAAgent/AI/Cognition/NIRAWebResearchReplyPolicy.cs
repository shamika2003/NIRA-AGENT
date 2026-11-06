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
        if (observedResults is null || observedResults.Any(result =>
                result.Risk != NIRACapabilityRisk.Observe || result.ChangedSystemState ||
                result.OutcomeUncertain)) return null;
        // Link lists are appropriate when explicitly requested; ordinary
        // research answers must instead be written and independently checked.
        // A source-only emergency handoff is separate from that normal path.
        foreach (var result in observedResults.Reverse())
        {
            if (result.CapabilityId != NIRACapabilityIds.WebSearch || !result.Succeeded)
                continue;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(result.Output);
                JsonElement root = NIRAWebEvidenceIdentityPolicy.RelevantSourcePayload(
                    doc.RootElement, userObjective);
                if (!TryPublicSources(root, out var publicSources)) continue;
                if (!NIRAResearchPresentationPolicy.IsLinkDiscoveryRequest(userObjective) &&
                    !string.IsNullOrWhiteSpace(userObjective))
                    return "I found published sources, but my independent answer check didn't complete, so I won't present that as verified yet.";
                StringBuilder reply = new(
                    "I found published sources, but my independent answer check didn't complete, so treat these as starting points rather than verified conclusions:\n");
                foreach (var source in publicSources.Take(3))
                    reply.Append("- [").Append(source.Title).Append("](")
                        .Append(source.Url).AppendLine(")");
                return reply.ToString().TrimEnd();
            }
            catch (JsonException) { }
            catch (InvalidOperationException) { }
        }
        return null;
    }

    // Validate the complete receipt before using any source; only safe public
    // URLs with nonempty attributed extracts can cross the response boundary.
    // Source titles are inert text, never Markdown/HTML markup from a publisher.
    private static bool TryPublicSources(JsonElement root,
        out List<(string Url, string Title, string Extract)> publicSources)
    {
        publicSources = new();
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("evidenceType", out var kind) ||
            kind.ValueKind != JsonValueKind.String ||
            kind.GetString() != "UntrustedPublicWebSearchExtracts" ||
            !root.TryGetProperty("completed", out var complete) ||
            complete.ValueKind != JsonValueKind.True ||
            !root.TryGetProperty("sources", out var sources) ||
            sources.ValueKind != JsonValueKind.Array ||
            !root.TryGetProperty("sourceCount", out var count) ||
            !count.TryGetInt32(out int number) ||
            number is < 1 or > 8 || number != sources.GetArrayLength()) return false;
        foreach (var source in sources.EnumerateArray())
        {
            if (source.ValueKind != JsonValueKind.Object ||
                !source.TryGetProperty("url", out var url) ||
                url.ValueKind != JsonValueKind.String ||
                !NIRAWebSearchParsing.SafeSourceUrl(url.GetString() ?? "") ||
                !source.TryGetProperty("extract", out var extract) ||
                extract.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(extract.GetString())) continue;
            string title = source.TryGetProperty("title", out var name) &&
                name.ValueKind == JsonValueKind.String ? name.GetString() ?? "" : "";
            string Clean(string value, int max) =>
                new string(value.Where(c => !char.IsControl(c) || c == ' ')
                    .Select(c => c is '[' or ']' or '(' or ')' or '<' or '>' or '`' or '*' or '#'
                        ? ' ' : c).Take(max).ToArray()).Trim();
            title = Clean(title, 95);
            string text = Clean(extract.GetString() ?? "", 360);
            if (text.Length == 0) continue;
            publicSources.Add((url.GetString()!,
                title.Length == 0 ? "Published reference" : title, text));
        }
        return publicSources.Count > 0;
    }

    // Select the appropriate reviewer-failure partial. Ordinary factual
    // questions get attributed excerpts rather than a bare apology or a wall
    // of links. A user specifically asking for websites gets the links.
    internal static string? FailedReviewSourceHandoff(
        IReadOnlyList<NIRACapabilityResult> results, string? userObjective) =>
        NIRAResearchPresentationPolicy.IsLinkDiscoveryRequest(userObjective)
            ? ObservedSourceHandoff(results, userObjective)
            : ObservedSourceDigestFromResults(results, userObjective);

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

        string? links = FailedReviewSourceHandoff(observedResults, userObjective);
        if (string.IsNullOrWhiteSpace(links) &&
            NIRAWebEvidenceIdentityPolicy.FirstRequestedUrl(userObjective) is not null)
        {
            // A source provider can identify the precise URL without giving
            // NIRA enough evidence to explain its content. Distinguish that
            // situation from a search that only found unrelated pages.
            bool foundTarget = HasObservedRequestedSource(observedResults, userObjective);
            return new NIRACognitionDecision {
                State = NIRACognitionState.Blocked,
                EmitReply = true,
                Reply = foundTarget
                    ? "I located the exact page you linked, but I couldn't " +
                      "finish checking and explaining its contents reliably. " +
                      "A raw search excerpt isn't a substitute for an answer."
                    : "I searched for the link you provided, but the returned sources " +
                      "did not reliably identify that exact resource. I can't verify " +
                      "its details from those results, so I won't substitute a " +
                      "similarly named page or make up an answer.",
                Speech = string.Empty,
                ReplyReady = true,
                CharacterReady = true,
                DisplayBlocks = Array.Empty<NIRARichBlock>(),
                DecisionSummary = foundTarget
                    ? "Exact linked source observed; independent explanatory review incomplete."
                    : "User-linked resource was not identified in the observed search evidence."
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

    // Check the originally observed trusted receipt before narrowing it.
    // RelevantSourcePayload intentionally reconstructs a filtered JSON shape;
    // validating the original first prevents an incomplete/unsafe payload
    // from becoming "completed" merely because its URL matches the user text.
    private static bool HasObservedRequestedSource(
        IReadOnlyList<NIRACapabilityResult> results, string? userObjective)
    {
        if (NIRAWebEvidenceIdentityPolicy.FirstRequestedUrl(userObjective) is null)
            return false;
        foreach (var result in results)
        {
            if (result.CapabilityId != NIRACapabilityIds.WebSearch ||
                !result.Succeeded || result.OutcomeUncertain ||
                result.ChangedSystemState || result.Risk != NIRACapabilityRisk.Observe)
                continue;
            try
            {
                using JsonDocument receipt = JsonDocument.Parse(result.Output);
                if (!TryPublicSources(receipt.RootElement, out _)) continue;
                JsonElement matched = NIRAWebEvidenceIdentityPolicy.RelevantSourcePayload(
                    receipt.RootElement, userObjective);
                if (TryPublicSources(matched, out _)) return true;
            }
            catch (JsonException) { }
            catch (InvalidOperationException) { }
        }
        return false;
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
    internal static string? ObservedSourceDigest(
        string capabilityEvidence, string? userObjective = null)
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
            return ObservedSourceDigestFromPayload(doc.RootElement, userObjective);
        }
        catch (JsonException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    // For failure handling after a trusted capability operation; the payload is
    // validated here as well, so no arbitrary text or forged URLs are promoted.
    internal static string? ObservedSourceDigestFromPayload(JsonElement payload, string? userObjective = null)
    {
        // A user-supplied URL is an exact research target. Even a correctly
        // matched search excerpt is not an independent explanation of that
        // page. Preserve the receipt for model synthesis/review, but NEVER
        // promote provider snippets to a completed public response after a
        // failed review. General topic research still has the explicitly
        // provisional, source-attributed partial recovery below.
        if (NIRAWebEvidenceIdentityPolicy.FirstRequestedUrl(userObjective) is not null)
            return null;
        payload = NIRAWebEvidenceIdentityPolicy.RelevantSourcePayload(payload, userObjective);
        if (!TryPublicSources(payload, out var sources)) return null;

        // Last-resort PARTIAL: independently observed extracts are quoted as
        // publisher claims, not transformed into facts and not certified by a
        // failed review. This must never be used as a normal finished answer.
        StringBuilder reply = new(
            "My independent answer check didn't complete. Here are attributable excerpts from the published material, not independently verified conclusions:\n\n");
        foreach (var source in sources.Take(4))
        {
            reply.Append("**").Append(source.Title).Append("** — [Source](")
                .Append(source.Url).Append(")\n");
            reply.Append("Source excerpt: “").Append(source.Extract).AppendLine("”\n");
        }
        reply.Append("These are attributed excerpts, not independently verified outcomes. " +
            "A publisher's description or proposal alone does not establish a demonstrated result.");
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
