/*
 * filename: SegaOutputChunk.cs
 */

using SegaAgent.Voice;

namespace SegaAgent.Mind;

public enum SegaOutputChunkType
{
    Text,
    Completed,
    Cancelled
}

public sealed record SegaOutputChunk
{
    public Guid RunId
    {
        get;
        init;
    }


    public SegaMindEventSource Source
    {
        get;
        init;
    }


    public SegaOutputChunkType Type
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
