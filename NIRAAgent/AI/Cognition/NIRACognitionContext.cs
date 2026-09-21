/*
 * filename: NIRACognitionContext.cs
 */

using NIRAAgent.Memory.LongTerm;
using NIRAAgent.Capabilities;
using NIRAAgent.Branches;
using NIRAAgent.Mind;
using NIRAAgent.Tools;

namespace NIRAAgent.AI.Cognition;

public sealed record NIRACognitionContext
{
    public Guid RunId
    {
        get;
        init;
    }


    public int Cycle
    {
        get;
        init;
    }


    public NIRAMindEvent Event
    {
        get;
        init;
    } =
        null!;


    public string CharacterContext
    {
        get;
        init;
    } =
        string.Empty;


    public string SelfModelContext
    {
        get;
        init;
    } =
        string.Empty;


    public string GoalContext
    {
        get;
        init;
    } =
        string.Empty;


    public string BranchContext
    {
        get;
        init;
    } =
        string.Empty;


    public string BranchWorkContext
    {
        get;
        init;
    } =
        string.Empty;


    public string CapabilityContext
    {
        get;
        init;
    } =
        string.Empty;


    public string DynamicToolContext
    {
        get;
        init;
    } =
        string.Empty;


    public string ConversationContext
    {
        get;
        init;
    } =
        string.Empty;

    public string TaskContinuityContext
    {
        get;
        init;
    } =
        string.Empty;


    public string PcContext
    {
        get;
        init;
    } =
        string.Empty;


    public string VisualArtifactContext
    {
        get;
        init;
    } =
        string.Empty;


    public string TemporalContext
    {
        get;
        init;
    } =
        string.Empty;


    public NIRAMemoryContextMode MemoryContextMode
    {
        get;
        init;
    } =
        NIRAMemoryContextMode.Full;


    public int ActiveLongTermMemoryCount
    {
        get;
        init;
    }


    public int LongTermMemoryCharacterBudget
    {
        get;
        init;
    }


    public string LongTermMemoryContext
    {
        get;
        init;
    } =
        string.Empty;


    public string ExecutiveEvidence
    {
        get;
        init;
    } =
        string.Empty;


    public string CapabilityEvidence
    {
        get;
        init;
    } =
        string.Empty;


    public string DynamicToolEvidence
    {
        get;
        init;
    } =
        string.Empty;


    public string MemorySearchEvidence
    {
        get;
        init;
    } =
        string.Empty;


    public bool IsFirstCycle =>
        Cycle ==
        1;
}
