/*
 * filename: SegaBodyCommand.cs
 */

namespace SegaAgent.Embodiment.Body;


// =============================================================
// COMMAND TYPE
// =============================================================

public enum SegaBodyCommandType
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

public enum SegaBodyCommandSource
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

public sealed record SegaBodyCommand
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


    public SegaBodyCommandType Type
    {
        get;
        init;
    }


    public SegaBodyCommandSource Source
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

    public static SegaBodyCommand MoveTo(
        int screenLeft,
        int screenTop,
        TimeSpan? duration = null,
        SegaBodyCommandSource source =
            SegaBodyCommandSource.System)
    {
        return new SegaBodyCommand
        {
            Type =
                SegaBodyCommandType
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


    public static SegaBodyCommand Show(
        SegaBodyCommandSource source =
            SegaBodyCommandSource.System)
    {
        return new SegaBodyCommand
        {
            Type =
                SegaBodyCommandType.Show,

            Source =
                source
        };
    }


    public static SegaBodyCommand Hide(
        SegaBodyCommandSource source =
            SegaBodyCommandSource.System)
    {
        return new SegaBodyCommand
        {
            Type =
                SegaBodyCommandType.Hide,

            Source =
                source
        };
    }
}
