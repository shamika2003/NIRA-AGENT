/*
 * filename: AgentBackgroundProcessor.cs
 */

using SegaAgent.Perception;

namespace SegaAgent.Agent;

public sealed class AgentBackgroundProcessor
{
    private readonly AgentCore _agent;

    private readonly AgentResponseDispatcher _dispatcher;


    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public AgentBackgroundProcessor(
        AgentCore agent,
        AgentResponseDispatcher dispatcher)
    {
        _agent =
            agent;


        _dispatcher =
            dispatcher;
    }


    // =========================================================
    // PERCEPTION
    // =========================================================

    public async Task ProcessPerceptionAsync(
        PerceptionEvent perception,
        CancellationToken cancellationToken = default)
    {
        if (perception == null)
        {
            return;
        }


        try
        {
            await foreach (
                var chunk
                in _agent.ProcessPerceptionAsync(
                    perception,
                    cancellationToken))
            {
                await _dispatcher.PublishAsync(
                    new AgentResponse(
                        AgentRequestSource.Perception,
                        chunk),
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            /*
             * Sega's autonomous processing was interrupted
             * because the user started talking.
             *
             * This is NOT application shutdown.
             */

            await PublishCancelledAsync(
                AgentRequestSource.Perception);
        }
    }


    // =========================================================
    // PROACTIVE
    // =========================================================

    public async Task ProcessProactiveAsync(
        PerceptionEvent perception,
        CancellationToken cancellationToken = default)
    {
        if (perception == null)
        {
            return;
        }


        try
        {
            await foreach (
                var chunk
                in _agent.ProcessProactiveAsync(
                    perception,
                    cancellationToken))
            {
                await _dispatcher.PublishAsync(
                    new AgentResponse(
                        AgentRequestSource.Proactive,
                        chunk),
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            await PublishCancelledAsync(
                AgentRequestSource.Proactive);
        }
    }


    // =========================================================
    // CANCELLED
    // =========================================================

    private async Task PublishCancelledAsync(
        AgentRequestSource source)
    {
        await _dispatcher.PublishAsync(
            new AgentResponse(
                source,
                new AgentStreamChunk
                {
                    Type =
                        AgentStreamChunkType.Cancelled
                }));
    }
}