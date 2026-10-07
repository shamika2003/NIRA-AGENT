/*
 * filename: NIRACognitionContracts.cs
 */

using NIRAAgent.Character.State;
using NIRAAgent.Conversation;
using NIRAAgent.Character.Appraisal;
using NIRAAgent.Capabilities;
using NIRAAgent.Branches;
using NIRAAgent.Memory.LongTerm;
using NIRAAgent.Goals;
using NIRAAgent.Voice;
using NIRAAgent.Tools;
using NIRAAgent.Artifacts;
using NIRAAgent.Presentation;

namespace NIRAAgent.AI.Cognition;

public enum NIRACognitionState
{
    Complete,
    Continue,
    NeedUser,
    Wait,
    Blocked
}


public enum NIRAReplyPresentationMode
{
    Natural,
    PreserveExact
}


public enum NIRAControlOperation
{
    CancelAllBranches,
    CancelOneBranch,
    CancelAllCommitments,
    CancelAllBranchesAndCommitments
}

// A model may classify explicit user intent, but cannot select a stale inventory
// or authorize mutations. Executive verifies quote, ownership and current state.
public sealed record NIRAControlRequest
{
    public NIRAControlOperation Operation { get; init; }
    public string EvidenceQuote { get; init; } = string.Empty;
    public string? BranchId { get; init; }
}

public sealed record NIRACognitionDecision
{
    public NIRACognitionState State
    {
        get;
        init;
    } =
        NIRACognitionState.Complete;


    public bool EmitReply
    {
        get;
        init;
    }


    public string Reply
    {
        get;
        init;
    } =
        string.Empty;


    // Spoken output differs from on-screen prose; empty falls back to Reply.
    public string Speech { get; init; } = string.Empty;
    public IReadOnlyList<NIRARichBlock> DisplayBlocks { get; init; } = Array.Empty<NIRARichBlock>();

    public NIRAReplyPresentationMode ReplyPresentation
    {
        get;
        init;
    } =
        NIRAReplyPresentationMode.Natural;


    // Optional public progress. Not internal reasoning; never proof of an action.
    public string ProgressUpdate { get; init; } = string.Empty;
    public string ProgressSpeech { get; init; } = string.Empty;
    public bool ProgressCorrection { get; init; }

    public string DecisionSummary
    {
        get;
        init;
    } =
        string.Empty;


    public IReadOnlyList<NIRAControlRequest> ControlRequests { get; init; } = Array.Empty<NIRAControlRequest>();

    // On-demand context is a model proposal; no user-text keyword matching.
    // The Executive validates sections and caps expansion per run.
    public IReadOnlyList<string> ContextRequests { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> CapabilityIds { get; init; } = Array.Empty<string>();
    // Cognition has completed the grounded semantic content for this turn.
    // This says nothing about whether the wording is already ready for direct
    // interpersonal delivery as NIRA.
    public bool ReplyReady { get; init; }

    // Natural reply/speech were written as final NIRA wording from the supplied
    // authoritative character kernel + live character pulse, rather than as a
    // deliberately neutral semantic draft. This is only a model-declared
    // presentation readiness signal; the Executive still owns the routing
    // decision and may require ResponseRealization when authoritative character
    // state materially changes after the appraisal is committed.
    //
    // PreserveExact does not use this flag because literal/verbatim output must
    // bypass personality rewriting altogether.
    public bool CharacterReady { get; init; }
    // True only when the fresh user event contains source-grounded novelty
    // worth the unified experience-formation pass (for example a durable
    // user fact/preference or meaningful outcome). Future obligations are
    // deliberately separate and use ReviewCommitment.
    public bool ReviewExperience { get; init; } = false;
    // Exact source excerpt for optional user-originated memory/self-preference
    // formation. The Executive checks it against the real user message before
    // spending another model call.
    public string NovelExperienceEvidence { get; init; } = string.Empty;

    // Separate from ordinary memory formation. Set only when the terminal reply
    // actually accepts/changes a future unresolved obligation (for example a
    // reminder or reschedule) and the authoritative commitment layer must run
    // before NIRA tells the user that obligation is stored/scheduled.
    public bool ReviewCommitment { get; init; }

    public IReadOnlyList<NIRAConversationSearchRequest> ConversationSearches { get; init; }
        = Array.Empty<NIRAConversationSearchRequest>();

    public IReadOnlyList<NIRAMemorySearchRequest> MemorySearches
    {
        get;
        init;
    } =
        Array.Empty<NIRAMemorySearchRequest>();


    public IReadOnlyList<NIRAGoalProposal> GoalProposals
    {
        get;
        init;
    } =
        Array.Empty<NIRAGoalProposal>();


    public IReadOnlyList<NIRABranchProposal> BranchProposals
    {
        get;
        init;
    } =
        Array.Empty<NIRABranchProposal>();


    public IReadOnlyList<NIRABranchWorkProposal> BranchWorkProposals
    {
        get;
        init;
    } =
        Array.Empty<NIRABranchWorkProposal>();


    public IReadOnlyList<NIRACapabilityRequest> CapabilityRequests
    {
        get;
        init;
    } =
        Array.Empty<NIRACapabilityRequest>();


    public IReadOnlyList<NIRADynamicToolProposal> DynamicToolProposals
    {
        get;
        init;
    } =
        Array.Empty<NIRADynamicToolProposal>();


    public IReadOnlyList<NIRADynamicToolInvocation> DynamicToolInvocations
    {
        get;
        init;
    } =
        Array.Empty<NIRADynamicToolInvocation>();


    public IReadOnlyList<NIRAVisualArtifactPresentationRequest> VisualPresentations
    {
        get;
        init;
    } =
        Array.Empty<NIRAVisualArtifactPresentationRequest>();


    // Exact contiguous excerpt from the CURRENT user event used to ground the
    // social appraisal. The Executive verifies this against the authoritative
    // event before any character mutation is accepted.
    public string AppraisalEvidenceQuote
    {
        get;
        init;
    } =
        string.Empty;


    public NIRACognitionAppraisalProposal? Appraisal
    {
        get;
        init;
    }


    public NIRACharacterExperienceAppraisal? ExperienceAppraisal
    {
        get;
        init;
    }


    public NIRAVocalIntent VocalIntent
    {
        get;
        init;
    } =
        NIRAVocalIntent.Default;
}


public sealed record NIRACognitionAppraisalProposal
{
    public double Respect
    {
        get;
        init;
    }


    public double Warmth
    {
        get;
        init;
    }


    public double Trust
    {
        get;
        init;
    }


    public double Appreciation
    {
        get;
        init;
    }


    public double Affection
    {
        get;
        init;
    }


    public double Playfulness
    {
        get;
        init;
    }


    public double Hostility
    {
        get;
        init;
    }


    public double Dismissal
    {
        get;
        init;
    }


    public double Repair
    {
        get;
        init;
    }


    public double Concern
    {
        get;
        init;
    }


    public double Engagement
    {
        get;
        init;
    }


    public double Pressure
    {
        get;
        init;
    }


    public double Confidence
    {
        get;
        init;
    }


    public double Ambiguity
    {
        get;
        init;
    }


    public NIRAInteractionMode SituationMode
    {
        get;
        init;
    } =
        NIRAInteractionMode.Casual;


    public double SituationIntensity
    {
        get;
        init;
    }
}
