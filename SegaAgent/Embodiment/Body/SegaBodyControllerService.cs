/*
 * filename: SegaBodyControllerService.cs
 */

using System.Diagnostics;

using Microsoft.Extensions.Hosting;

using SegaAgent.Agent.State;
using SegaAgent.PC.Awareness;

namespace SegaAgent.Embodiment.Body;


// =============================================================
// BODY CONTROLLER
//
// World state observes.
// This controller decides.
// CompanionWindow executes.
//
// This service does not read Win32 directly, manipulate WPF,
// call an LLM or modify the world snapshot.
// =============================================================

public sealed class SegaBodyControllerService
    : IHostedService
{
    private readonly PcWorldStateService
        _worldState;


    private readonly SegaStateService
        _segaState;


    private readonly SegaBodyCommandService
        _commands;


    private readonly object
        _sync =
            new();


    private bool
        _hiddenForFullscreen;


    public SegaBodyControllerService(
        PcWorldStateService worldState,
        SegaStateService segaState,
        SegaBodyCommandService commands)
    {
        _worldState =
            worldState
            ?? throw new ArgumentNullException(
                nameof(worldState));


        _segaState =
            segaState
            ?? throw new ArgumentNullException(
                nameof(segaState));


        _commands =
            commands
            ?? throw new ArgumentNullException(
                nameof(commands));
    }


    public Task StartAsync(
        CancellationToken cancellationToken)
    {
        _worldState.SnapshotUpdated +=
            WorldState_SnapshotUpdated;


        Evaluate(
            _worldState.Current);


        Debug.WriteLine(
            "[SegaBodyController] STARTED");


        return Task.CompletedTask;
    }


    public Task StopAsync(
        CancellationToken cancellationToken)
    {
        _worldState.SnapshotUpdated -=
            WorldState_SnapshotUpdated;


        Debug.WriteLine(
            "[SegaBodyController] STOPPED");


        return Task.CompletedTask;
    }


    private void WorldState_SnapshotUpdated(
        PcWorldState world)
    {
        Evaluate(
            world);
    }


    private void Evaluate(
        PcWorldState world)
    {
        ArgumentNullException.ThrowIfNull(
            world);


        PcSegaPresenceState sega =
            world.Sega;


        if (!sega.IsAvailable)
        {
            return;
        }


        // =====================================================
        // DIRECT USER CONTROL ALWAYS WINS
        // =====================================================

        if (
            _segaState
                .Current
                .Body ==
            SegaBodyState.Dragging)
        {
            return;
        }


        PcForegroundWindowState foreground =
            world.ForegroundWindow;


        bool fullscreenOnSegaMonitor =
            foreground.IsValid
            &&
            !foreground.IsMinimized
            &&
            foreground.IsFullscreen
            &&
            sega.SharesMonitorWithForeground
            &&
            foreground.Handle !=
                sega.WindowHandle;


        SegaBodyCommand?
            command =
                null;


        lock (_sync)
        {
            // =================================================
            // ENTER FULLSCREEN QUIET MODE
            // =================================================

            if (
                fullscreenOnSegaMonitor
                &&
                sega.IsVisible
                &&
                sega.OverlapsFullscreenContent
                &&
                !_hiddenForFullscreen)
            {
                _hiddenForFullscreen =
                    true;


                command =
                    SegaBodyCommand.Hide(
                        SegaBodyCommandSource
                            .WorldPolicy);
            }


            // =================================================
            // LEAVE FULLSCREEN QUIET MODE
            // =================================================

            else if (
                !fullscreenOnSegaMonitor
                &&
                _hiddenForFullscreen)
            {
                _hiddenForFullscreen =
                    false;


                command =
                    SegaBodyCommand.Show(
                        SegaBodyCommandSource
                            .WorldPolicy);
            }
        }


        if (command !=
            null)
        {
            _commands.Issue(
                command);
        }
    }
}
