/*
 * filename: NIRABodyCommand.cs
 */

namespace NIRAAgent.Embodiment.Body;


// =============================================================
// COMMAND TYPE
// =============================================================

public enum NIRABodyCommandType
{
    MoveToScreenPosition,

    Show,

    Hide
}


// =============================================================
// COMMAND SOURCE
//
// Future executive goals, tools and autonomous body policies can
// share the same command channel without directly knowing WPF.
// =============================================================

public enum NIRABodyCommandSource
{
    WorldPolicy,

    Agent,

    Tool,

    System
}


// =============================================================
// BODY COMMAND
//
// Screen coordinates are physical desktop pixels.
// WPF-specific execution stays in the UI layer.
// =============================================================

public sealed record NIRABodyCommand
{
    public Guid Id
    {
        get;
        init;
    } =
        Guid.NewGuid();


    public long Sequence
    {
        get;
        init;
    }


    public DateTimeOffset IssuedAt
    {
        get;
        init;
    }


    public NIRABodyCommandType Type
    {
        get;
        init;
    }


    public NIRABodyCommandSource Source
    {
        get;
        init;
    }


    // =========================================================
    // MOVE TARGET
    // =========================================================

    public int ScreenLeft
    {
        get;
        init;
    }


    public int ScreenTop
    {
        get;
        init;
    }


    // =========================================================
    // MOVE DURATION
    // =========================================================

    public TimeSpan Duration
    {
        get;
        init;
    } =
        TimeSpan.FromMilliseconds(
            350);


    // =========================================================
    // FACTORIES
    // =========================================================

    public static NIRABodyCommand MoveTo(
        int screenLeft,
        int screenTop,
        TimeSpan? duration = null,
        NIRABodyCommandSource source =
            NIRABodyCommandSource.System)
    {
        return new NIRABodyCommand
        {
            Type =
                NIRABodyCommandType
                    .MoveToScreenPosition,

            ScreenLeft =
                screenLeft,

            ScreenTop =
                screenTop,

            Duration =
                duration
                ?? TimeSpan.FromMilliseconds(
                    350),

            Source =
                source
        };
    }


    public static NIRABodyCommand Show(
        NIRABodyCommandSource source =
            NIRABodyCommandSource.System)
    {
        return new NIRABodyCommand
        {
            Type =
                NIRABodyCommandType.Show,

            Source =
                source
        };
    }


    public static NIRABodyCommand Hide(
        NIRABodyCommandSource source =
            NIRABodyCommandSource.System)
    {
        return new NIRABodyCommand
        {
            Type =
                NIRABodyCommandType.Hide,

            Source =
                source
        };
    }
}

