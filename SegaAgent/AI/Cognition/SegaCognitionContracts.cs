/*
 * filename: SegaCognitionContracts.cs
 */

using SegaAgent.Character.State;
using SegaAgent.Capabilities;
using SegaAgent.Branches;
using SegaAgent.Memory.LongTerm;
using SegaAgent.Goals;
using SegaAgent.Voice;
using SegaAgent.Tools;

namespace SegaAgent.AI.Cognition;

public enum SegaCognitionState
{
    Complete,
    Continue,
    NeedUser,
    Wait,
    Blocked
}


public sealed record SegaCognitionDecision
{
    public SegaCognitionState State
    {
        get;
        init;
    } =
        SegaCognitionState.Complete;


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


    public string DecisionSummary
    {
        get;
        init;
    } =
        string.Empty;


    public IReadOnlyList<SegaMemorySearchRequest> MemorySearches
    {
        get;
        init;
    } =
        Array.Empty<SegaMemorySearchRequest>();


    public IReadOnlyList<SegaGoalProposal> GoalProposals
    {
        get;
        init;
    } =
        Array.Empty<SegaGoalProposal>();


    public IReadOnlyList<SegaBranchProposal> BranchProposals
    {
        get;
        init;
    } =
        Array.Empty<SegaBranchProposal>();


    public IReadOnlyList<SegaBranchWorkProposal> BranchWorkProposals
    {
        get;
        init;
    } =
        Array.Empty<SegaBranchWorkProposal>();


    public IReadOnlyList<SegaCapabilityRequest> CapabilityRequests
    {
        get;
        init;
    } =
        Array.Empty<SegaCapabilityRequest>();


    public IReadOnlyList<SegaDynamicToolProposal> DynamicToolProposals
    {
        get;
        init;
    } =
        Array.Empty<SegaDynamicToolProposal>();


    public IReadOnlyList<SegaDynamicToolInvocation> DynamicToolInvocations
    {
        get;
        init;
    } =
        Array.Empty<SegaDynamicToolInvocation>();


    public SegaCognitionAppraisalProposal? Appraisal
    {
        get;
        init;
    }




    public SegaVocalIntent VocalIntent
    {
        get;
        init;
    } =
        SegaVocalIntent.Default;
}


public sealed record SegaCognitionAppraisalProposal
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


    public SegaInteractionMode SituationMode
    {
        get;
        init;
    } =
        SegaInteractionMode.Casual;


    public double SituationIntensity
    {
        get;
        init;
    }
}