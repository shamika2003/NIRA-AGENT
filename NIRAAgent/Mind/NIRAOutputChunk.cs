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
    // Ephemeral unverified provider text, UI only: never archive or speak.
    Preview,
    // Ephemeral user-visible status; never a saved assistant reply.
    Progress,
    VisualArtifact,
    Completed,
    Cancelled,
    // Append rather than renumber existing enum members.
    // Host-owned activity only; never speech or conversation archive content.
    Activity
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


    // Trusted host-only completion metadata. A fully audited, disposable
    // arithmetic turn contains no durable user experience to form. Never
    // infer this from model-authored text or from an unverified tool result.
    public bool SkipPostTurnExperienceReview { get; init; }

    public string Content
    {
        get;
        init;
    } =
        string.Empty;


    // Speech is independently rendered; null/empty preserves legacy Content speech.
    // Trusted runtime origin, not model-authored display content. Used to
    // associate independently arriving replies with the originating request.
    public string OriginPrompt { get; init; } = string.Empty;
    public Guid? ArchiveMessageId { get; init; }
    public string SpeechContent { get; init; } = string.Empty;
    // Spoken status is deliberately distinct from a final response.
    public bool IsProgressCorrection { get; init; }
    // Trusted runtime signal, never inferred from arbitrary model text.
    public bool IsImageGenerationProgress { get; init; }
    // Only the host maps persisted AgentActivityEvent records to these fields.
    // Tool identifiers are classification inputs, not displayed raw to the user.
    public string ActivityCode { get; init; } = string.Empty;
    public string ActivityToolId { get; init; } = string.Empty;
    public string ActivityDetailCode { get; init; } = string.Empty;
    public DateTimeOffset ActivityAtUtc { get; init; }
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
