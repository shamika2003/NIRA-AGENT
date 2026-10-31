using NIRAAgent.Mind;

namespace NIRAAgent.AI.Cognition;

/// <summary>
/// A narrow latency optimization for NIRA's already authored first-turn
/// interpersonal or truly self-contained knowledge response, INCLUDING
/// optional validated typed displayBlocks for self-contained explanations.
/// No keyword classifier, tool authorization or factual completion inference.
/// External work, pending tasks, user commitments and tool claims still require
/// the independent review/authoritative completion path.
/// </summary>
public static class NIRAConversationFastPath
{
    public static bool CanUseDirectReply(
        NIRACognitionDecision d, bool firstUserCycle,
        bool hasTrustedWorkEvidence, bool hasPendingOwnedTask,
        bool embeddedApplication)
    {
        return firstUserCycle && !hasTrustedWorkEvidence &&
            !hasPendingOwnedTask && !embeddedApplication &&
            (d.ConversationOnly ^ d.SelfContainedAnswer) &&
            d.ReplyReady && d.CharacterReady &&
            d.State == NIRACognitionState.Complete && d.EmitReply &&
            d.ReplyPresentation == NIRAReplyPresentationMode.Natural &&
            !string.IsNullOrWhiteSpace(d.Reply) &&
            !d.ReviewCommitment && !d.ReviewExperience &&
            string.IsNullOrWhiteSpace(d.NovelExperienceEvidence) &&
            d.ResourceIntent is null &&
            // Rich screen structure is not a reason to buy a second model call.
            // A self-contained explanation may deliver its WPF blocks from the
            // first model decision. The Executive has already normalized the
            // declarative block data through NIRAPresentationPolicy. Purely
            // interpersonal conversation remains natural plain text.
            (!d.ConversationOnly || d.DisplayBlocks.Count == 0) &&
            d.ContextRequests.Count == 0 && d.CapabilityIds.Count == 0 &&
            d.CapabilityRequests.Count == 0 &&
            d.MemorySearches.Count == 0 && d.ConversationSearches.Count == 0 &&
            d.GoalProposals.Count == 0 && d.BranchProposals.Count == 0 &&
            d.BranchWorkProposals.Count == 0 && d.ControlRequests.Count == 0 &&
            d.DynamicToolProposals.Count == 0 && d.DynamicToolInvocations.Count == 0 &&
            d.VisualPresentations.Count == 0 &&
            string.IsNullOrWhiteSpace(d.ProgressUpdate) &&
            d.RuntimeContractDiagnostics.Count == 0 &&
            d.CommitmentProposals.Count == 0;
    }
}
