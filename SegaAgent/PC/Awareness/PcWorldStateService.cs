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

    /*
     * Windows sensing is cheap and local.
     *
     * This does NOT call the AI.
     *
     * 500 ms gives Sega reasonably fresh environmental
     * awareness without aggressive polling.
     */

    private static readonly TimeSpan
        RefreshInterval =
            TimeSpan.FromMilliseconds(500);


    // =========================================================
    // DEPENDENCIES
    // =========================================================

    private readonly PcAwarenessService
        _awareness;


    // =========================================================
    // CURRENT SNAPSHOT
    // =========================================================

    private PcWorldState _current;


    private long _version;


    // =========================================================
    // EVENTS
    // =========================================================

    public event Action<PcWorldState>?
        SnapshotUpdated;


    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public PcWorldStateService(
        PcAwarenessService awareness)
    {
        _awareness =
            awareness
            ?? throw new ArgumentNullException(
                nameof(awareness));


        /*
         * Capture an initial state immediately.
         *
         * This means consumers always have a valid snapshot,
         * even before the background refresh loop performs its
         * first iteration.
         */

        _current =
            _awareness.Read();


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
    //
    // Useful later for tools and perception that need to know
    // whether the world changed since a previous observation.
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
            when (stoppingToken
                .IsCancellationRequested)
        {
            // Normal application shutdown.
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
            PcWorldState snapshot =
                _awareness.Read();


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
            /*
             * A temporary Windows sensing failure should not
             * destroy Sega's world-state service.
             *
             * The previous valid snapshot remains available.
             */

            Debug.WriteLine(
                $"[PcWorld] REFRESH ERROR: {ex}");
        }
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
                /*
                 * A future subscriber must never be allowed
                 * to terminate the world-state service.
                 */

                Debug.WriteLine(
                    $"[PcWorld] " +
                    $"SNAPSHOT SUBSCRIBER ERROR: {ex}");
            }
        }
    }
}