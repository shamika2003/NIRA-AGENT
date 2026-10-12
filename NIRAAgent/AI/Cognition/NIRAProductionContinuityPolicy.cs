using NIRAAgent.Mind;

namespace NIRAAgent.AI.Cognition;

/// <summary>
/// Production boundary invariants shared with the isolated Character Lab.
/// Never reads or writes a relationship, conversation, memory, or credential store.
/// </summary>
public static class NIRAProductionContinuityPolicy
{
    /// <summary>
    /// A vetted missing-input question is a valid terminal handoff, not a
    /// claim that NIRA has completed the user's underlying objective.
    /// The completion reviewer must agree with the actual proposed state.
    /// </summary>
    public static bool AcceptsObjectiveReview(
        NIRACognitionDecision decision, NIRATaskCompletionReview? review)
    {
        if (review is null || !decision.EmitReply ||
            string.IsNullOrWhiteSpace(decision.Reply))
            return false;
        return (decision.State == NIRACognitionState.Complete &&
                review.Verdict == "Complete") ||
               (decision.State == NIRACognitionState.NeedUser &&
                review.Verdict == "AwaitingUser");
    }

    /// <summary>
    /// A user-facing NeedUser without a visible question cannot be handed off:
    /// the user sees silence and ConversationManager cannot remember the missing input.
    /// Only repair otherwise idle user decisions, never a pending capability or work proposal.
    /// </summary>
    public static bool HasEmptyUserHandoff(NIRAMindEventSource source, NIRACognitionDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        return source == NIRAMindEventSource.User &&
            decision.State == NIRACognitionState.NeedUser &&
            (!decision.EmitReply || string.IsNullOrWhiteSpace(decision.Reply)) &&
            decision.ControlRequests.Count == 0 &&
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
            decision.RuntimeContractDiagnostics.Count == 0;
    }

    /// <summary>
    /// Trusted evidence for exactly one model correction. No user data or model
    /// speculation is converted into a requirement or an authorization.
    /// </summary>
    public const string MissingHandoffCorrection =
        "RUNTIME USER-HANDOFF CORRECTION: The previous model decision returned " +
        "NeedUser without a visible clarification question. That cannot be " +
        "delivered to the user or saved for continuation. Reconsider the SAME " +
        "original request and available evidence: if it can be answered now, " +
        "use Complete with emitReply=true and a helpful final reply. Otherwise " +
        "use NeedUser with emitReply=true and ONE precise question about a " +
        "genuinely missing user-provided detail. Do not fabricate observations, " +
        "request secrets in normal chat, invent previous work, or issue a tool call " +
        "solely to satisfy this correction.";

    /// <summary>
    /// Fail closed after the bounded correction attempt. The handoff must be
    /// visible and noncommittal rather than an empty model reply or fake observation.
    /// </summary>
    public static NIRACognitionDecision MakeVisibleFallback(NIRACognitionDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        if (decision.State != NIRACognitionState.NeedUser)
            throw new ArgumentException("Fallback is restricted to NeedUser decisions.", nameof(decision));

        return decision with
        {
            EmitReply = true,
            Reply = "I can help, but I need a little more detail to continue. What should I focus on?",
            Speech = string.Empty,
            ReplyReady = true,
            CharacterReady = true,
            ReviewExperience = false,
            ReviewCommitment = false,
            DecisionSummary = "The user-facing clarification was missing after a bounded model retry; a neutral, non-factual question was supplied."
        };
    }

    /// <summary>
    /// Metadata-only production telemetry. Never logs conversation text, memory
    /// content, secret material, or relationship values.
    /// </summary>
    public static string DescribeContext(NIRACognitionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return $"Cycle={context.Cycle}; Character={(context.CharacterContext.Length > 0)}; " +
            $"Delivery={(context.CharacterDeliveryContext.Length > 0)}; " +
            $"RecentConversationChars={context.ConversationPulseContext.Length}; " +
            $"HistoricalSocialChars={context.SocialCarryoverContext.Length}; " +
            $"MemoryMode={context.MemoryContextMode}; MemoryChars={context.LongTermMemoryContext.Length}; " +
            $"PendingClarification={(context.TaskContinuityContext.Length > 0)}";
    }
}