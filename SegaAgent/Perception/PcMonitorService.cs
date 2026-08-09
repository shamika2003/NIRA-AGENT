/*
 * filename: PcMonitorService.cs
 */

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
        PcState? previousState = null;


        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var currentState =
                    _pcAwareness.Read();


                // =================================================
                // 1. CHECK FOR NEW EVENT
                // =================================================

                var perception =
                    _analyzer.Analyze(
                        previousState,
                        currentState);


                if (perception != null)
                {
                    if (_attention.TryAcceptPerception(
                            perception))
                    {
                        await foreach (
                            var chunk
                            in _agent.ProcessPerceptionAsync(
                                perception,
                                stoppingToken))
                        {
                            // AgentCore publishes autonomous
                            // output to the UI.
                        }
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
                break;
            }
        }
    }
}