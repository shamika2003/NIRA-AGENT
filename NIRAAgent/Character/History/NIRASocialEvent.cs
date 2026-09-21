/*
 * filename: NIRASocialEvent.cs
 */

namespace NIRAAgent.Character.History;


// =============================================================
// EVENT SOURCE
//
// Who or what caused the event.
// =============================================================

public enum NIRASocialEventSource
{
    User,

    NIRA,

    Environment,

    System
}


// =============================================================
// EVENT KIND
//
// Broad event category.
//
// Specific events such as:
//
// ForegroundApplicationChanged
// UserIdle
// CompanionCheck
//
// are stored separately in EventName.
//
// This prevents this enum from becoming enormous.
// =============================================================

public enum NIRASocialEventKind
{
    UserMessage,

    NIRAResponse,

    EnvironmentEvent,

    ProactiveEvent,

    ActionResult,

    Interruption,

    SystemEvent
}


// =============================================================
// SOCIAL EVENT
// =============================================================

public sealed record NIRASocialEvent
{
    // =========================================================
    // IDENTITY
    // =========================================================

    public Guid Id
    {
        get;
        init;
    }


    public long Sequence
    {
        get;
        init;
    }


    public DateTimeOffset Timestamp
    {
        get;
        init;
    }


    // =========================================================
    // SOURCE
    // =========================================================

    public NIRASocialEventSource Source
    {
        get;
        init;
    }


    public NIRASocialEventKind Kind
    {
        get;
        init;
    }


    // =========================================================
    // EVENT NAME
    //
    // Examples:
    //
    // UserMessage
    // NIRAResponse
    // ForegroundApplicationChanged
    // ForegroundWindowChanged
    // UserIdle
    // CompanionCheck
    // ToolCompleted
    // =========================================================

    public string EventName
    {
        get;
        init;
    } = string.Empty;


    // =========================================================
    // TOPIC KEY
    //
    // Stable semantic grouping.
    //
    // Examples later:
    //
    // pc:foreground:chrome
    // pc:idle
    // work:NIRA-agent
    // conversation:general
    //
    // Social history itself does NOT decide these keys.
    // =========================================================

    public string TopicKey
    {
        get;
        init;
    } = string.Empty;


    // =========================================================
    // CONTENT
    //
    // Human-readable event content.
    //
    // This may contain:
    //
    // user message
    // NIRA response
    // environmental description
    // action result
    //
    // It is not necessarily sent directly to the model.
    // =========================================================

    public string Content
    {
        get;
        init;
    } = string.Empty;


    // =========================================================
    // METADATA
    //
    // Structured lightweight facts for future evaluators.
    //
    // Example:
    //
    // process = chrome
    // title = YouTube
    // previousProcess = Code
    //
    // Do not put emotional interpretation here.
    // =========================================================

    public IReadOnlyDictionary<
        string,
        string>
        Metadata
    {
        get;
        init;
    } =
        new Dictionary<
            string,
            string>();
}
