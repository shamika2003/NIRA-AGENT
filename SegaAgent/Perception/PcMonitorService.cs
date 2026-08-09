/*
 * filename: PcMonitorService.cs
 */

using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using SegaAgent.Agent;
using SegaAgent.PC.Awareness;

namespace SegaAgent.Perception;

public sealed class PcMonitorService : BackgroundService
{
    private readonly PcAwarenessService _pcAwareness;

    private readonly PerceptionAnalyzer _analyzer;

    private readonly AttentionManager _attention;

    private readonly AgentCore _agent;


    public PcMonitorService(
        PcAwarenessService pcAwareness,
        PerceptionAnalyzer analyzer,
        AttentionManager attention,
        AgentCore agent)
    {
        _pcAwareness = pcAwareness;
        _analyzer = analyzer;
        _attention = attention;
        _agent = agent;
    }


    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        Debug.WriteLine(
            "[PcMonitor] SERVICE STARTED");


        PcState? previousState = null;


        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var currentState =
                    _pcAwareness.Read();


                Debug.WriteLine(
                    $"[PcMonitor] " +
                    $"App='{currentState.ActiveApplication}' | " +
                    $"Idle={currentState.UserIdleTime.TotalSeconds:F0}s");


                // =================================================
                // 1. CHECK FOR NEW EVENT
                // =================================================

                var perception =
                    _analyzer.Analyze(
                        previousState,
                        currentState);


                if (perception != null)
                {
                    Debug.WriteLine(
                        $"[PcMonitor] EVENT DETECTED: " +
                        $"{perception.Type}");


                    var accepted =
                        _attention.TryAcceptPerception(
                            perception);


                    Debug.WriteLine(
                        $"[PcMonitor] " +
                        $"Attention accepted = {accepted}");


                    if (accepted)
                    {
                        Debug.WriteLine(
                            "[PcMonitor] " +
                            "STARTING PERCEPTION AGENT");


                        await foreach (
                            var chunk
                            in _agent.ProcessPerceptionAsync(
                                perception,
                                stoppingToken))
                        {
                            Debug.WriteLine(
                                $"[PcMonitor] " +
                                $"Agent chunk: " +
                                $"{chunk.Type}");
                        }


                        Debug.WriteLine(
                            "[PcMonitor] " +
                            "PERCEPTION AGENT FINISHED");
                    }
                }


                // =================================================
                // 2. CHECK DEFERRED EVENT
                // =================================================

                if (_attention.TryTakePending(
                        currentState,
                        out var pending) &&
                    pending != null)
                {
                    Debug.WriteLine(
                        $"[PcMonitor] " +
                        $"PROCESSING PENDING EVENT: " +
                        $"{pending.Type}");


                    await foreach (
                        var chunk
                        in _agent.ProcessPerceptionAsync(
                            pending,
                            stoppingToken))
                    {
                    }
                }


                previousState =
                    currentState;


                await Task.Delay(
                    TimeSpan.FromSeconds(5),
                    stoppingToken);
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine(
                    "[PcMonitor] SERVICE CANCELED");

                break;
            }
            catch (Exception ex)
            {
                // IMPORTANT:
                //
                // Do not allow one unexpected monitoring
                // exception to silently kill the monitoring
                // loop.

                Debug.WriteLine(
                    $"[PcMonitor] ERROR: {ex}");


                await Task.Delay(
                    TimeSpan.FromSeconds(5),
                    stoppingToken);
            }
        }


        Debug.WriteLine(
            "[PcMonitor] SERVICE STOPPED");
    }
}