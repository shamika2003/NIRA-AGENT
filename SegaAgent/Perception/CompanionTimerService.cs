/*
 * filename: CompanionTimerService.cs
 */

using Microsoft.Extensions.Hosting;
using SegaAgent.Agent;
using SegaAgent.PC.Awareness;

namespace SegaAgent.Perception;

public sealed class CompanionTimerService : BackgroundService
{
    private readonly PcAwarenessService _pcAwareness;

    private readonly AttentionManager _attention;

    private readonly AgentCore _agent;


    public CompanionTimerService(
        PcAwarenessService pcAwareness,
        AttentionManager attention,
        AgentCore agent)
    {
        _pcAwareness = pcAwareness;

        _attention = attention;

        _agent = agent;
    }


    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        using var timer =
            new PeriodicTimer(
                TimeSpan.FromMinutes(10));


        while (
            await timer.WaitForNextTickAsync(
                stoppingToken))
        {
            try
            {
                // =================================================
                // IMPORTANT
                //
                // This checks time since the USER LAST TALKED
                // TO SEGA.
                //
                // It is NOT PcState.UserIdleTime.
                // =================================================

                if (!_attention.CanRunProactiveCheck())
                    continue;


                var currentState =
                    _pcAwareness.Read();


                var perception =
                    new PerceptionEvent
                    {
                        Type =
                            "CompanionCheck",

                        Description =
                            "The user has not interacted with Sega recently. " +
                            "Make a natural, friendly companion-style check-in " +
                            "based on the current PC context. " +
                            "Do not sound like a monitoring system.",

                        CurrentState =
                            currentState
                    };


                await foreach (
                    var chunk
                    in _agent.ProcessProactiveAsync(
                        perception,
                        stoppingToken))
                {
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}