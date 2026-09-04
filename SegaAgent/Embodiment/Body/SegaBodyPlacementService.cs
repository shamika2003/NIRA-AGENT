/*
 * filename: SegaBodyPlacementService.cs
 */

using SegaAgent.PC.Awareness;

namespace SegaAgent.Embodiment.Body;

public sealed class SegaBodyPlacementService
{
    private const int ScreenMargin =
        20;


    private const int DefaultOffset =
        30;


    private readonly SegaBodyPlacementStore
        _store;


    private readonly PcAwarenessService
        _awareness;


    public SegaBodyPlacementService(
        SegaBodyPlacementStore store,
        PcAwarenessService awareness)
    {
        _store =
            store
            ?? throw new ArgumentNullException(
                nameof(store));


        _awareness =
            awareness
            ?? throw new ArgumentNullException(
                nameof(awareness));
    }


    // =========================================================
    // STARTUP
    // =========================================================

    public PcRectangle ResolveStartupBounds(
        int width,
        int height)
    {
        width =
            Math.Max(
                1,
                width);


        height =
            Math.Max(
                1,
                height);


        PcRectangle? stored =
            _store.LoadPreferred();


        if (stored.HasValue)
        {
            PcRectangle candidate =
                new(
                    stored.Value.Left,
                    stored.Value.Top,
                    stored.Value.Left +
                        width,
                    stored.Value.Top +
                        height);


            return ClampToAvailableDisplay(
                candidate);
        }


        PcDisplayState primary =
            _awareness
                .ReadDisplayForWindow(
                    IntPtr.Zero);


        PcRectangle workArea =
            primary.WorkArea;


        if (workArea.IsEmpty)
        {
            return new PcRectangle(
                30,
                30,
                30 +
                    width,
                30 +
                    height);
        }


        PcRectangle initial =
            new(
                workArea.Right -
                    width -
                    DefaultOffset,

                workArea.Bottom -
                    height -
                    DefaultOffset,

                workArea.Right -
                    DefaultOffset,

                workArea.Bottom -
                    DefaultOffset);


        PcRectangle safe =
            ClampToWorkArea(
                initial,
                workArea);


        _store.SavePreferred(
            safe);


        return safe;
    }


    // =========================================================
    // CLAMP
    //
    // If a saved monitor disappeared, MonitorFromRect(nearest)
    // resolves the candidate onto a surviving display.
    // =========================================================

    public PcRectangle ClampToAvailableDisplay(
        PcRectangle requested)
    {
        if (requested.IsEmpty)
        {
            return requested;
        }


        PcDisplayState display =
            _awareness
                .ReadDisplayForRectangle(
                    requested);


        if (display.WorkArea.IsEmpty)
        {
            display =
                _awareness
                    .ReadDisplayForWindow(
                        IntPtr.Zero);
        }


        if (display.WorkArea.IsEmpty)
        {
            return requested;
        }


        return ClampToWorkArea(
            requested,
            display.WorkArea);
    }


    // =========================================================
    // SAVE USER PREFERENCE
    // =========================================================

    public void SavePreferred(
        PcRectangle bounds)
    {
        if (bounds.IsEmpty)
        {
            return;
        }


        PcRectangle safe =
            ClampToAvailableDisplay(
                bounds);


        _store.SavePreferred(
            safe);
    }


    // =========================================================
    // WORK AREA
    // =========================================================

    private static PcRectangle ClampToWorkArea(
        PcRectangle requested,
        PcRectangle workArea)
    {
        int width =
            requested.Width;


        int height =
            requested.Height;


        int minimumLeft =
            workArea.Left +
            ScreenMargin;


        int maximumLeft =
            workArea.Right -
            width -
            ScreenMargin;


        int minimumTop =
            workArea.Top +
            ScreenMargin;


        int maximumTop =
            workArea.Bottom -
            height -
            ScreenMargin;


        int left =
            maximumLeft >=
                minimumLeft
                ? Math.Clamp(
                    requested.Left,
                    minimumLeft,
                    maximumLeft)
                : workArea.Left;


        int top =
            maximumTop >=
                minimumTop
                ? Math.Clamp(
                    requested.Top,
                    minimumTop,
                    maximumTop)
                : workArea.Top;


        return new PcRectangle(
            left,
            top,
            left +
                width,
            top +
                height);
    }
}
