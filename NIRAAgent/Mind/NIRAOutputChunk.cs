/*
 * filename: NIRAOutputChunk.cs
 */

using NIRAAgent.Artifacts;
using NIRAAgent.Voice;

namespace NIRAAgent.Mind;

public enum NIRAOutputChunkType
{
    Text,
    VisualArtifact,
    Completed,
    Cancelled
}

public sealed record NIRAOutputChunk
{
    public Guid RunId
    {
        get;
        init;
    }


    public NIRAMindEventSource Source
    {
        get;
        init;
    }


    public NIRAOutputChunkType Type
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


    public NIRAVisualArtifact? VisualArtifact
    {
        get;
        init;
    }


    public NIRAVoiceExpression VoiceExpression
    {
        get;
        init;
    } =
        NIRAVoiceExpression.Neutral;
}

