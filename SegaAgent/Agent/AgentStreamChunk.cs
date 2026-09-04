/*
 * filename: AgentStreamChunk.cs
 */

using SegaAgent.Voice;

namespace SegaAgent.Agent;

public enum AgentStreamChunkType
{
    Text,

    Completed,

    Cancelled
}


public sealed class AgentStreamChunk
{
    public AgentStreamChunkType Type
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


    public SegaVoiceExpression VoiceExpression
    {
        get;
        init;
    } =
        SegaVoiceExpression.Neutral;
}