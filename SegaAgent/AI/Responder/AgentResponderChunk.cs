/*
 * filename: AgentResponderChunk.cs
 */

using SegaAgent.Character.Appraisal;

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
}