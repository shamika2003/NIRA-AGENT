/*
 * filename: AgentResponderChunk.cs
 */

using SegaAgent.Character.Appraisal;
using SegaAgent.Memory.LongTerm;
using SegaAgent.Voice;

namespace SegaAgent.AI.Responder;

public enum AgentResponderChunkType
{
    Appraisal,

    Text
}


public sealed record AgentResponderChunk
{
    public AgentResponderChunkType Type
    {
        get;
        init;
    }


    public string Content
    {
        get;
        init;
    } =
        string.Empty;


    public SegaInteractionAppraisal?
        Appraisal
    {
        get;
        init;
    }


    public IReadOnlyList<SegaMemoryCandidate>
        MemoryCandidates
    {
        get;
        init;
    } =
        Array.Empty<SegaMemoryCandidate>();


    public SegaVocalIntent VocalIntent
    {
        get;
        init;
    } =
        SegaVocalIntent.Default;
}