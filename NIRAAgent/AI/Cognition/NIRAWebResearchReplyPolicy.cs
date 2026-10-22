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
