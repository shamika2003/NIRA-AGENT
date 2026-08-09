/*
 * filename: AgentBackgroundProcessor.cs
 */

using SegaAgent.Perception;

namespace SegaAgent.Agent;

public sealed class AgentBackgroundProcessor
{
    private readonly AgentCore _agent;

    private readonly AgentResponseDispatcher _dispatcher;


    public AgentBackgroundProcessor(
        AgentCore agent,
        AgentResponseDispatcher dispatcher)
    {
        _agent = agent;

        _dispatcher = dispatcher;
    }


    // =========================================================
    // PROCESS ACCEPTED PERCEPTION
    // =========================================================

    public async Task ProcessPerceptionAsync(
        PerceptionEvent perception,
        CancellationToken cancellationToken = default)
    {
        if (perception == null)
        {
            return;
        }

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


    // =========================================================
    // PROCESS ACCEPTED PROACTIVE EVENT
    // =========================================================

    public async Task ProcessProactiveAsync(
        PerceptionEvent perception,
        CancellationToken cancellationToken = default)
    {
        if (perception == null)
        {
            return;
        }

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
}