/*
 * filename: PcMonitorService.cs
 */

using System.Diagnostics;

using Microsoft.Extensions.Hosting;

using SegaAgent.Mind;
using SegaAgent.Character.History;
using SegaAgent.PC.Awareness;

namespace SegaAgent.Perception;

public sealed class PcMonitorService
    : BackgroundService
{
    private readonly PcWorldStateService
        _worldState;


    private readonly PerceptionAnalyzer
        _analyzer;


    private readonly SegaSocialHistoryService
        _socialHistory;


    private readonly AttentionManager
        _attention;


    private readonly SegaBackgroundProcessor
        _backgroundProcessor;


    public PcMonitorService(
        PcWorldStateService worldState,
        PerceptionAnalyzer analyzer,
        AttentionManager attention,
        SegaBackgroundProcessor backgroundProcessor,
        SegaSocialHistoryService socialHistory)
    {
        _worldState =
            worldState
            ?? throw new ArgumentNullException(
                nameof(worldState));


        _analyzer =
            analyzer
            ?? throw new ArgumentNullException(
                nameof(analyzer));


        _attention =
            attention
            ?? throw new ArgumentNullException(
                nameof(attention));


        _backgroundProcessor =
            backgroundProcessor
            ?? throw new ArgumentNullException(
                nameof(backgroundProcessor));


        _socialHistory =
            socialHistory
            ?? throw new ArgumentNullException(
                nameof(socialHistory));
    }


    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        Debug.WriteLine(
            "[PcMonitor] SERVICE STARTED");


        PcWorldState?
            previousState =
                null;


        using PeriodicTimer timer =
            new(
                TimeSpan.FromSeconds(
                    1));


        try
        {
            while (!stoppingToken
                .IsCancellationRequested)
            {
                PcWorldState currentState =
                    _worldState.Current;


                PcForegroundWindowState window =
                    currentState.ForegroundWindow;


                Debug.WriteLine(
                    $"[PcMonitor] " +
                    $"WorldVersion={_worldState.Version} | " +
                    $"Process='{window.ProcessName}' | " +
                    $"PID={window.ProcessId} | " +
                    $"Window='{window.Title}' | " +
                    $"Fullscreen={window.IsFullscreen} | " +
                    $"Idle=" +
                    $"{currentState.User.IdleTime.TotalSeconds:F0}s");


                PerceptionEvent? perception =
                    _analyzer.Analyze(
                        previousState,
                        currentState);


                if (perception !=
                    null)
                {
                    Debug.WriteLine(
                        $"[PcMonitor] EVENT DETECTED: " +
                        $"{perception.Type}");


                    if (!_attention.IsPerceptionEnabled(
                            perception))
                    {
                        Debug.WriteLine(
                            $"[PcMonitor] EVENT IGNORED BY SETTINGS: " +
                            $"{perception.Type}");
                    }
                    else
                    {
                        SegaSocialEvent
                            socialEvent =
                                _socialHistory.Record(
                                    SegaSocialEventSource.Environment,
                                    SegaSocialEventKind.EnvironmentEvent,
                                    perception.Type,
                                    perception.TopicKey,
                                    perception.Description,
                                    perception.Metadata);


                        perception.SocialEventId =
                            socialEvent.Id;


                        bool accepted =
                            _attention
                                .TryAcceptPerception(
                                    perception);


                        Debug.WriteLine(
                            $"[PcMonitor] " +
                            $"Attention accepted = {accepted}");


                        if (accepted)
                        {
                            await _backgroundProcessor
                                .ProcessPerceptionAsync(
                                    perception,
                                    stoppingToken);
                        }
                    }
                }


                if (
                    _attention.TryTakePending(
                        currentState,
                        out PerceptionEvent?
                            pending)
                    &&
                    pending !=
                    null)
                {
                    await _backgroundProcessor
                        .ProcessPerceptionAsync(
                            pending,
                            stoppingToken);
                }


                previousState =
                    currentState;


                await timer.WaitForNextTickAsync(
                    stoppingToken);
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken
                .IsCancellationRequested)
        {
        }


        Debug.WriteLine(
            "[PcMonitor] SERVICE STOPPED");
    }
}