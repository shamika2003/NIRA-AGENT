/*
 * filename: SegaCognitionContext.cs
 */

using SegaAgent.Memory.LongTerm;
using SegaAgent.Capabilities;
using SegaAgent.Branches;
using SegaAgent.Mind;
using SegaAgent.Tools;

namespace SegaAgent.AI.Cognition;

public sealed record SegaCognitionContext
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


    public SegaMindEvent Event
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


    public string PcContext
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


    public SegaMemoryContextMode MemoryContextMode
    {
        get;
        init;
    } =
        SegaMemoryContextMode.Full;


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