/*
 * filename: PcWorldStateService.cs
 */

using System.Diagnostics;

using Microsoft.Extensions.Hosting;

namespace SegaAgent.PC.Awareness;

public sealed class PcWorldStateService
    : BackgroundService
{
    // =========================================================
    // CONFIGURATION
    // =========================================================

    private static readonly TimeSpan
        RefreshInterval =
            TimeSpan.FromMilliseconds(
                500);


    // =========================================================
    // DEPENDENCIES
    // =========================================================

    private readonly PcAwarenessService
        _awareness;


    private readonly SegaPresenceService
        _segaPresence;


    // =========================================================
    // CURRENT SNAPSHOT
    // =========================================================

    private PcWorldState
        _current;


    private long
        _version;


    // =========================================================
    // EVENTS
    // =========================================================

    public event Action<PcWorldState>?
        SnapshotUpdated;


    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public PcWorldStateService(
        PcAwarenessService awareness,
        SegaPresenceService segaPresence)
    {
        _awareness =
            awareness
            ?? throw new ArgumentNullException(
                nameof(awareness));


        _segaPresence =
            segaPresence
            ?? throw new ArgumentNullException(
                nameof(segaPresence));


        _current =
            BuildSnapshot(
                _awareness.Read());


        _version =
            1;
    }


    // =========================================================
    // CURRENT
    // =========================================================

    public PcWorldState Current =>
        Volatile.Read(
            ref _current);


    // =========================================================
    // VERSION
    // =========================================================

    public long Version =>
        Interlocked.Read(
            ref _version);


    // =========================================================
    // EXECUTE
    // =========================================================

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        Debug.WriteLine(
            "[PcWorld] SERVICE STARTED");


        using PeriodicTimer timer =
            new(
                RefreshInterval);


        try
        {
            while (
                await timer.WaitForNextTickAsync(
                    stoppingToken))
            {
                RefreshSnapshot();
            }
        }
        catch (OperationCanceledException)
            when (
                stoppingToken
                    .IsCancellationRequested)
        {
        }


        Debug.WriteLine(
            "[PcWorld] SERVICE STOPPED");
    }


    // =========================================================
    // REFRESH
    // =========================================================

    private void RefreshSnapshot()
    {
        try
        {
            PcWorldState sensed =
                _awareness.Read();


            PcWorldState snapshot =
                BuildSnapshot(
                    sensed);


            Interlocked.Exchange(
                ref _current,
                snapshot);


            Interlocked.Increment(
                ref _version);


            PublishSnapshot(
                snapshot);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[PcWorld] REFRESH ERROR: {ex}");
        }
    }


    // =========================================================
    // BUILD WORLD SNAPSHOT
    //
    // PcAwarenessService owns raw Windows sensing.
    // SegaPresenceService owns raw physical body facts.
    // PcWorldStateService combines those observations and
    // derives physical relationships between them.
    // =========================================================

    private PcWorldState BuildSnapshot(
        PcWorldState sensed)
    {
        SegaPresenceSnapshot presence =
            _segaPresence.Current;


        PcSegaPresenceState sega =
            BuildSegaState(
                sensed,
                presence);


        return new PcWorldState
        {
            Timestamp =
                sensed.Timestamp,

            User =
                sensed.User,

            Mouse =
                sensed.Mouse,

            ForegroundWindow =
                sensed.ForegroundWindow,

            Display =
                sensed.Display,

            Sega =
                sega
        };
    }


    // =========================================================
    // BUILD SEGA STATE
    // =========================================================

    private PcSegaPresenceState BuildSegaState(
        PcWorldState world,
        SegaPresenceSnapshot presence)
    {
        if (
            !presence.IsAvailable
            ||
            presence.WindowHandle ==
                IntPtr.Zero)
        {
            return new PcSegaPresenceState();
        }


        PcDisplayState segaDisplay =
            _awareness
                .ReadDisplayForWindow(
                    presence.WindowHandle);


        PcRectangle bodyBounds =
            presence.BodyBounds;


        // =====================================================
        // CURSOR RELATIONSHIP
        // =====================================================

        double mouseDx =
            world.Mouse.X -
            bodyBounds.CenterX;


        double mouseDy =
            world.Mouse.Y -
            bodyBounds.CenterY;


        double mouseDistance =
            Math.Sqrt(
                mouseDx *
                    mouseDx
                +
                mouseDy *
                    mouseDy);


        bool mouseOverBody =
            presence.IsVisible
            &&
            bodyBounds.Contains(
                world.Mouse.X,
                world.Mouse.Y);


        // =====================================================
        // MONITOR RELATIONSHIP
        // =====================================================

        bool sharesMonitor =
            !segaDisplay
                .MonitorBounds
                .IsEmpty
            &&
            !world
                .Display
                .MonitorBounds
                .IsEmpty
            &&
            segaDisplay.MonitorBounds ==
                world.Display.MonitorBounds;


        // =====================================================
        // FOREGROUND RELATIONSHIP
        // =====================================================

        PcForegroundWindowState foreground =
            world.ForegroundWindow;


        bool validExternalForeground =
            foreground.IsValid
            &&
            !foreground.IsMinimized
            &&
            foreground.Handle !=
                presence.WindowHandle;


        long intersectionArea =
            validExternalForeground
                ? bodyBounds.IntersectionArea(
                    foreground.Bounds)
                : 0;


        double overlapRatio =
            bodyBounds.Area >
                0
                ? Math.Clamp(
                    (double)intersectionArea /
                        bodyBounds.Area,
                    0.0,
                    1.0)
                : 0.0;


        bool overlapsForeground =
            presence.IsVisible
            &&
            overlapRatio >
                0.0;


        bool overlapsFullscreen =
            overlapsForeground
            &&
            foreground.IsFullscreen
            &&
            sharesMonitor;


        return new PcSegaPresenceState
        {
            IsAvailable =
                true,

            WindowHandle =
                presence.WindowHandle,

            WindowBounds =
                presence.WindowBounds,

            BodyBounds =
                bodyBounds,

            IsVisible =
                presence.IsVisible,

            IsFaded =
                presence.IsFaded,

            Display =
                segaDisplay,

            IsMouseOverBody =
                mouseOverBody,

            MouseDistanceFromBodyCenter =
                mouseDistance,

            SharesMonitorWithForeground =
                sharesMonitor,

            OverlapsForegroundWindow =
                overlapsForeground,

            ForegroundOverlapRatio =
                overlapRatio,

            OverlapsFullscreenContent =
                overlapsFullscreen
        };
    }


    // =========================================================
    // PUBLISH
    // =========================================================

    private void PublishSnapshot(
        PcWorldState snapshot)
    {
        Action<PcWorldState>?
            handlers =
                SnapshotUpdated;


        if (handlers ==
            null)
        {
            return;
        }


        foreach (
            Action<PcWorldState> handler
            in handlers.GetInvocationList())
        {
            try
            {
                handler(
                    snapshot);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[PcWorld] " +
                    $"SNAPSHOT SUBSCRIBER ERROR: {ex}");
            }
        }
    }
}
