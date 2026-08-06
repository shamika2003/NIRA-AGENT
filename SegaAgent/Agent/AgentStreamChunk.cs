/*
 * filename: AgentStreamChunk.cs
 */

namespace SegaAgent.Agent;

public enum AgentStreamChunkType
{
    Text,
    Completed
}


public sealed class AgentStreamChunk
{
    public AgentStreamChunkType Type { get; init; }

    public string Content { get; init; } = "";
}