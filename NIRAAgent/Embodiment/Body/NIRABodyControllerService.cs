/*
 * filename: NIRABodyControllerService.cs
 */

using System.Diagnostics;

using Microsoft.Extensions.Hosting;

using NIRAAgent.Agent.State;
using NIRAAgent.PC.Awareness;

namespace NIRAAgent.Embodiment.Body;


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

public sealed class NIRABodyControllerService
    : IHostedService
{
    private readonly PcWorldStateService
        _worldState;


    private readonly NIRAStateService
        _NIRAState;


    private readonly NIRABodyCommandService
        _commands;


    private readonly object
        _sync =
            new();


    private bool
        _hiddenForFullscreen;


    public NIRABodyControllerService(
        PcWorldStateService worldState,
        NIRAStateService NIRAState,
        NIRABodyCommandService commands)
    {
        _worldState =
            worldState
            ?? throw new ArgumentNullException(
                nameof(worldState));


        _NIRAState =
            NIRAState
            ?? throw new ArgumentNullException(
                nameof(NIRAState));


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
            "[NIRABodyController] STARTED");


        return Task.CompletedTask;
    }


    public Task StopAsync(
        CancellationToken cancellationToken)
    {
        _worldState.SnapshotUpdated -=
            WorldState_SnapshotUpdated;


        Debug.WriteLine(
            "[NIRABodyController] STOPPED");


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


        PcNIRAPresenceState NIRA =
            world.NIRA;


        if (!NIRA.IsAvailable)
        {
            return;
        }


        // =====================================================
        // DIRECT USER CONTROL ALWAYS WINS
        // =====================================================

        if (
            _NIRAState
                .Current
                .Body ==
            NIRABodyState.Dragging)
        {
            return;
        }


        PcForegroundWindowState foreground =
            world.ForegroundWindow;


        bool fullscreenOnNIRAMonitor =
            foreground.IsValid
            &&
            !foreground.IsMinimized
            &&
            foreground.IsFullscreen
            &&
            NIRA.SharesMonitorWithForeground
            &&
            foreground.Handle !=
                NIRA.WindowHandle;


        NIRABodyCommand?
            command =
                null;


        lock (_sync)
        {
            // =================================================
            // ENTER FULLSCREEN QUIET MODE
            // =================================================

            if (
                fullscreenOnNIRAMonitor
                &&
                NIRA.IsVisible
                &&
                NIRA.OverlapsFullscreenContent
                &&
                !_hiddenForFullscreen)
            {
                _hiddenForFullscreen =
                    true;


                command =
                    NIRABodyCommand.Hide(
                        NIRABodyCommandSource
                            .WorldPolicy);
            }


            // =================================================
            // LEAVE FULLSCREEN QUIET MODE
            // =================================================

            else if (
                !fullscreenOnNIRAMonitor
                &&
                _hiddenForFullscreen)
            {
                _hiddenForFullscreen =
                    false;


                command =
                    NIRABodyCommand.Show(
                        NIRABodyCommandSource
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

