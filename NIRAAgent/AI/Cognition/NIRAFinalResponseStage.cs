/*
 * filename: NIRAFinalResponseStage.cs
 */

using NIRAAgent.Character.Appraisal;
using NIRAAgent.Character.Interaction;
using NIRAAgent.Presentation;

namespace NIRAAgent.AI.Cognition;

// =============================================================
// NIRA FINAL RESPONSE STAGE (BLOCK 03)
//
// Block 02 produces a grounded semantic completion or a user-facing draft.
// Block 03 converts that completion into the final public NIRA response using
// the authoritative character/presentation pass. It does not own tools,
// planning, goals, memory mutation or execution authority.
// =============================================================

public sealed class NIRAFinalResponseStage
{
    private readonly NIRAResponseRealizationService _realization;
    private readonly NIRATaskCompletionReviewService _review;

    public NIRAFinalResponseStage(
        NIRAResponseRealizationService realization,
        NIRATaskCompletionReviewService review)
    {
        _realization = realization ?? throw new ArgumentNullException(nameof(realization));
        _review = review ?? throw new ArgumentNullException(nameof(review));
    }

    // The public-response stage is the sole owner of terminal natural
    // presentation routing. A completed internal-work draft is deliberately
    // not character-ready, even if an upstream review or a runtime fallback
    // accidentally marks its readiness flags true.
    internal static bool ShouldRealize(
        NIRACognitionDecision decision,
        string groundedReply,
        string? conversationContext,
        bool hasOutstandingWork,
        bool authoritativeCharacterShift,
        bool independentlyReviewed,
        bool passedThroughInternalWork,
        bool verifiedDirectFirstCall = false)
    {
        ArgumentNullException.ThrowIfNull(decision);
        if (hasOutstandingWork ||
            decision.State is not (NIRACognitionState.Complete or
                NIRACognitionState.Blocked or NIRACognitionState.NeedUser) ||
            !decision.EmitReply ||
            decision.ReplyPresentation != NIRAReplyPresentationMode.Natural ||
            string.IsNullOrWhiteSpace(groundedReply))
            return false;

        // Work continuation produces factual findings, not a public voice.
        // Do not use "independently reviewed" or model-declared readiness
        // as permission to bypass NIRA's first public character rendering.
        if (passedThroughInternalWork)
            return true;

        // Executive-verified, character-ready first-call responses are already
        // public NIRA speech. Re-running a style model solely for an opening
        // repeated in chat is more expensive than the cadence improvement;
        // entry cognition already sees the prior openings. A real character
        // shift or nonready reply must still enter Block 03.
        if (verifiedDirectFirstCall && !authoritativeCharacterShift &&
            decision.ReplyReady && decision.CharacterReady)
            return false;

        // Other first-call responses retain conservative character routing.
        return NIRACharacterDeliveryPolicy.RequiresOptimizedFidelityPass(
            decision, groundedReply, hasOutstandingWork,
            authoritativeCharacterShift, independentlyReviewed) ||
            NIRAConversationCadencePolicy.RepeatsOpening(
                groundedReply, conversationContext);
    }

