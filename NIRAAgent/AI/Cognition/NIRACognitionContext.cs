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


    // Deterministic application-owned translation of the current character
    // state into concrete delivery consequences. This is always supplied to
    // cognition so a one-call Natural reply cannot ignore persisted mood.
    public string CharacterDeliveryContext
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


    public string OwnedTaskContext { get; init; } = string.Empty;

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


    // Small newest-first continuity window that is always sent to cognition.
    // The larger ConversationContext remains opt-in for deeper retrieval.
    public string ConversationPulseContext
    {
        get;
        init;
    } =
        string.Empty;


    // Bounded cross-session social continuity from persisted significant
    // episodes. This is historical evidence, never a new instruction.
    public string SocialCarryoverContext
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


    // Executive-supplied IDs from actual capability results in THIS run.
    // Never parsed from model-authored text or raw connector output.
    public IReadOnlyList<string> ObservedCapabilityIds { get; init; } = Array.Empty<string>();

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
