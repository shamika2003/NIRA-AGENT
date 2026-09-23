/*
 * filename: NIRAOutputChunk.cs
 */

using NIRAAgent.Artifacts;
using NIRAAgent.Presentation;
using NIRAAgent.Voice;

namespace NIRAAgent.Mind;

public enum NIRAOutputChunkType
{
    Text,
    // Ephemeral user-visible status; never a saved assistant reply.
    Progress,
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


    // Speech is independently rendered; null/empty preserves legacy Content speech.
    public Guid? ArchiveMessageId { get; init; }
    public string SpeechContent { get; init; } = string.Empty;
    // Spoken status is deliberately distinct from a final response.
    public bool IsProgressCorrection { get; init; }
    public IReadOnlyList<NIRARichBlock> DisplayBlocks { get; init; } = Array.Empty<NIRARichBlock>();

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




