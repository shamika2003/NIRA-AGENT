/*
 * filename: NIRACognitionContracts.cs
 */

using NIRAAgent.Character.State;
using NIRAAgent.Character.Appraisal;
using NIRAAgent.Capabilities;
using NIRAAgent.Branches;
using NIRAAgent.Memory.LongTerm;
using NIRAAgent.Goals;
using NIRAAgent.Voice;
using NIRAAgent.Tools;
using NIRAAgent.Artifacts;

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


    public NIRAReplyPresentationMode ReplyPresentation
    {
        get;
        init;
    } =
        NIRAReplyPresentationMode.Natural;


    public string DecisionSummary
    {
        get;
        init;
    } =
        string.Empty;


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