    public async Task<NIRAFinalResponseStageResult> CompleteAsync(
        NIRAFinalResponseStageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string groundedReply = (request.GroundedReply ?? string.Empty).Trim();
        string groundedSpeech = (request.GroundedSpeech ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(groundedReply))
        {
            return new NIRAFinalResponseStageResult
            {
                Reply = string.Empty,
                Speech = string.Empty,
                UsedRealization = false,
                FallbackToGroundedDraft = false
            };
        }

        NIRAResponseRealizationResult realized =
            await _realization.RealizeAsync(
                new NIRAResponseRealizationRequest
                {
                    DraftReply = groundedReply,
                    DraftSpeech = groundedSpeech,
                    DecisionSummary = request.DecisionSummary,
                    EventSource = request.EventSource,
                    EventName = request.EventName,
                    TopicKey = request.TopicKey,
                    EventContent = request.EventContent,
                    TemporalContext = request.TemporalContext,
                    ConversationContext = request.ConversationContext,
                    SocialCarryoverContext = request.SocialCarryoverContext,
                    Interaction = request.Interaction,
                    AppliedSocialAppraisal = request.AppliedSocialAppraisal,
                    RequiredVerbatimFragments = request.RequiredVerbatimFragments,
                    AllowPresentationComposition = request.DisplayBlocks.Count == 0
                },
                cancellationToken);

        bool reverted = false;

        if (request.HasCapabilityResults &&
            !NIRAPublicCharacterDelivery.PreservesGroundedResult(
                groundedReply,
                realized.Reply))
        {
            reverted = true;
            realized = realized with
            {
                Reply = groundedReply,
                Speech = groundedSpeech
            };
        }
        else if (request.RequiresPresentationReview &&
                 request.HasCapabilityResults &&
                 !string.Equals(realized.Reply, groundedReply, StringComparison.Ordinal))
        {
            NIRATaskCompletionReview? review =
                await _review.ReviewAsync(
                    new NIRATaskCompletionReviewRequest(
                        request.OriginalUserObjective,
                        NIRAPresentationPolicy.ForObjectiveReview(realized.Reply,
                            realized.DisplayBlocks.Count > 0 ? realized.DisplayBlocks : request.DisplayBlocks),
                        request.ReviewEvidence,
                        request.ConversationContext,
                        request.PendingObjective,
                        request.ExternalAppId),
                    cancellationToken);

            if (review?.Verdict != "Complete")
            {
                reverted = true;
                realized = realized with
                {
                    Reply = groundedReply,
                    Speech = groundedSpeech
                };
            }
        }

        return new NIRAFinalResponseStageResult
        {
            Reply = realized.Reply,
            Speech = realized.Speech,
            DisplayBlocks = reverted ? request.DisplayBlocks :
                (request.DisplayBlocks.Count > 0 ? request.DisplayBlocks : realized.DisplayBlocks),
            UsedRealization = true,
            FallbackToGroundedDraft = reverted
        };
    }
}

public sealed record NIRAFinalResponseStageRequest
{
    public string GroundedReply { get; init; } = string.Empty;
    public string GroundedSpeech { get; init; } = string.Empty;
    public string DecisionSummary { get; init; } = string.Empty;
    public string EventSource { get; init; } = string.Empty;
    public string EventName { get; init; } = string.Empty;
    public string TopicKey { get; init; } = string.Empty;
    public string EventContent { get; init; } = string.Empty;
    public string TemporalContext { get; init; } = string.Empty;
    public string ConversationContext { get; init; } = string.Empty;
    public string SocialCarryoverContext { get; init; } = string.Empty;
    public string OriginalUserObjective { get; init; } = string.Empty;
    public string PendingObjective { get; init; } = string.Empty;
    public string ReviewEvidence { get; init; } = string.Empty;
    public string? ExternalAppId { get; init; }
    public bool HasCapabilityResults { get; init; }
    public bool RequiresPresentationReview { get; init; }
    public NIRAInteractionContext? Interaction { get; init; }
    public NIRAInteractionAppraisal? AppliedSocialAppraisal { get; init; }
    public IReadOnlyList<string> RequiredVerbatimFragments { get; init; } = Array.Empty<string>();
    public IReadOnlyList<NIRARichBlock> DisplayBlocks { get; init; } = Array.Empty<NIRARichBlock>();
}

public sealed record NIRAFinalResponseStageResult
{
    public IReadOnlyList<NIRARichBlock> DisplayBlocks { get; init; } = Array.Empty<NIRARichBlock>();
    public string Reply { get; init; } = string.Empty;
    public string Speech { get; init; } = string.Empty;
    public bool UsedRealization { get; init; }
    public bool FallbackToGroundedDraft { get; init; }
}
