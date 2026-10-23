using System.Text;
using System.Text.Json;
using NIRAAgent.Capabilities;

namespace NIRAAgent.AI.Cognition;

/// <summary>
/// Authoritative shape-only guard for a successful source lookup followed by an
/// unusable model decision. Does not infer facts, select tools, or rewrite
/// source text. Used ONLY after an observed successful web.search receipt.
/// </summary>
public static class NIRAWebResearchReplyPolicy
{
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

    // A successful read-only web receipt can support a source-only PARTIAL
    // handoff after exhausted synthesis. Optional image errors do not erase
    // source evidence. No mutation or uncertain-capability results are allowed.
    internal static string? ObservedSourceDigestFromResults(
        IReadOnlyList<NIRACapabilityResult> observedResults)
    {
        if (observedResults is null || observedResults.Count == 0 ||
            observedResults.Any(result => result.Risk != NIRACapabilityRisk.Observe ||
                result.ChangedSystemState || result.OutcomeUncertain))
            return null;
        var web = observedResults.Where(result =>
            result.CapabilityId == NIRACapabilityIds.WebSearch).ToArray();
        if (web.Length != 1 || !web[0].Succeeded) return null;
        try
        {
            using JsonDocument receipt = JsonDocument.Parse(web[0].Output);
            return ObservedSourceDigestFromPayload(receipt.RootElement);
        }
        catch (JsonException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    // Failure-only, source-attributed partial handoff. The reviewer is still
    // mandatory to certify a model-written factual answer; this never releases
    // the unchecked draft or interprets a search snippet as proven truth.
    // A real successful read is worth more to the user than a blanket apology.
    public static string? ObservedSourceHandoff(
        IReadOnlyList<NIRACapabilityResult> observedResults)
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
                JsonElement root = doc.RootElement;
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
    internal static string? ObservedSourceDigestFromPayload(JsonElement payload)
    {
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